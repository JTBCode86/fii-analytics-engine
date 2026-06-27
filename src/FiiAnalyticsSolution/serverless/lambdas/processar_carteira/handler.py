import json
import os
import io
import boto3
import pandas as pd

# Configuração correta de rede interna para Lambdas no LocalStack
LOCAL_HOST = os.environ.get("LOCALSTACK_HOSTNAME")
ENDPOINT_URL = f"http://{LOCAL_HOST}:4566" if LOCAL_HOST else None

s3_client = boto3.client("s3", endpoint_url=ENDPOINT_URL)
dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
table = dynamodb.Table("FiiAnalyticsDb")

def lambda_handler(event, context):
    print(f"Recebido evento do SQS: {json.dumps(event)}")
    
    try:
        for record in event.get("Records", []):
            body = json.loads(record["body"])
            usuario_id = body["UsuarioId"]
            s3_key = body["S3Key"]
            bucket_name = body["BucketName"]
            
            print(f"Iniciando processamento da carteira do usuário: {usuario_id}")
            
            # 1. Busca o arquivo do S3
            s3_object = s3_client.get_object(Bucket=bucket_name, Key=s3_key)
            
            # 2. REMÉDIO SANTO: Lê decodificando com utf-8-sig (remove o BOM do Windows)
            file_content = s3_object["Body"].read().decode("utf-8-sig")
            df = pd.read_csv(io.StringIO(file_content))
            
            # 3. Limpa espaços invisíveis ou quebras de linha dos nomes das colunas
            df.columns = df.columns.str.strip()
            
            # 4. Normaliza os dados da coluna Ticker
            df['Ticker'] = df['Ticker'].astype(str).str.strip().str.upper()
            
            # Garante compatibilidade caso a coluna venha com qualquer um dos nomes
            coluna_preco = 'PrecoCompra' if 'PrecoCompra' in df.columns else 'PrecoMedio'
            
            # Cálculo do Custo Total
            df['CustoTotal'] = df['Quantidade'] * df[coluna_preco]
            
            # Consolidação dos Ativos (Cálculo do Preço Médio Ponderado)
            carteira_consolidada = df.groupby('Ticker').agg(
                QuantidadeTotal=('Quantidade', 'sum'),
                CustoTotalAcumulado=('CustoTotal', 'sum')
            ).reset_index()
            
            carteira_consolidada['PrecoMedioCalculado'] = carteira_consolidada['CustoTotalAcumulado'] / carteira_consolidada['QuantidadeTotal']
            
            # 5. Escrita em lote no DynamoDB
            with table.batch_writer() as batch:
                for _, row in carteira_consolidada.iterrows():
                    ticker = row['Ticker']
                    quantidade = int(row['QuantidadeTotal'])
                    preco_medio = float(round(row['PrecoMedioCalculado'], 2))
                    
                    print(f"Gravando no DynamoDB: Ativo={ticker}, Qtd={quantidade}, PM={preco_medio}")
                    
                    batch.put_item(
                        Item={
                            "PK": f"USER#{usuario_id}",
                            "SK": f"CARTEIRA#{ticker}",
                            "Ticker": ticker,
                            "Quantidade": quantidade,
                            "PrecoMedio": str(preco_medio)
                        }
                    )
            
            print(f"Processamento concluído com sucesso para o usuário {usuario_id}!")
            
        return {"statusCode": 200, "body": json.dumps("Processamento finalizado.")}
        
    except Exception as e:
        print(f"Erro crítico no processamento da Lambda: {str(e)}")
        raise e