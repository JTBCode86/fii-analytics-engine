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

def obter_todos_tickers():
    """Busca todos os tickers únicos presentes no DynamoDB para o scraping global."""
    response = table.scan(ProjectionExpression="Ticker")
    # Retorna uma lista de tickers únicos
    return list(set(item['Ticker'] for item in response.get('Items', [])))

def processar_scraping(ticker):
    """Lógica central de extração de dados."""
    print(f"Iniciando scraping para: {ticker}")
    url = f"https://statusinvest.com.br/fundos-imobiliarios/{ticker.lower()}"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
    }
    
    response = requests.get(url, headers=headers)
    if response.status_code != 200:
        print(f"Erro {response.status_code} ao acessar {ticker}")
        return

    soup = BeautifulSoup(response.content, 'html.parser')
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
    print(f"Sucesso: {ticker} atualizado.")

def lambda_handler(event, context):
    # 1. Identificar a origem do disparo
    tickers_para_processar = []

    # Gatilho SQS (via upload de carteira)
    if "Records" in event:
        for record in event["Records"]:
            body = json.loads(record.get("body", "{}"))
            ticker = body.get("ticker")
            if ticker: tickers_para_processar.append(ticker)
            
    # Gatilho EventBridge (Agendamento Automático)
    else:
        print("Disparo via EventBridge detectado. Iniciando varredura global...")
        tickers_para_processar = obter_todos_tickers()

    # 2. Executar processamento
    for ticker in tickers_para_processar:
        try:
            processar_scraping(ticker)
        except Exception as e:
            print(f"Erro no processamento do ticker {ticker}: {str(e)}")
            
    return {"statusCode": 200, "body": json.dumps(f"Processados {len(tickers_para_processar)} ativos.")}