#!/bin/bash
echo "########### INICIANDO PROVISIONAMENTO E DEPLOY DE LAMBDAS ###########"

REGION="us-east-1"
ENDPOINT="http://localhost:4566"
BUCKET_NAME="fii-carteiras-bucket"

# ==============================================================================
# 1. Criação dos recursos básicos de Infraestrutura (S3, SQS e DynamoDB)
# ==============================================================================

echo "-> Criando Bucket S3: ${BUCKET_NAME}..."
aws --endpoint-url=$ENDPOINT s3 mb s3://$BUCKET_NAME --region $REGION

echo "-> Criando Fila SQS: fii-importacao-queue..."
aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name fii-importacao-queue --region $REGION

SQS_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url $ENDPOINT/000000000000/fii-importacao-queue \
    --attribute-names QueueArn \
    --query "Attributes.QueueArn" \
    --output text \
    --region $REGION)

echo "-> Criando Tabela DynamoDB: FiiAnalyticsDb..."
aws --endpoint-url=$ENDPOINT dynamodb create-table \
    --table-name FiiAnalyticsDb \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST \
    --region $REGION

# ==============================================================================
# 2. Coleta do Pacote Pré-Compilado (Bypass do Pip interno do LocalStack)
# ==============================================================================

echo "-> Coletando pacote estável gerado pelo contêiner lambda-builder..."
# Copia o zip puro do volume compartilhado para a pasta temporária local de deploy
cp /tmp/lambda_dist/processar_carteira.zip /tmp/processar_carteira.zip

# ==============================================================================
# 3. Upload para o S3 (Bypass do limite de 50MB) e Deploy
# ==============================================================================

echo "-> Fazendo upload do pacote pesado para o S3..."
aws --endpoint-url=$ENDPOINT s3 cp /tmp/processar_carteira.zip s3://$BUCKET_NAME/packages/processar_carteira.zip --region $REGION

echo "-> Efetuando Deploy da Lambda: processar-carteira-lambda (via S3)..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name processar-carteira-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --code S3Bucket=$BUCKET_NAME,S3Key=packages/processar_carteira.zip \
    --timeout 60 \
    --region $REGION

# --- Lambda 2: scraper-ativos-lambda (Leve, sem dependências pesadas) ---
echo "-> Preparando código da Lambda: scraper-ativos-lambda..."
zip -q -j /tmp/scraper_ativos.zip /app/serverless/lambdas/scraper_ativos/handler.py

echo "-> Efetuando Deploy da Lambda: scraper-ativos-lambda..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name scraper-ativos-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --zip-file fileb:///tmp/scraper_ativos.zip \
    --region $REGION

# Limpeza dos resíduos locais temporários
rm -f /tmp/processar_carteira.zip /tmp/scraper_ativos.zip

# ==============================================================================
# 4. Configuração dos Mapeamentos de Gatilhos
# ==============================================================================

echo "-> Aguardando 5 segundos para estabilização das Lambdas..."
sleep 5

echo "-> Configurando Gatilho SQS -> Lambda..."
aws --endpoint-url=$ENDPOINT lambda create-event-source-mapping \
    --function-name processar-carteira-lambda \
    --event-source-arn $SQS_ARN \
    --batch-size 1 \
    --region $REGION

echo "-> Configurando Regra Agendada (Cron) para o Scraper..."
aws --endpoint-url=$ENDPOINT events put-rule \
    --name executar-scraper-diario \
    --schedule-expression "rate(1 day)" \
    --region $REGION

aws --endpoint-url=$ENDPOINT events put-targets \
    --rule executar-scraper-diario \
    --targets "Id"="1","Arn"="arn:aws:lambda:$REGION:000000000000:function:scraper-ativos-lambda" \
    --region $REGION

echo "########### PROVISIONAMENTO DE NUVEM LOCAL CONCLUÍDO COM SUCESSO ###########"