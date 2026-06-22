#!/bin/bash
# Provisiona los recursos AWS del pipeline en LocalStack al arrancar (ADR-001).
# LocalStack ejecuta este script cuando el edge está listo (ready.d).
#
# Nota: en la arquitectura objetivo estos recursos los define AWS CDK (ADR-009).
# Aquí se crean con awslocal para tener el pipeline corriendo ya en dev.
set -euo pipefail

TOPIC="atalaya-telemetry"
QUEUE="atalaya-telemetry-queue"
DLQ="atalaya-telemetry-dlq"
BUCKET="atalaya-datalake"

echo "[init] creando recursos de Atalaya en LocalStack..."

# Data lake (eventos crudos inmutables, ADR-007)
awslocal s3 mb "s3://${BUCKET}"

# DLQ para mensajes envenenados (resiliencia, SAD §8)
DLQ_URL=$(awslocal sqs create-queue --queue-name "${DLQ}" --query 'QueueUrl' --output text)
DLQ_ARN=$(awslocal sqs get-queue-attributes --queue-url "${DLQ_URL}" \
  --attribute-names QueueArn --query 'Attributes.QueueArn' --output text)

# Cola principal (buffer) con redrive a la DLQ tras 5 intentos
awslocal sqs create-queue --queue-name "${QUEUE}" \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${DLQ_ARN}\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\"}"
QUEUE_URL=$(awslocal sqs get-queue-url --queue-name "${QUEUE}" --query 'QueueUrl' --output text)
QUEUE_ARN=$(awslocal sqs get-queue-attributes --queue-url "${QUEUE_URL}" \
  --attribute-names QueueArn --query 'Attributes.QueueArn' --output text)

# Topic SNS y suscripción de la cola (fan-out → buffer, ADR-001)
TOPIC_ARN=$(awslocal sns create-topic --name "${TOPIC}" --query 'TopicArn' --output text)
awslocal sns subscribe --topic-arn "${TOPIC_ARN}" --protocol sqs \
  --notification-endpoint "${QUEUE_ARN}" --attributes RawMessageDelivery=true

echo "[init] listo:"
echo "  SNS topic : ${TOPIC_ARN}"
echo "  SQS queue : ${QUEUE_URL}"
echo "  SQS dlq   : ${DLQ_URL}"
echo "  S3 bucket : s3://${BUCKET}"
