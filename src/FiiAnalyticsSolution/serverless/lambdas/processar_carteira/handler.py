import json
import os
import io
import boto3
import pandas as pd

# Configuração de ambiente
LOCAL_HOST = os.environ.get("LOCALSTACK_HOSTNAME", "localhost")
ENDPOINT_URL = f"http://{LOCAL_HOST}:4566"

s3_client = boto3.client("s3", endpoint_url=ENDPOINT_URL)
dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
table = dynamodb.Table("FiiAnalyticsDb")

def lambda_handler(event, context):
    print(f"Evento recebido: {json.dumps(event)}")
    
    try:
        # Identifica se o evento veio do SQS ou invocação direta
        records = []
        if "Records" in event:
            records = event["Records"]
        else:
            # Caso seja invocação direta, transforma o evento em uma lista única
            records = [{"body": json.dumps(event)}]

        for record in records:
            body = json.loads(record["body"])
            
            # Verificação básica de existência de dados
            usuario_id = body.get("UsuarioId", "usuario_teste")
            s3_key = body.get("S3Key")
            bucket_name = body.get("BucketName")

            if not s3_key or not bucket_name:
                print("Erro: S3Key ou BucketName não encontrados no payload.")
                continue

            print(f"Processando arquivo {s3_key} do bucket {bucket_name} para {usuario_id}")

            # 1. Busca arquivo no S3
            s3_object = s3_client.get_object(Bucket=bucket_name, Key=s3_key)
            file_content = s3_object["Body"].read().decode("utf-8-sig")
            df = pd.read_csv(io.StringIO(file_content))
            
            # 2. Limpeza e Normalização
            df.columns = df.columns.str.strip()
            df['Ticker'] = df['Ticker'].astype(str).str.strip().str.upper()
            coluna_preco = 'PrecoCompra' if 'PrecoCompra' in df.columns else 'PrecoMedio'
            df['CustoTotal'] = df['Quantidade'] * df[coluna_preco]
            
            # 3. Consolidação
            carteira_consolidada = df.groupby('Ticker').agg(
                QuantidadeTotal=('Quantidade', 'sum'),
                CustoTotalAcumulado=('CustoTotal', 'sum')
            ).reset_index()
            
            carteira_consolidada['PrecoMedioCalculado'] = carteira_consolidada['CustoTotalAcumulado'] / carteira_consolidada['QuantidadeTotal']
            
            # 4. Escrita no DynamoDB com Log de depuração
            with table.batch_writer() as batch:
                for _, row in carteira_consolidada.iterrows():
                    ticker = row['Ticker']
                    quantidade = int(row['QuantidadeTotal'])
                    preco_medio = float(round(row['PrecoMedioCalculado'], 2))
                    
                    pk = f"USER#{usuario_id}"
                    sk = f"CARTEIRA#{ticker}"
                    
                    print(f"DEBUG: Gravando no DynamoDB | PK={pk} | SK={sk} | Qtd={quantidade} | PM={preco_medio}")
                    
                    batch.put_item(
                        Item={
                            "PK": pk,
                            "SK": sk,
                            "Ticker": ticker,
                            "Quantidade": quantidade,
                            "PrecoMedio": str(preco_medio)
                        }
                    )
            
        return {"statusCode": 200, "body": json.dumps("Processamento concluído com sucesso.")}

    except Exception as e:
        print(f"Erro crítico: {str(e)}")
        raise e