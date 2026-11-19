import json
import os
import io
import uuid
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
import boto3
import pandas as pd
from botocore.exceptions import ClientError

# Configuração de ambiente
LOCAL_HOST = os.environ.get("LOCALSTACK_HOSTNAME", "localhost")
ENDPOINT_URL = f"http://{LOCAL_HOST}:4566"
# URL da fila SQS definida como variável de ambiente
SCRAPER_QUEUE_URL = os.environ.get("SCRAPER_QUEUE_URL", "http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/scraper-queue")

s3_client = boto3.client("s3", endpoint_url=ENDPOINT_URL)
dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
dynamo_client = boto3.client("dynamodb", endpoint_url=ENDPOINT_URL)
sqs_client = boto3.client("sqs", endpoint_url=ENDPOINT_URL)
TABLE_NAME = os.environ.get("TABLE_NAME", "FiiAnalyticsDb")
table = dynamodb.Table(TABLE_NAME)

def agora():
    return datetime.now(timezone.utc).isoformat()


def atualizar_status(usuario_id, importacao_id, status, mensagem):
    table.update_item(
        Key={"PK": f"USER#{usuario_id}", "SK": f"IMPORTACAO#{importacao_id}"},
        ConditionExpression="attribute_exists(PK) AND #status <> :concluido AND #status <> :superada",
        UpdateExpression="SET #status = :status, AtualizadoEm = :atualizado, Mensagem = :mensagem",
        ExpressionAttributeNames={"#status": "Status"},
        ExpressionAttributeValues={
            ":status": status, ":atualizado": agora(), ":mensagem": mensagem,
            ":concluido": "CONCLUIDO", ":superada": "SUPERADA",
        },
    )


def numero_decimal(valor, coluna):
    if pd.isna(valor):
        raise ValueError(f"Valor ausente na coluna {coluna}.")
    try:
        numero = Decimal(valor.strip())
    except InvalidOperation as exc:
        raise ValueError(f"Valor inválido na coluna {coluna}.") from exc
    if not numero.is_finite():
        raise ValueError(f"Valor inválido na coluna {coluna}.")
    return numero


def ativar_carteira(usuario_id, importacao_id):
    pk = f"USER#{usuario_id}"
    try:
        dynamo_client.transact_write_items(TransactItems=[
            {"Update": {
                "TableName": TABLE_NAME,
                "Key": {"PK": {"S": pk}, "SK": {"S": "IMPORTACAO#CONTROLE"}},
                "ConditionExpression": "UltimaSolicitada = :id",
                "UpdateExpression": "SET CarteiraAtiva = :id",
                "ExpressionAttributeValues": {":id": {"S": importacao_id}},
            }},
            {"Update": {
                "TableName": TABLE_NAME,
                "Key": {"PK": {"S": pk}, "SK": {"S": f"IMPORTACAO#{importacao_id}"}},
                "ConditionExpression": "attribute_exists(PK)",
                "UpdateExpression": "SET #status = :status, AtualizadoEm = :atualizado, Mensagem = :mensagem",
                "ExpressionAttributeNames": {"#status": "Status"},
                "ExpressionAttributeValues": {
                    ":status": {"S": "CONCLUIDO"},
                    ":atualizado": {"S": agora()},
                    ":mensagem": {"S": "Carteira salva. As cotações podem estar em atualização."},
                },
            }},
        ])
    except ClientError as exc:
        if exc.response["Error"]["Code"] != "TransactionCanceledException":
            raise
        controle = table.get_item(
            Key={"PK": pk, "SK": "IMPORTACAO#CONTROLE"}, ConsistentRead=True
        ).get("Item", {})
        if "UltimaSolicitada" not in controle or controle["UltimaSolicitada"] == importacao_id:
            raise
        atualizar_status(usuario_id, importacao_id, "SUPERADA", "Uma importação mais recente substituiu esta solicitação.")


def lambda_handler(event, context):
    print(f"Evento recebido: {json.dumps(event)}")
    
    try:
        records = event["Records"] if "Records" in event else [{"body": json.dumps(event)}]

        for record in records:
            body = json.loads(record["body"])
            usuario_id = body.get("UsuarioId")
            s3_key = body.get("S3Key")
            bucket_name = body.get("BucketName")
            importacao_id = body.get("ImportacaoId")

            if not usuario_id or not s3_key or not bucket_name or not importacao_id:
                raise ValueError("UsuarioId, S3Key, BucketName e ImportacaoId são obrigatórios.")
            importacao_id = str(uuid.UUID(importacao_id))
            job = table.get_item(
                Key={"PK": f"USER#{usuario_id}", "SK": f"IMPORTACAO#{importacao_id}"},
                ConsistentRead=True,
            ).get("Item")
            if not job:
                raise ValueError("Importação não registrada.")
            if job.get("Status") in ("CONCLUIDO", "SUPERADA"):
                continue
            atualizar_status(usuario_id, importacao_id, "PROCESSANDO", "Processando a carteira.")

            print(f"Processando arquivo {s3_key} para {usuario_id}")

            try:
                # 1. Busca arquivo no S3
                s3_object = s3_client.get_object(Bucket=bucket_name, Key=s3_key)
                file_content = s3_object["Body"].read().decode("utf-8-sig")
                df = pd.read_csv(io.StringIO(file_content), dtype=str)
            
                # 2. Limpeza e Normalização
                df.columns = df.columns.str.strip()
                if df.empty or df['Ticker'].isna().any():
                    raise ValueError("A carteira deve conter tickers válidos.")
                df['Ticker'] = df['Ticker'].astype(str).str.strip().str.upper()
                if df['Ticker'].eq("").any():
                    raise ValueError("A carteira deve conter tickers válidos.")
                coluna_preco = 'PrecoCompra' if 'PrecoCompra' in df.columns else 'PrecoMedio'
                for coluna in ('Quantidade', coluna_preco, 'TotalProventos'):
                    df[coluna] = df[coluna].map(lambda valor: numero_decimal(valor, coluna))
                if df['Quantidade'].map(lambda valor: valor <= 0 or valor != valor.to_integral_value()).any():
                    raise ValueError("A quantidade deve ser um inteiro positivo.")
                if (df[coluna_preco] < 0).any():
                    raise ValueError("O preço não pode ser negativo.")
                df['CustoTotal'] = df['Quantidade'] * df[coluna_preco]
            
                carteira_consolidada = df.groupby('Ticker').agg(
                    QuantidadeTotal=('Quantidade', 'sum'),
                    CustoTotalAcumulado=('CustoTotal', 'sum'),
                    TotalProventosAcumulado=('TotalProventos', 'sum')
                ).reset_index()
            
                tickers = []
                with table.batch_writer() as batch:
                    for _, row in carteira_consolidada.iterrows():
                        ticker = row['Ticker']
                        quantidade = int(row['QuantidadeTotal'])
                        if quantidade > 2147483647:
                            raise ValueError("A quantidade consolidada excede o limite suportado.")
                        preco_medio = (row['CustoTotalAcumulado'] / row['QuantidadeTotal']).quantize(
                            Decimal("0.000001"), rounding=ROUND_HALF_UP
                        )
                        batch.put_item(Item={
                            "PK": f"SNAPSHOT#{usuario_id}#{importacao_id}",
                            "SK": f"ATIVO#{ticker}",
                            "Ticker": ticker,
                            "Quantidade": quantidade,
                            "PrecoMedio": str(preco_medio),
                            "TotalProventos": str(row['TotalProventosAcumulado'])
                        })
                        tickers.append(ticker)

                for ticker in tickers:
                    sqs_client.send_message(
                        QueueUrl=SCRAPER_QUEUE_URL,
                        MessageBody=json.dumps({"ticker": ticker})
                    )
                ativar_carteira(usuario_id, importacao_id)
            except Exception:
                tentativas = int(record.get("attributes", {}).get("ApproximateReceiveCount", "1"))
                status = "FALHOU" if tentativas >= 5 else "RETENTANDO"
                atualizar_status(usuario_id, importacao_id, status, "Não foi possível processar a carteira; consulte os logs.")
                raise
            
        return {"statusCode": 200, "body": json.dumps("Processamento concluído e ativos enfileirados.")}

    except Exception as e:
        print(f"Erro crítico: {str(e)}")
        raise