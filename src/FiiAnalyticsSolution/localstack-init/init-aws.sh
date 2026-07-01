#!/bin/bash
echo "########### INICIANDO PROVISIONAMENTO E DEPLOY DE LAMBDAS ###########"

REGION="us-east-1"
ENDPOINT="http://localhost:4566"
BUCKET_NAME="fii-carteiras-bucket"

# ==============================================================================
# 1. Criação dos recursos básicos de Infraestrutura
# ==============================================================================

echo "-> Criando Bucket S3: ${BUCKET_NAME}..."
aws --endpoint-url=$ENDPOINT s3 mb s3://$BUCKET_NAME --region $REGION

echo "-> Criando Filas SQS..."
# Fila para importar carteiras
aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name fii-importacao-queue --region $REGION
# Fila para o scraper (a nova fila de automação)
SCRAPER_QUEUE_URL=$(aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name scraper-queue --query 'QueueUrl' --output text --region $REGION)

SQS_IMPORT_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url $ENDPOINT/000000000000/fii-importacao-queue \
    --attribute-names QueueArn --query "Attributes.QueueArn" --output text --region $REGION)

SQS_SCRAPER_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url $SCRAPER_QUEUE_URL \
    --attribute-names QueueArn --query "Attributes.QueueArn" --output text --region $REGION)

echo "-> Criando Tabela DynamoDB: FiiAnalyticsDb..."
aws --endpoint-url=$ENDPOINT dynamodb create-table \
    --table-name FiiAnalyticsDb \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST \
    --region $REGION

# ==============================================================================
# 2. Coleta e Upload dos Pacotes
# ==============================================================================

cp /tmp/lambda_dist/processar_carteira.zip /tmp/processar_carteira.zip
cp /tmp/lambda_dist/scraper_ativos.zip /tmp/scraper_ativos.zip

aws --endpoint-url=$ENDPOINT s3 cp /tmp/processar_carteira.zip s3://$BUCKET_NAME/packages/processar_carteira.zip --region $REGION
aws --endpoint-url=$ENDPOINT s3 cp /tmp/scraper_ativos.zip s3://$BUCKET_NAME/packages/scraper_ativos.zip --region $REGION

# ==============================================================================
# 3. Deploy das Lambdas
# ==============================================================================

echo "-> Efetuando Deploy da Lambda: processar-carteira-lambda..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name processar-carteira-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --code S3Bucket=$BUCKET_NAME,S3Key=packages/processar_carteira.zip \
    --timeout 60 --region $REGION

echo "-> Efetuando Deploy da Lambda: scraper-ativos-lambda..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name scraper-ativos-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --code S3Bucket=$BUCKET_NAME,S3Key=packages/scraper_ativos.zip \
    --timeout 60 --region $REGION
    
rm -f /tmp/processar_carteira.zip /tmp/scraper_ativos.zip

# ==============================================================================
# 4. Configuração dos Gatilhos (Event Driven Architecture)
# ==============================================================================

echo "-> Aguardando estabilização..."
sleep 5

# Gatilho para importação de carteiras
aws --endpoint-url=$ENDPOINT lambda create-event-source-mapping \
    --function-name processar-carteira-lambda \
    --event-source-arn $SQS_IMPORT_ARN \
    --batch-size 1 --region $REGION

# Gatilho para o Scraper (O novo elo do pipeline)
aws --endpoint-url=$ENDPOINT lambda create-event-source-mapping \
    --function-name scraper-ativos-lambda \
    --event-source-arn $SQS_SCRAPER_ARN \
    --batch-size 1 --region $REGION

echo "########### PROVISIONAMENTO CONCLUÍDO COM SUCESSO ###########"