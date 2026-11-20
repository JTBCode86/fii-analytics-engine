#!/bin/bash
set -e
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
IMPORT_DLQ_URL=$(aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name fii-importacao-dlq --query 'QueueUrl' --output text --region $REGION)
SCRAPER_DLQ_URL=$(aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name scraper-dlq --query 'QueueUrl' --output text --region $REGION)
IMPORT_DLQ_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url "$IMPORT_DLQ_URL" --attribute-names QueueArn --query 'Attributes.QueueArn' --output text --region $REGION)
SCRAPER_DLQ_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url "$SCRAPER_DLQ_URL" --attribute-names QueueArn --query 'Attributes.QueueArn' --output text --region $REGION)

IMPORT_ATTRIBUTES="{\"VisibilityTimeout\":\"360\",\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"$IMPORT_DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\"}"
SCRAPER_ATTRIBUTES="{\"VisibilityTimeout\":\"360\",\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"$SCRAPER_DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\"}"
IMPORT_QUEUE_URL=$(aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name fii-importacao-queue \
    --attributes "$IMPORT_ATTRIBUTES" --query 'QueueUrl' --output text --region $REGION)
SCRAPER_QUEUE_URL=$(aws --endpoint-url=$ENDPOINT sqs create-queue --queue-name scraper-queue \
    --attributes "$SCRAPER_ATTRIBUTES" --query 'QueueUrl' --output text --region $REGION)

SQS_IMPORT_ARN=$(aws --endpoint-url=$ENDPOINT sqs get-queue-attributes \
    --queue-url $IMPORT_QUEUE_URL \
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

aguardar_lambda() {
    local nome="$1"
    echo "-> Aguardando a Lambda ${nome} ficar ativa..."
    if ! aws --endpoint-url=$ENDPOINT lambda wait function-active-v2 \
        --function-name "$nome" --region $REGION; then
        echo "ERRO: A Lambda ${nome} não ficou ativa. Verifique os logs do LocalStack." >&2
        return 1
    fi
}

echo "-> Efetuando Deploy da Lambda: processar-carteira-lambda..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name processar-carteira-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --code S3Bucket=$BUCKET_NAME,S3Key=packages/processar_carteira.zip \
    --timeout 60 --region $REGION

aguardar_lambda processar-carteira-lambda

echo "-> Efetuando Deploy da Lambda: scraper-ativos-lambda..."
aws --endpoint-url=$ENDPOINT lambda create-function \
    --function-name scraper-ativos-lambda \
    --runtime python3.10 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --handler handler.lambda_handler \
    --code S3Bucket=$BUCKET_NAME,S3Key=packages/scraper_ativos.zip \
    --timeout 60 --region $REGION

aguardar_lambda scraper-ativos-lambda
    
rm -f /tmp/processar_carteira.zip /tmp/scraper_ativos.zip

# ==============================================================================
# 4. Configuração dos Gatilhos (Event Driven Architecture)
# ==============================================================================

# Gatilho para importação de carteiras (SQS)
aws --endpoint-url=$ENDPOINT lambda create-event-source-mapping \
    --function-name processar-carteira-lambda \
    --event-source-arn $SQS_IMPORT_ARN \
    --batch-size 1 --region $REGION

# Gatilho para o Scraper via SQS (Pipeline de Upload)
aws --endpoint-url=$ENDPOINT lambda create-event-source-mapping \
    --function-name scraper-ativos-lambda \
    --event-source-arn $SQS_SCRAPER_ARN \
    --batch-size 1 --region $REGION

# 5. Configuração do EventBridge (Automação Temporal)
echo "-> Criando regra de agendamento no EventBridge..."
aws --endpoint-url=$ENDPOINT events put-rule \
    --name "AtualizacaoDiariaFIIs" \
    --schedule-expression "rate(24 hours)" \
    --state "ENABLED" \
    --region $REGION

# Vincula a Lambda como alvo da regra
LAMBDA_ARN="arn:aws:lambda:$REGION:000000000000:function:scraper-ativos-lambda"
aws --endpoint-url=$ENDPOINT events put-targets \
    --rule "AtualizacaoDiariaFIIs" \
    --targets "[{\"Id\": \"Target1\", \"Arn\": \"$LAMBDA_ARN\"}]" \
    --region $REGION

# Permissão para o EventBridge invocar a Lambda
aws --endpoint-url=$ENDPOINT lambda add-permission \
    --function-name scraper-ativos-lambda \
    --statement-id "EventBridgeInvoke" \
    --action "lambda:InvokeFunction" \
    --principal "events.amazonaws.com" \
    --source-arn "arn:aws:events:$REGION:000000000000:rule/AtualizacaoDiariaFIIs" \
    --region $REGION

echo "########### PROVISIONAMENTO CONCLUÍDO COM SUCESSO ###########"