---
title: "AWS Console Setup & Resource Runbook"
tags:
  - infrastructure
  - aws-console
  - aws
  - devops
  - manual-setup
date: 2026-10-11
status: active
---

# AWS Console Setup & Resource Runbook

This document details the step-by-step procedures to provision, configure, and operate all **Alert Pulse** cloud resources directly via the **AWS Management Console**.

---

## 1. Amazon DynamoDB Setup

1. Open **Amazon DynamoDB Console** -> **Tables** -> **Create table**.
2. **Table details**:
   - Table name: `MonitoringHub`
   - Partition key: `PK` (String)
   - Sort key: `SK` (String)
   - Table class: DynamoDB Standard
3. **Table settings**:
   - Customize settings -> **Read/write capacity settings**: On-demand (*Pay per request*).
4. **Global Secondary Index (GSI)**:
   - Click **Create global index**:
     - Partition key: `SeverityStatus` (String)
     - Sort key: `CreatedAt` (String)
     - Index name: `GSI1-Severity`
     - Attribute projections: `All`
5. **Time to Live (TTL)**:
   - Go to **Additional settings** -> **Time to Live (TTL)** -> **Enable TTL**.
   - TTL attribute name: `ExpiresAt`.
6. Click **Create table**.

---

## 2. Amazon SNS & SQS DLQ Setup

### A. Dead-Letter Queue (DLQ)
1. Open **Amazon SQS Console** -> **Create queue**.
2. Type: **Standard**.
3. Name: `critical-alerts-dlq`.
4. Message retention period: `14 days` (`1209600 seconds`).
5. Click **Create queue**. Copy the Queue ARN.

### B. Main Critical Alerts Queue with Redrive Policy
1. In Amazon SQS Console, click **Create queue**.
2. Type: **Standard**.
3. Name: `critical-alerts-queue`.
4. Scroll to **Dead-letter queue**:
   - Toggle **Enabled**.
   - Select `critical-alerts-dlq`.
   - Maximum receives: `3`.
5. Click **Create queue**. Copy the Queue ARN.

### C. SNS Topic for Critical Alerts
1. Open **Amazon SNS Console** -> **Topics** -> **Create topic**.
2. Type: **Standard**.
3. Name: `WebhookCriticalAlerts`.
4. Click **Create topic**. Copy the Topic ARN.

### D. Subscriptions
1. In the `WebhookCriticalAlerts` topic page, click **Create subscription**:
   - **Email Subscription**:
     - Protocol: `Email`.
     - Endpoint: `noc-alerts@yourdomain.com`.
     - Subscription filter policy:
       ```json
       {
         "Severity": ["CRITICAL"]
       }
       ```
   - **SQS Subscription**:
     - Protocol: `Amazon SQS`.
     - Endpoint: Select `critical-alerts-queue`.
     - Subscription filter policy:
       ```json
       {
         "Severity": ["CRITICAL"]
       }
       ```

---

## 3. AWS AppSync GraphQL API Setup

1. Open **AWS AppSync Console** -> **Create API** -> **Design from scratch** (or *Create with existing schema*).
2. API Name: `AlertPulseApi`.
3. Default authorization mode: `API key` (Set description and 365 days expiration).
4. **Schema**:
   - Paste the contents of [`schema.graphql`](file:///Users/gcorrea/Documents/projetos/alert-pulse/infra/appsync/schema.graphql).
   - Click **Save Schema**.
5. **Data Sources**:
   - Create `NoneDataSource`:
     - Type: `None`.
   - Create `DynamoDBDataSource`:
     - Type: `Amazon DynamoDB table`.
     - Region: Your region.
     - Table: `MonitoringHub`.
     - Create a new role or select an existing IAM role with read/query permissions on `MonitoringHub`.
6. **Resolvers**:
   - Attach resolvers using the VTL mapping templates located in [`infra/appsync/resolvers/`](file:///Users/gcorrea/Documents/projetos/alert-pulse/infra/appsync/resolvers/):
     - `Mutation.publishWebhookEvent` -> `NoneDataSource`
     - `Mutation.updateEventStatus` -> `NoneDataSource`
     - `Query.getOpenIncidents` -> `DynamoDBDataSource`
     - `Query.getRecentEvents` -> `DynamoDBDataSource`
7. Copy the **GraphQL Endpoint URL**, **Realtime WebSocket URL**, and **API Key** into your application configuration.

---

## 4. IAM Roles for Webhook Dispatcher API

The .NET backend requires AWS credentials to interact with DynamoDB, SNS, and AppSync. Rather than hardcoding long-term access keys, assign an IAM Role to your compute environment (**AWS Lambda**, **AWS App Runner**, or **ECS Fargate**).

---

### Step 4.1: Create the Custom Permissions Policy

1. Open the [IAM Console](https://console.aws.amazon.com/iam) -> In the left sidebar, click **Policies** -> Click **Create policy**.
2. Select the **JSON** tab and paste the following least-privilege policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "DynamoDbTableAndIndexAccess",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query",
        "dynamodb:Scan"
      ],
      "Resource": [
        "arn:aws:dynamodb:*:*:table/MonitoringHub",
        "arn:aws:dynamodb:*:*:table/MonitoringHub/index/*"
      ]
    },
    {
      "Sid": "SnsPublishCriticalAlerts",
      "Effect": "Allow",
      "Action": [
        "sns:Publish"
      ],
      "Resource": "arn:aws:sns:*:*:WebhookCriticalAlerts"
    },
    {
      "Sid": "AppSyncGraphQLOperations",
      "Effect": "Allow",
      "Action": [
        "appsync:GraphQL"
      ],
      "Resource": "arn:aws:appsync:*:*:apis/*/types/Mutation/*"
    }
  ]
}
```

3. Click **Next**.
4. **Policy details**:
   * **Policy name**: `AlertPulseBackendPolicy`
   * **Description**: `Grants Alert Pulse backend least-privilege permissions to DynamoDB, SNS, and AppSync.`
5. Click **Create policy**.

---

### Step 4.2: Create the IAM Role

1. In the IAM left sidebar, click **Roles** -> Click **Create role**.
2. **Select trusted entity**:
   * **Trusted entity type**: Choose **AWS service**.
   * **Use case**: Pick the compute service hosting your backend:
     * For **AWS Lambda**: Select **Lambda** (`lambda.amazonaws.com`).
     * For **AWS App Runner**: Under *Use case for other AWS services*, choose **App Runner** -> **App Runner Task (Custom trust policy)**.
     * For **Amazon ECS Fargate**: Choose **Elastic Container Service** -> select **Elastic Container Service Task** (`ecs-tasks.amazonaws.com`).
3. Click **Next**.
4. **Add permissions**:
   * In the search box, search for `AlertPulseBackendPolicy` (the policy created in Step 4.1) and check its box.
   * *(For Lambda)*: Also search for and attach AWS managed policy `AWSLambdaBasicExecutionRole` (allows writing logs to Amazon CloudWatch).
5. Click **Next**.
6. **Name, review, and create**:
   * **Role name**: `AlertPulseBackendExecutionRole`
   * **Description**: `Execution role for Alert Pulse .NET backend API.`
7. Click **Create role**.

---

### Step 4.3: Verify the Trust Relationship Policy

Ensure your role's **Trust relationships** tab matches your target hosting service:

* **For AWS Lambda (Execution Role):**
  ```json
  {
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Principal": {
          "Service": "lambda.amazonaws.com"
        },
        "Action": "sts:AssumeRole"
      }
    ]
  }
  ```

* **For AWS App Runner (Instance Role):**
  ```json
  {
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Principal": {
          "Service": "tasks.apprunner.amazonaws.com"
        },
        "Action": "sts:AssumeRole"
      }
    ]
  }
  ```

* **For Amazon ECS Fargate (Task Role):**
  ```json
  {
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Principal": {
          "Service": "ecs-tasks.amazonaws.com"
        },
        "Action": "sts:AssumeRole"
      }
    ]
  }
  ```

---

### Step 4.4: Attach the Role to Your Service

* **If using AWS Lambda:**
  1. Open your Lambda Function -> **Configuration** -> **Permissions**.
  2. Under **Execution role**, select or edit to assign `AlertPulseBackendExecutionRole`.
* **If using AWS App Runner:**
  1. Go to **App Runner** -> Open or create your service.
  2. Under **Security** -> **Instance role**, select `AlertPulseBackendExecutionRole`.
* **If using Amazon ECS Task Definition:**
  1. In the Task Definition configuration under **Task Role**, select `AlertPulseBackendExecutionRole`.
* **Result**: The AWS SDK in .NET automatically discovers these IAM credentials via `DefaultAWSCredentials` with zero hardcoded secret keys.

---

## 5. Serverless Backend with AWS Lambda & Amazon API Gateway

Deploying the .NET 10 API as a serverless AWS Lambda function provides auto-scaling to zero with pay-per-request economics.

### Step 5.1: Package and Publish the .NET 10 Lambda Function

The application uses `Amazon.Lambda.AspNetCoreServer.Hosting`, allowing the same Minimal API codebase to run in Lambda via API Gateway HTTP API.

1. **Publish via ZIP Package (Custom Runtime on Amazon Linux 2023)**:
   ```bash
   cd backend/src/WebhookMonitor

   # Publish self-contained executable for Linux ARM64 or x86_64
   dotnet publish -c Release -r linux-arm64 --self-contained -o ./publish-lambda \
       /p:PublishSingleFile=true /p:StripSymbols=true

   # Create deployment zip package
   cd publish-lambda && zip -r ../lambda-package.zip . && cd ..
   ```

2. **Or Publish via Container Image (ECR)**:
   ```bash
   # Build container using backend/Dockerfile
   docker build -f backend/Dockerfile -t alert-pulse-backend:lambda .
   docker tag alert-pulse-backend:lambda <account-id>.dkr.ecr.us-east-1.amazonaws.com/alert-pulse-backend:lambda
   docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/alert-pulse-backend:lambda
   ```

---

### Step 5.2: Create the Lambda Function in AWS Console

1. Open **AWS Lambda Console** -> Click **Create function**.
2. Choose **Author from scratch** (for ZIP) or **Container image** (for ECR):
   * **Function name**: `AlertPulseBackendApi`.
   * **Runtime**: `.NET 8` or `Amazon Linux 2023 (provided.al2023)`.
   * **Architecture**: `arm64` (Graviton2/3, 20% cheaper) or `x86_64`.
   * **Permissions**: Change default execution role -> Select **Use an existing role** -> Choose `AlertPulseBackendExecutionRole`.
3. Click **Create function**.
4. **Configuration**:
   * Go to **Configuration** -> **General configuration** -> **Edit**:
     * **Memory**: `1024 MB` (ensures fast cold starts and balanced CPU allocation).
     * **Timeout**: `15 seconds`.
   * Go to **Configuration** -> **Environment variables** -> **Edit** and add:
     ```env
     ASPNETCORE_ENVIRONMENT=Production
     AWS__Region=us-east-1
     AWS__UseLocalStack=false
     AWS__DynamoDbTable=MonitoringHub
     AWS__SnsAlertTopicArn=arn:aws:sns:us-east-1:<account-id>:WebhookCriticalAlerts
     AppSync__GraphQLEndpoint=https://<api-id>.appsync-api.us-east-1.amazonaws.com/graphql
     AppSync__ApiKey=da2-<your-appsync-api-key>
     ```

---

### Step 5.3: Create Amazon API Gateway (HTTP API)

1. Open **Amazon API Gateway Console** -> Click **Create API**.
2. Find **HTTP API** -> Click **Build**.
3. **Configure API**:
   * API name: `AlertPulseHttpApi`.
   * Under **Integrations**, click **Add integration**:
     * Choose **Lambda**.
     * Select your Lambda function: `AlertPulseBackendApi`.
4. **Configure routes**:
   * Set Method: `ANY` -> Resource path: `/{proxy+}`.
   * Click **Add route**: Method: `ANY` -> Resource path: `/`.
5. **Configure stages**:
   * Keep default stage `$default` with **Auto-deploy** enabled.
6. Click **Next** -> Click **Create**.
7. **Configure CORS**:
   * In API Gateway, navigate to **Develop** -> **CORS** -> Click **Configure**:
     * **Access-Control-Allow-Origin**: `http://localhost:4200`, `https://<your-cloudfront-domain>.cloudfront.net` (or `*`).
     * **Access-Control-Allow-Methods**: `GET`, `POST`, `OPTIONS`.
     * **Access-Control-Allow-Headers**: `*`.
     * Click **Save**.
8. Copy the **Invoke URL** (e.g. `https://<api-id>.execute-api.us-east-1.amazonaws.com`). This becomes your `apiUrl` in [`frontend/src/environments/environment.ts`](file:///Users/gcorrea/Documents/projetos/alert-pulse/frontend/src/environments/environment.ts).

---

## 🔗 Related Notes
* CLI reference: [[AWS-Setup|AWS Setup (CLI)]].
* Architecture overview: [[Overview|Architecture Overview]].
* Local testing with LocalStack: [[LocalStack-Docker|Local Environment with Docker & LocalStack]].
