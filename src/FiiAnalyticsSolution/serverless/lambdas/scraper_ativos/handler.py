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

def formatar_valor(texto):
    """Converte '10,50' ou '10,50%' para float 10.50"""
    if not texto or texto == "N/A": return 0.0
    clean_text = texto.replace('R$', '').replace('%', '').replace('.', '').replace(',', '.').strip()
    try:
        return float(clean_text)
    except ValueError:
        return 0.0

def extrair_valor_por_label(soup, label_texto):
    """Busca o valor baseado no texto do label"""
    elemento = soup.find(string=lambda text: text and label_texto in text)
    if elemento:
        container = elemento.find_parent('div', class_='pb-1') or elemento.find_parent('div')
        if container:
            valor_el = container.find('strong', class_='value')
            if valor_el: return valor_el.text
    return "0.0"

def lambda_handler(event, context):
    # A estrutura 'Records' é padrão para gatilhos SQS no LocalStack
    records = event.get("Records", [{"body": json.dumps(event)}])
    
    for record in records:
        try:
            # SQS entrega o payload dentro do campo 'body'
            body = json.loads(record.get("body", "{}"))
            ticker = body.get("ticker")
            if not ticker: 
                print("Ticker não encontrado no payload.")
                continue
                
            print(f"Iniciando scraping (via SQS/Trigger) para: {ticker}")
            
            url = f"https://statusinvest.com.br/fundos-imobiliarios/{ticker.lower()}"
            headers = {
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
            }
            
            response = requests.get(url, headers=headers)
            if response.status_code != 200:
                print(f"Erro {response.status_code} ao acessar {ticker}")
                continue
                
            soup = BeautifulSoup(response.content, 'html.parser')
            
            # Extração dos dados
            cotacao_el = soup.find('strong', {'class': 'value'})
            cotacao_raw = cotacao_el.text if cotacao_el else "0"
            dy_raw = extrair_valor_por_label(soup, 'Dividend Yield')
            pvp_raw = extrair_valor_por_label(soup, 'P/VP')
            
            table.put_item(
                Item={
                    "PK": f"ATIVO#{ticker.upper()}",
                    "SK": "METADATA",
                    "Ticker": ticker.upper(),
                    "Cotacao": str(formatar_valor(cotacao_raw)),
                    "DividendYield": str(formatar_valor(dy_raw)),
                    "PVP": str(formatar_valor(pvp_raw))
                }
            )
            print(f"Sucesso: {ticker} atualizado no DynamoDB.")

        except Exception as e:
            print(f"Erro no processamento do ticker {ticker}: {str(e)}")
            
    return {"statusCode": 200, "body": json.dumps("Processamento concluído.")}