import importlib.util
import io
import json
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch
from botocore.exceptions import ClientError


def load_handler(name):
    path = Path(__file__).resolve().parents[1] / "lambdas" / name / "handler.py"
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    with patch("boto3.client"), patch("boto3.resource"):
        spec.loader.exec_module(module)
    return module


processar = load_handler("processar_carteira")
scraper = load_handler("scraper_ativos")
IMPORTACAO_ID = "bcecb77e-3c45-4fd8-8580-1e5d8a6e31b8"


def evento(usuario_id="123"):
    return {"UsuarioId": usuario_id, "S3Key": "carteira.csv", "BucketName": "bucket", "ImportacaoId": IMPORTACAO_ID}


def tabela_pendente():
    table = MagicMock()
    table.get_item.return_value = {"Item": {"Status": "PENDENTE"}}
    return table


class ProcessarCarteiraTests(unittest.TestCase):
    def test_falha_ao_enfileirar_e_propagada_apos_gravacao(self):
        body = evento()
        s3 = MagicMock()
        s3.get_object.return_value = {
            "Body": io.BytesIO(b"Ticker,Quantidade,PrecoCompra,TotalProventos\nHGLG11,2,10,1\n")
        }
        table = tabela_pendente()
        queue = MagicMock()

        def falhar(**kwargs):
            table.batch_writer.return_value.__exit__.assert_called_once()
            raise RuntimeError("fila indisponivel")

        queue.send_message.side_effect = falhar
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table), \
                patch.object(processar, "sqs_client", queue):
            with self.assertRaisesRegex(RuntimeError, "fila indisponivel"):
                processar.lambda_handler({"Records": [{"body": json.dumps(body)}]}, None)

    def test_mensagem_sem_usuario_falha(self):
        body = {"S3Key": "carteira.csv", "BucketName": "bucket"}
        with self.assertRaisesRegex(ValueError, "UsuarioId"):
            processar.lambda_handler({"Records": [{"body": json.dumps(body)}]}, None)

    def test_quantidade_invalida_nao_altera_carteira(self):
        body = evento()
        s3 = MagicMock()
        s3.get_object.return_value = {
            "Body": io.BytesIO(b"Ticker,Quantidade,PrecoCompra,TotalProventos\nHGLG11,0,10,1\n")
        }
        table = tabela_pendente()
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table):
            with self.assertRaisesRegex(ValueError, "quantidade"):
                processar.lambda_handler({"Records": [{"body": json.dumps(body)}]}, None)
        table.batch_writer.assert_not_called()

    def test_importacao_enfileira_ticker_para_scraping(self):
        body = evento()
        s3 = MagicMock()
        s3.get_object.return_value = {
            "Body": io.BytesIO(b"Ticker,Quantidade,PrecoCompra,TotalProventos\nhglg11,2,10,1\n")
        }
        table = tabela_pendente()
        queue = MagicMock()
        dynamo = MagicMock()
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table), \
                patch.object(processar, "sqs_client", queue), patch.object(processar, "dynamo_client", dynamo):
            processar.lambda_handler({"Records": [{"body": json.dumps(body)}]}, None)

        item = table.batch_writer.return_value.__enter__.return_value.put_item.call_args.kwargs["Item"]
        self.assertEqual((f"SNAPSHOT#123#{IMPORTACAO_ID}", "ATIVO#HGLG11"), (item["PK"], item["SK"]))
        dynamo.transact_write_items.assert_called_once()
        event = {"Records": [{"body": queue.send_message.call_args.kwargs["MessageBody"]}]}
        with patch.object(scraper, "processar_scraping") as processar_scraping:
            scraper.lambda_handler(event, None)
        processar_scraping.assert_called_once_with("HGLG11")

    def test_preco_medio_e_calculado_em_decimal(self):
        s3 = MagicMock()
        s3.get_object.return_value = {
            "Body": io.BytesIO(b"Ticker,Quantidade,PrecoCompra,TotalProventos\nHGLG11,1,10.00,0.10\nHGLG11,1,10.01,0.20\n")
        }
        table = tabela_pendente()
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table), \
                patch.object(processar, "sqs_client", MagicMock()), patch.object(processar, "dynamo_client", MagicMock()):
            processar.lambda_handler({"Records": [{"body": json.dumps(evento())}]}, None)
        item = table.batch_writer.return_value.__enter__.return_value.put_item.call_args.kwargs["Item"]
        self.assertEqual("10.005000", item["PrecoMedio"])
        self.assertEqual("0.30", item["TotalProventos"])

    def test_importacao_mais_antiga_nao_sobrescreve_mais_recente(self):
        s3 = MagicMock()
        s3.get_object.return_value = {
            "Body": io.BytesIO(b"Ticker,Quantidade,PrecoCompra,TotalProventos\nHGLG11,1,10,0\n")
        }
        table = tabela_pendente()
        table.get_item.side_effect = [
            {"Item": {"Status": "PENDENTE"}},
            {"Item": {"UltimaSolicitada": "outra-importacao"}},
        ]
        dynamo = MagicMock()
        dynamo.transact_write_items.side_effect = ClientError(
            {"Error": {"Code": "TransactionCanceledException", "Message": "cancelado"}}, "TransactWriteItems"
        )
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table), \
                patch.object(processar, "sqs_client", MagicMock()), patch.object(processar, "dynamo_client", dynamo):
            processar.lambda_handler({"Records": [{"body": json.dumps(evento())}]}, None)
        self.assertEqual("SUPERADA", table.update_item.call_args.kwargs["ExpressionAttributeValues"][":status"])

    def test_ultima_tentativa_marca_falha(self):
        s3 = MagicMock()
        s3.get_object.side_effect = RuntimeError("erro no S3")
        table = tabela_pendente()
        record = {"body": json.dumps(evento()), "attributes": {"ApproximateReceiveCount": "5"}}
        with patch.object(processar, "s3_client", s3), patch.object(processar, "table", table):
            with self.assertRaises(RuntimeError):
                processar.lambda_handler({"Records": [record]}, None)
        self.assertEqual("FALHOU", table.update_item.call_args.kwargs["ExpressionAttributeValues"][":status"])


class ScraperTests(unittest.TestCase):
    def test_numero_malformado_nao_e_convertido_em_cotacao(self):
        with self.assertRaises(ValueError):
            scraper.formatar_valor("1.2.3")

    def test_scan_le_todas_as_paginas_e_remove_duplicatas(self):
        table = MagicMock()
        last_key = {"PK": "USER#123", "SK": "CARTEIRA#HGLG11"}
        table.scan.side_effect = [
            {"Items": [{"Ticker": "HGLG11"}], "LastEvaluatedKey": last_key},
            {"Items": [{"Ticker": "HGLG11"}, {"Ticker": "XPLG11"}, {}]},
        ]
        with patch.object(scraper, "table", table):
            self.assertEqual(["HGLG11", "XPLG11"], scraper.obter_todos_tickers())
        self.assertEqual(last_key, table.scan.call_args_list[1].kwargs["ExclusiveStartKey"])

    def test_scraping_invalido_preserva_metadados_e_falha(self):
        response = MagicMock()
        response.content = b"""
            <strong class="value">100,00</strong>
            <div class="pb-1">Dividend Yield<strong class="value">N/A</strong></div>
            <div class="pb-1">P/VP<strong class="value">1,20</strong></div>
        """
        table = MagicMock()
        with patch.object(scraper.requests, "get", return_value=response), patch.object(scraper, "table", table):
            with self.assertRaises(ValueError):
                scraper.processar_scraping("HGLG11")
        table.put_item.assert_not_called()

    def test_scraping_valido_persiste_valores(self):
        response = MagicMock()
        response.content = b"""
            <strong class="value">1.234,56</strong>
            <div class="pb-1">Dividend Yield<strong class="value">10,50%</strong></div>
            <div class="pb-1">P/VP<strong class="value">1,20</strong></div>
        """
        table = MagicMock()
        with patch.object(scraper.requests, "get", return_value=response) as get, patch.object(scraper, "table", table):
            scraper.processar_scraping("HGLG11")
        self.assertEqual(15, get.call_args.kwargs["timeout"])
        item = table.put_item.call_args.kwargs["Item"]
        self.assertEqual(("1234.56", "10.50", "1.20"), (item["Cotacao"], item["DividendYield"], item["PVP"]))

    def test_erro_no_scraping_propagado_ao_sqs(self):
        event = {"Records": [{"body": json.dumps({"ticker": "HGLG11"})}]}
        with patch.object(scraper, "processar_scraping", side_effect=RuntimeError("falhou")):
            with self.assertRaisesRegex(RuntimeError, "falhou"):
                scraper.lambda_handler(event, None)


if __name__ == "__main__":
    unittest.main()
