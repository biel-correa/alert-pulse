---
title: "Local Environment with Docker & LocalStack"
tags:
  - infrastructure
  - docker
  - localstack
  - dev-environment
  - dynamodb-admin
  - mailpit
date: 2026-10-11
status: approved
---

# Local Environment with Docker & LocalStack

To develop, test, and validate **Alert Pulse** without incurring AWS cloud costs or requiring active internet connectivity, the monorepo provides a full local support stack orchestrated via **Docker Compose**.

---

## 📦 Local Services Stack

| **`alert-pulse-localstack`** | `localstack/localstack:3.8.1` | `4566` | Unified gateway emulating **DynamoDB**, **SNS**, and **SQS**. |
| **`alert-pulse-dynamodb-admin`** | `aaronshaf/dynamodb-admin:latest` | `8001` | Web UI to inspect table items, PK/SK, GSI1, and TTL expiration. |
| **`alert-pulse-mailpit`** | `axllent/mailpit:latest` | `8025` (Web)<br/>`1025` (SMTP) | Local inbox to inspect critical alert emails dispatched by SNS. |
| **`alert-pulse-webhook-sink`** | `mendhak/http-https-echo:latest` | `8080` | Mock HTTP Echo receiver to test payload replays (*Retry*). |
| **`alert-pulse-backend`** | `alert-pulse-backend:latest` | `5050` | .NET 10 Webhook Ingestion, Classification & Dispatcher API. |
| **`alert-pulse-frontend`** | `alert-pulse-frontend:latest` | `4200` | Angular 22 Operations & Incident Monitoring Dashboard (Nginx). |

---

## 🐳 Configuration: `docker-compose.yml`

The configuration file is located at the root of the monorepo:

```yaml
services:
  # ---------------------------------------------------------------------------
  # LocalStack: Emulating DynamoDB, SNS, and SQS
  # ---------------------------------------------------------------------------
  localstack:
    container_name: alert-pulse-localstack
    image: localstack/localstack:latest
    ports:
      - "4566:4566"
    environment:
      - SERVICES=dynamodb,sns,sqs
      - AWS_DEFAULT_REGION=us-east-1
      - DOCKER_HOST=unix:///var/run/docker.sock
    volumes:
      - "./infra/init-aws.sh:/etc/localstack/init/ready.d/init-aws.sh:ro"
      - "/var/run/docker.sock:/var/run/docker.sock"
      - "localstack-data:/var/lib/localstack"
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:4566/_localstack/health || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 15s
    networks:
      - alert-pulse-net

  # ---------------------------------------------------------------------------
  # DynamoDB Admin: Visual dashboard to inspect items, PK/SK, and GSIs
  # Web UI available at: http://localhost:8001
  # ---------------------------------------------------------------------------
  dynamodb-admin:
    container_name: alert-pulse-dynamodb-admin
    image: aaronshaf/dynamodb-admin:latest
    ports:
      - "8001:8001"
    environment:
      - DYNAMO_ENDPOINT=http://localstack:4566
      - AWS_REGION=us-east-1
      - AWS_ACCESS_KEY_ID=test
      - AWS_SECRET_ACCESS_KEY=test
    depends_on:
      localstack:
        condition: service_healthy
    networks:
      - alert-pulse-net

  # ---------------------------------------------------------------------------
  # Mailpit: Local inbox for previewing SNS alert emails
  # Web UI available at: http://localhost:8025 (SMTP on port 1025)
  # ---------------------------------------------------------------------------
  mailpit:
    container_name: alert-pulse-mailpit
    image: axllent/mailpit:latest
    ports:
      - "8025:8025"
      - "1025:1025"
    environment:
      - MP_SMTP_AUTH_ACCEPT_ANY=1
      - MP_SMTP_AUTH_ALLOW_INSECURE=1
    networks:
      - alert-pulse-net

  # ---------------------------------------------------------------------------
  # Webhook Sink (HTTP Echo): Mock receiver for webhook replay testing
  # Available at: http://localhost:8080
  # ---------------------------------------------------------------------------
  webhook-sink:
    container_name: alert-pulse-webhook-sink
    image: mendhak/http-https-echo:latest
    ports:
      - "8080:8080"
    environment:
      - HTTP_PORT=8080
    networks:
      - alert-pulse-net

networks:
  alert-pulse-net:
    name: alert-pulse-network
    driver: bridge

volumes:
  localstack-data:
    name: alert-pulse-localstack-data
```

---

## 📜 Automated Bootstrap Script: `infra/init-aws.sh`

Mounted at `/etc/localstack/init/ready.d/init-aws.sh` in the LocalStack container, this script runs automatically when the container finishes starting:

```bash
#!/bin/bash
set -eo pipefail

echo "=== [Alert Pulse] Initializing local AWS infrastructure ==="

export AWS_DEFAULT_REGION=us-east-1

# 1. Create MonitoringHub DynamoDB Table with GSI1-Severity
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

# 2. Enable TTL on ExpiresAt attribute
awslocal dynamodb update-time-to-live \
    --table-name MonitoringHub \
    --time-to-live-specification "Enabled=true, AttributeName=ExpiresAt"

# 3. Create SNS Topic for Critical Alerts
TOPIC_ARN=$(awslocal sns create-topic --name WebhookCriticalAlerts --query "TopicArn" --output text)

# 4. Create SQS Queue and subscribe to SNS with Filter Policy
QUEUE_URL=$(awslocal sqs create-queue --queue-name critical-alerts-queue --query "QueueUrl" --output text)
QUEUE_ARN=$(awslocal sqs get-queue-attributes --queue-url "$QUEUE_URL" --attribute-names QueueArn --query "Attributes.QueueArn" --output text)

awslocal sns subscribe \
    --topic-arn "$TOPIC_ARN" \
    --protocol sqs \
    --notification-endpoint "$QUEUE_ARN" \
    --filter-policy '{"Severity": ["CRITICAL"]}'

# 5. Seed Test Webhook Endpoints
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

echo "=== [Alert Pulse] Local AWS resources ready for use! ==="
```

---

## 🔍 Web Dashboards & Local Inspection

After starting the stack with `docker compose up -d`, open the following URLs in your browser:

* **DynamoDB Admin:** [`http://localhost:8001`](http://localhost:8001) — Browse and edit records in the `MonitoringHub` table.
* **Mailpit (SNS Emails):** [`http://localhost:8025`](http://localhost:8025) — View alert emails dispatched to `noc-alerts@empresa.com`.
* **Webhook Sink (Echo):** [`http://localhost:8080`](http://localhost:8080) — Inspect payload bodies received during replay (*retry*) tests.
* **LocalStack Gateway:** [`http://localhost:4566`](http://localhost:4566) — Root endpoint for AWS CLI and SDK connections.

### Verification via Terminal:
```bash
# Verify DynamoDB tables
aws --endpoint-url=http://localhost:4566 dynamodb list-tables

# Inspect messages in the critical alerts SQS queue
aws --endpoint-url=http://localhost:4566 sqs receive-message --queue-url http://localhost:4566/000000000000/critical-alerts-queue
```

---

## 🔗 Related Notes
* Step-by-step local guide: [[Local-Development|Local Development Guide]].
* Automated testing against this stack: [[Testing-Strategy|Testing Strategy]].
