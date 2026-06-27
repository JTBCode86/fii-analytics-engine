import os
import json
import boto3
import requests
from bs4 import BeautifulSoup

# Configuração dinâmica
ENDPOINT_URL = os.environ.get("DYNAMODB_ENDPOINT") # Ex: http://localstack:4566
s3_client = boto3.client("s3", endpoint_url=os.environ.get("S3_ENDPOINT"))
dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
table = dynamodb.Table(os.environ.get("TABLE_NAME", "FiiAnalyticsDb"))

def get_tickers():
    """Busca a lista de ativos de uma variável de ambiente ou S3."""
    tickers_env = os.environ.get("TICKERS_LIST")
    if tickers_env:
        return json.loads(tickers_env)
    
    # Exemplo: buscar de um arquivo no S3 caso não esteja no ENV
    try:
        response = s3_client.get_object(Bucket="meu-bucket-config", Key="tickers.json")
        return json.loads(response['Body'].read().decode('utf-8'))
    except Exception:
        return ["MXRF11", "HGLG11"] # Default seguro

def limpar_valor(txt):
    """Limpeza genérica de valores monetários."""
    return float(txt.replace("R$", "").replace(".", "").replace(",", ".").strip())

def extrair_dados_fii(ticker):
    url = f"https://www.fundsexplorer.com.br/funds/{ticker.lower()}"
    headers = {"User-Agent": "Mozilla/5.0"}
    
    response = requests.get(url, headers=headers, timeout=10)
    response.raise_for_status()
    soup = BeautifulSoup(response.text, 'html.parser')
    
    # Mapeamento dinâmico (aqui você pode buscar por atributos data- ou classes)
    return {
        "preco": limpar_valor(soup.find("span", class_="price").text),
        "vp": limpar_valor(soup.find("span", class_="vp").text),
        "dividendo_12m": limpar_valor(soup.find("span", class_="dividendo").text) * 12
    }

def lambda_handler(event, context):
    tickers = get_tickers()
    
    for ticker in tickers:
        try:
            dados = extrair_dados_fii(ticker)
            
            # Gravação dinâmica no DynamoDB
            table.put_item(
                Item={
                    "PK": f"FII#{ticker}",
                    "SK": "METRICAS",
                    "Nome": f"Fundo {ticker}", # Poderia vir do scraper também
                    "PrecoAtual": str(round(dados["preco"], 2)),
                    "ValorPatrimonialPorCota": str(round(dados["vp"], 2)),
                    "DividendoAcumulado12M": str(round(dados["dividendo_12m"], 2))
                }
            )
            print(f"Sucesso: {ticker}")
        except Exception as e:
            print(f"Erro no ativo {ticker}: {e}")
            
    return {"statusCode": 200, "body": "Atualização concluída"}