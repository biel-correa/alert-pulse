---
title: "AWS Setup (CLI & IAM Policies)"
tags:
  - infrastructure
  - aws
  - cli
  - iam
  - dynamodb
  - sns
  - appsync
date: 2026-10-11
status: approved
---

# AWS Setup (CLI & IAM Policies)

This document provides official **AWS CLI** commands and least-privilege IAM policies required to configure cloud resources for **Alert Pulse**.

---

## 1. Create DynamoDB Table with GSI & TTL

```bash
# 1. Create MonitoringHub table in on-demand mode (PAY_PER_REQUEST)
aws dynamodb create-table \
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

# 2. Enable Time to Live (TTL) on ExpiresAt attribute
aws dynamodb update-time-to-live \
    --table-name MonitoringHub \
    --time-to-live-specification "Enabled=true, AttributeName=ExpiresAt"
```

---

## 2. Create SNS Topic & Filtered Subscriptions

```bash
# 1. Create topic for critical alerts
TOPIC_ARN=$(aws sns create-topic \
    --name WebhookCriticalAlerts \
    --query "TopicArn" \
    --output text)

echo "Topic created: $TOPIC_ARN"

# 2. Subscribe NOC team email with filter policy matching only CRITICAL alerts
aws sns subscribe \
    --topic-arn "$TOPIC_ARN" \
    --protocol email \
    --notification-endpoint noc-alerts@empresa.com \
    --filter-policy '{"Severity": ["CRITICAL"]}'
```

---

## 3. Create AWS AppSync GraphQL API

```bash
# 1. Create GraphQL API with API Key authentication
API_ID=$(aws appsync create-graphql-api \
    --name AlertPulseApi \
    --authentication-type API_KEY \
    --query "graphqlApi.apiId" \
    --output text)

# 2. Generate an API Key valid for 365 days
aws appsync create-api-key \
    --api-id "$API_ID" \
    --description "Development key for Dashboard and API"

# 3. Deploy GraphQL schema file
aws appsync start-schema-creation \
    --api-id "$API_ID" \
    --definition fileb://../GraphQL/schema.graphql
```

---

## 🛡️ Least-Privilege IAM Policy (.NET API Role)

The .NET API requires an IAM role attached to its Kubernetes pod (IRSA) or ECS task:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "DynamoDbAccess",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query"
      ],
      "Resource": [
        "arn:aws:dynamodb:*:*:table/MonitoringHub",
        "arn:aws:dynamodb:*:*:table/MonitoringHub/index/*"
      ]
    },
    {
      "Sid": "SnsPublishAccess",
      "Effect": "Allow",
      "Action": [
        "sns:Publish"
      ],
      "Resource": "arn:aws:sns:*:*:WebhookCriticalAlerts"
    },
    {
      "Sid": "AppSyncMutationAccess",
      "Effect": "Allow",
      "Action": [
        "appsync:GraphQL"
      ],
      "Resource": "arn:aws:appsync:*:*:apis/*/types/Mutation/*"
    }
  ]
}
```

---

## 🔗 Related Notes
* Cloud setup runbook: [[AWS-Console|AWS Console Setup & Resource Runbook]].
* Run locally without cloud accounts: [[LocalStack-Docker|Local Environment with Docker & LocalStack]].
