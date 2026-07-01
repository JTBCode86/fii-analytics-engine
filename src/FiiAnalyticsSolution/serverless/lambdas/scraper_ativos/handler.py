import json
import os
import requests
import boto3
from bs4 import BeautifulSoup

# Configuração de rede interna
LOCAL_HOST = os.environ.get("LOCALSTACK_HOSTNAME", "localhost")
ENDPOINT_URL = f"http://{LOCAL_HOST}:4566"

dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
table = dynamodb.Table("FiiAnalyticsDb")

def extrair_texto(elemento, padrao="N/A"):
    """Função auxiliar para evitar erro NoneType ao acessar .text"""
    return elemento.text.strip() if elemento else padrao
    
def lambda_handler(event, context):
    
    # continua o codigo normal...
    print(f"Evento recebido: {json.dumps(event)}")
    
    # Suporte a SQS ou Invocação Direta
    records = event["Records"] if "Records" in event else [{"body": json.dumps(event)}]
    
    for record in records:
        body = json.loads(record["body"])
        ticker = body.get("ticker")
        
        if not ticker:
            print("Erro: Ticker não informado no evento.")
            continue
            
        print(f"Iniciando scraping para o ativo: {ticker}")
        
        try:
            # Simulando requisição (ajuste a URL conforme seu alvo real)
            url = f"https://statusinvest.com.br/fundos-imobiliarios/{ticker.lower()}"
            headers = {'User-Agent': 'Mozilla/5.0'}
            response = requests.get(url, headers=headers)
            
            if response.status_code != 200:
                print(f"Erro ao acessar {ticker}: Status {response.status_code}")
                continue
                
            soup = BeautifulSoup(response.content, 'html.parser')
            
            # Buscando elementos com tratamento de erro (usando a função auxiliar)
            valor_atual = extrair_texto(soup.find('strong', {'class': 'value'}))
            dy = extrair_texto(soup.find('div', {'title': 'Dividend Yield'}))
            
            print(f"DEBUG: Scraped {ticker} | Valor: {valor_atual} | DY: {dy}")
            
            # Gravação no DynamoDB
            table.put_item(
                Item={
                    "PK": f"ATIVO#{ticker}",
                    "SK": "METADATA",
                    "Ticker": ticker,
                    "Cotacao": valor_atual,
                    "DividendYield": dy
                }
            )
            print(f"Dados do {ticker} gravados com sucesso!")

        except Exception as e:
            print(f"Erro crítico no scraper do {ticker}: {str(e)}")
            
    return {"statusCode": 200, "body": json.dumps("Scraping finalizado.")}