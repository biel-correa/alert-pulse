#!/bin/bash
set -eo pipefail

echo "=========================================================="
echo "  [Alert Pulse] Initializing local AWS infrastructure"
echo "=========================================================="

export AWS_DEFAULT_REGION=us-east-1

# 1. Create DynamoDB Table: MonitoringHub (if not exists)
if awslocal dynamodb describe-table --table-name MonitoringHub >/dev/null 2>&1; then
    echo "--> DynamoDB table 'MonitoringHub' already exists."
else
    echo "--> Creating DynamoDB table 'MonitoringHub'..."
    awslocal dynamodb create-table \
        --table-name MonitoringHub \
        --attribute-definitions \
            AttributeName=PK,AttributeType=S \
            AttributeName=SK,AttributeType=S \
            AttributeName=SeverityStatus,AttributeType=S \
            AttributeName=CreatedAt,AttributeType=S \
        --key-schema \
            AttributeName=PK,KeyType=HASH \
            AttributeName=SK,KeyType=RANGE \
        --billing-mode PAY_PER_REQUEST \
        --global-secondary-indexes \
            "[{\"IndexName\": \"GSI1-Severity\",
              \"KeySchema\": [{\"AttributeName\":\"SeverityStatus\",\"KeyType\":\"HASH\"},
                              {\"AttributeName\":\"CreatedAt\",\"KeyType\":\"RANGE\"}],
              \"Projection\":{\"ProjectionType\":\"ALL\"}}]"

    echo "--> Enabling TTL on 'ExpiresAt' attribute..."
    awslocal dynamodb update-time-to-live \
        --table-name MonitoringHub \
        --time-to-live-specification "Enabled=true, AttributeName=ExpiresAt"
fi

# 2. Create SNS Topic for Critical Alerts
echo "--> Ensuring SNS topic 'WebhookCriticalAlerts'..."
TOPIC_ARN=$(awslocal sns create-topic --name WebhookCriticalAlerts --query "TopicArn" --output text)
echo "    SNS Topic ARN: $TOPIC_ARN"

# 3. Create SQS Dead-Letter Queue (DLQ) for Poison/Unprocessable Messages
echo "--> Ensuring SQS DLQ 'critical-alerts-dlq'..."
DLQ_URL=$(awslocal sqs create-queue --queue-name critical-alerts-dlq --query "QueueUrl" --output text)
DLQ_ARN=$(awslocal sqs get-queue-attributes --queue-url "$DLQ_URL" --attribute-names QueueArn --query "Attributes.QueueArn" --output text)
echo "    SQS DLQ ARN: $DLQ_ARN"

# 4. Create Main SQS Queue with Redrive Policy (maxReceiveCount: 3)
echo "--> Ensuring SQS queue 'critical-alerts-queue' with RedrivePolicy..."
QUEUE_URL=$(awslocal sqs create-queue --queue-name critical-alerts-queue --query "QueueUrl" --output text)
REDRIVE_POLICY="{\"deadLetterTargetArn\":\"$DLQ_ARN\",\"maxReceiveCount\":3}"
awslocal sqs set-queue-attributes \
    --queue-url "$QUEUE_URL" \
    --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"$DLQ_ARN\\\",\\\"maxReceiveCount\\\":3}\"}"
QUEUE_ARN=$(awslocal sqs get-queue-attributes --queue-url "$QUEUE_URL" --attribute-names QueueArn --query "Attributes.QueueArn" --output text)

# 5. Subscribe SQS Queue to SNS Topic with Critical Filter Policy
echo "--> Subscribing SQS queue to SNS topic with FilterPolicy..."
awslocal sns subscribe \
    --topic-arn "$TOPIC_ARN" \
    --protocol sqs \
    --notification-endpoint "$QUEUE_ARN" \
    --attributes '{"FilterPolicy": "{\"Severity\": [\"CRITICAL\"]}"}'

# 6. Seed Initial Test Endpoints into DynamoDB
echo "--> Seeding test endpoints into DynamoDB..."
awslocal dynamodb put-item \
    --table-name MonitoringHub \
    --item '{
        "PK": {"S": "ENDPOINT#stripe-billing"},
        "SK": {"S": "METADATA"},
        "Id": {"S": "stripe-billing"},
        "Name": {"S": "Stripe Billing Production Webhook"},
        "SecretKey": {"S": "whsec_test_secret_123"},
        "UrlPath": {"S": "/api/v1/webhooks/ingest/stripe-billing"},
        "SeverityThreshold": {"S": "DEFAULT"},
        "CreatedAt": {"S": "2026-10-11T10:00:00.000Z"},
        "IsActive": {"BOOL": true}
    }'

awslocal dynamodb put-item \
    --table-name MonitoringHub \
    --item '{
        "PK": {"S": "ENDPOINT#github-deployments"},
        "SK": {"S": "METADATA"},
        "Id": {"S": "github-deployments"},
        "Name": {"S": "GitHub Deployment CI/CD Webhook"},
        "SecretKey": {"S": "whsec_github_secret_456"},
        "UrlPath": {"S": "/api/v1/webhooks/ingest/github-deployments"},
        "SeverityThreshold": {"S": "DEFAULT"},
        "CreatedAt": {"S": "2026-10-11T10:00:00.000Z"},
        "IsActive": {"BOOL": true}
    }'

echo "=========================================================="
echo "  [Alert Pulse] Local AWS resources ready for use! "
echo "=========================================================="
