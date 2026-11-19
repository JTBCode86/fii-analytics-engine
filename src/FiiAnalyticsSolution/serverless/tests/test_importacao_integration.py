import io
import json
import os
import unittest
import uuid
from unittest.mock import MagicMock, patch

import boto3

from test_handlers import load_handler


@unittest.skipUnless(os.environ.get("FII_TEST_DYNAMODB_ENDPOINT"), "LocalStack não configurado")
class ImportacaoIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.handler = load_handler("processar_carteira")
        endpoint = os.environ["FII_TEST_DYNAMODB_ENDPOINT"]
        session = boto3.Session(aws_access_key_id="test", aws_secret_access_key="test", region_name="us-east-1")
        cls.client = session.client("dynamodb", endpoint_url=endpoint)
        cls.resource = session.resource("dynamodb", endpoint_url=endpoint)
        cls.table_name = f"FiiTest{uuid.uuid4().hex}"
        cls.client.create_table(
            TableName=cls.table_name,
            AttributeDefinitions=[{"AttributeName": "PK", "AttributeType": "S"}, {"AttributeName": "SK", "AttributeType": "S"}],
            KeySchema=[{"AttributeName": "PK", "KeyType": "HASH"}, {"AttributeName": "SK", "KeyType": "RANGE"}],
            BillingMode="PAY_PER_REQUEST",
        )
        cls.client.get_waiter("table_exists").wait(TableName=cls.table_name)
        cls.table = cls.resource.Table(cls.table_name)

    @classmethod
    def tearDownClass(cls):
        cls.table.delete()

    def registrar(self, usuario, importacao):
        self.table.put_item(Item={"PK": f"USER#{usuario}", "SK": f"IMPORTACAO#{importacao}", "Status": "PENDENTE"})
        self.table.update_item(
            Key={"PK": f"USER#{usuario}", "SK": "IMPORTACAO#CONTROLE"},
            UpdateExpression="SET UltimaSolicitada = :id",
            ExpressionAttributeValues={":id": importacao},
        )

    def processar(self, usuario, importacao, csv, falha_fila=False):
        s3 = MagicMock()
        s3.get_object.return_value = {"Body": io.BytesIO(csv.encode())}
        fila = MagicMock()
        if falha_fila:
            fila.send_message.side_effect = RuntimeError("fila indisponível")
        event = {"Records": [{"body": json.dumps({
            "UsuarioId": usuario, "ImportacaoId": importacao, "S3Key": "carteira.csv", "BucketName": "bucket"
        })}]}
        with patch.object(self.handler, "table", self.table), patch.object(self.handler, "TABLE_NAME", self.table_name), \
                patch.object(self.handler, "dynamo_client", self.client), patch.object(self.handler, "s3_client", s3), \
                patch.object(self.handler, "sqs_client", fila):
            self.handler.lambda_handler(event, None)

    def ler(self, pk, sk):
        return self.table.get_item(Key={"PK": pk, "SK": sk}, ConsistentRead=True).get("Item")

    def test_substituicao_falha_e_importacao_superada(self):
        usuario = uuid.uuid4().hex
        primeira, antiga, nova, falha = [str(uuid.uuid4()) for _ in range(4)]
        csv_a = "Ticker,Quantidade,PrecoCompra,TotalProventos\nHGLG11,2,10.01,0.10\n"
        csv_b = "Ticker,Quantidade,PrecoCompra,TotalProventos\nXPLG11,1,20,0\n"

        self.registrar(usuario, primeira)
        self.processar(usuario, primeira, csv_a)
        self.assertEqual(primeira, self.ler(f"USER#{usuario}", "IMPORTACAO#CONTROLE")["CarteiraAtiva"])
        self.assertEqual("10.010000", self.ler(f"SNAPSHOT#{usuario}#{primeira}", "ATIVO#HGLG11")["PrecoMedio"])

        self.registrar(usuario, antiga)
        self.registrar(usuario, nova)
        self.processar(usuario, nova, csv_b)
        self.processar(usuario, antiga, csv_a)
        controle = self.ler(f"USER#{usuario}", "IMPORTACAO#CONTROLE")
        self.assertEqual(nova, controle["CarteiraAtiva"])
        self.assertIsNone(self.ler(f"SNAPSHOT#{usuario}#{nova}", "ATIVO#HGLG11"))
        self.assertIsNotNone(self.ler(f"SNAPSHOT#{usuario}#{nova}", "ATIVO#XPLG11"))
        self.assertEqual("SUPERADA", self.ler(f"USER#{usuario}", f"IMPORTACAO#{antiga}")["Status"])
        concluida = self.ler(f"USER#{usuario}", f"IMPORTACAO#{nova}")
        self.assertEqual("CONCLUIDO", concluida["Status"])
        self.assertTrue(concluida["AtualizadoEm"])

        self.registrar(usuario, falha)
        with self.assertRaisesRegex(RuntimeError, "fila indisponível"):
            self.processar(usuario, falha, csv_a, falha_fila=True)
        self.assertEqual(nova, self.ler(f"USER#{usuario}", "IMPORTACAO#CONTROLE")["CarteiraAtiva"])
        self.assertEqual("RETENTANDO", self.ler(f"USER#{usuario}", f"IMPORTACAO#{falha}")["Status"])
