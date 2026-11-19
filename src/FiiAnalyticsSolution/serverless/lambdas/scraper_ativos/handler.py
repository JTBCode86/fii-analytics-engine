import json
import os
import re
from decimal import Decimal, InvalidOperation
from datetime import datetime, timezone
import requests
import boto3
from bs4 import BeautifulSoup

# Configuração de rede interna
LOCAL_HOST = os.environ.get("LOCALSTACK_HOSTNAME", "localhost")
ENDPOINT_URL = f"http://{LOCAL_HOST}:4566"

dynamodb = boto3.resource("dynamodb", endpoint_url=ENDPOINT_URL)
table = dynamodb.Table("FiiAnalyticsDb")

def formatar_valor(texto):
    """Converte números no formato brasileiro sem substituir dados inválidos por zero."""
    if not texto or texto.strip() == "N/A":
        raise ValueError("Indicador de mercado indisponível.")
    if not re.fullmatch(r"(?:R\$\s*)?(?:\d{1,3}(?:\.\d{3})+|\d+)(?:,\d+)?\s*%?", texto.strip()):
        raise ValueError(f"Indicador de mercado inválido: {texto}")
    clean_text = texto.replace('R$', '').replace('%', '').replace('.', '').replace(',', '.').strip()
    try:
        valor = Decimal(clean_text)
    except InvalidOperation as exc:
        raise ValueError(f"Indicador de mercado inválido: {texto}") from exc
    if not valor.is_finite() or valor < 0:
        raise ValueError(f"Indicador de mercado inválido: {texto}")
    return valor

def extrair_valor_por_label(soup, label_texto):
    """Busca o valor baseado no texto do label"""
    elemento = soup.find(string=lambda text: text and label_texto in text)
    if elemento:
        container = elemento.find_parent('div', class_='pb-1') or elemento.find_parent('div')
        if container:
            valor_el = container.find('strong', class_='value')
            if valor_el: return valor_el.text
    raise ValueError(f"Indicador não encontrado: {label_texto}")

def obter_todos_tickers():
    """Busca todos os tickers únicos presentes no DynamoDB para o scraping global."""
    tickers = set()
    request = {"ProjectionExpression": "Ticker"}
    while True:
        response = table.scan(**request)
        tickers.update(item["Ticker"] for item in response.get("Items", []) if item.get("Ticker"))
        last_key = response.get("LastEvaluatedKey")
        if not last_key:
            return sorted(tickers)
        request["ExclusiveStartKey"] = last_key

def processar_scraping(ticker):
    """Lógica central de extração de dados."""
    print(f"Iniciando scraping para: {ticker}")
    url = f"https://statusinvest.com.br/fundos-imobiliarios/{ticker.lower()}"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
    }
    
    response = requests.get(url, headers=headers, timeout=15)
    response.raise_for_status()

    soup = BeautifulSoup(response.content, 'html.parser')
    cotacao_el = soup.find('strong', {'class': 'value'})
    if not cotacao_el:
        raise ValueError(f"Cotação não encontrada: {ticker}")
    cotacao_raw = cotacao_el.text
    dy_raw = extrair_valor_por_label(soup, 'Dividend Yield')
    pvp_raw = extrair_valor_por_label(soup, 'P/VP')
    cotacao = formatar_valor(cotacao_raw)
    dividend_yield = formatar_valor(dy_raw)
    pvp = formatar_valor(pvp_raw)
    if cotacao <= 0:
        raise ValueError(f"Cotação inválida para {ticker}: {cotacao_raw}")
    
    table.put_item(
        Item={
            "PK": f"ATIVO#{ticker.upper()}",
            "SK": "METADATA",
            "Ticker": ticker.upper(),
            "Cotacao": str(cotacao),
            "DividendYield": str(dividend_yield),
            "PVP": str(pvp),
            "AtualizadoEm": datetime.now(timezone.utc).isoformat()
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
            if not ticker:
                raise ValueError("A mensagem SQS deve conter um ticker.")
            tickers_para_processar.append(ticker)
            
    # Gatilho EventBridge (Agendamento Automático)
    else:
        print("Disparo via EventBridge detectado. Iniciando varredura global...")
        tickers_para_processar = obter_todos_tickers()

    # 2. Executar processamento
    for ticker in tickers_para_processar:
        processar_scraping(ticker)
            
    return {"statusCode": 200, "body": json.dumps(f"Processados {len(tickers_para_processar)} ativos.")}