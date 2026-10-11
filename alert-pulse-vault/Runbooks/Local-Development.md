---
title: "Local Development Guide"
tags:
  - runbook
  - development
  - onboarding
  - quickstart
date: 2026-10-11
status: approved
---

# Local Development Guide

This guide walks engineers through spinning up the complete **Alert Pulse** monorepo from scratch using Docker, LocalStack, .NET 10, and Angular 22.

---

## 🧰 Prerequisites

* **Docker & Docker Compose** (Docker Desktop or OrbStack)
* **.NET 10 SDK** (`dotnet --version >= 10.0.x`)
* **Node.js 22+ & npm** (`node --version >= 22.x`)
* **Angular CLI 22+** (`npm install -g @angular/cli`)
* **AWS CLI** configured with dummy credentials (`AWS_ACCESS_KEY_ID=test`, `AWS_SECRET_ACCESS_KEY=test`, `AWS_DEFAULT_REGION=us-east-1`)

> [!TIP] Monorepo Environment
> All commands below are run from the monorepo root (`alert-pulse/`). Packages are structured into `backend/`, `frontend/`, `infra/`, and documentation lives in `alert-pulse-vault/` (or `docs/`). Refer to [[Monorepo-Structure|Monorepo Structure & Strategy]] for unified root `package.json` scripts.

---

## 🚀 Execution Steps

### Step 1: Start Local Support Services (Docker Compose)

From the repository root, run:

```bash
docker compose up -d

# Check container health status
docker compose ps
```

Once containers report healthy, open these developer dashboards:
* 🌐 **DynamoDB Admin (Database UI):** [http://localhost:8001](http://localhost:8001) — Browse `MonitoringHub` records and view pre-seeded endpoints (`stripe-billing`, `github-deployments`).
* ✉️ **Mailpit (Alert Email Inbox):** [http://localhost:8025](http://localhost:8025) — View emails published by SNS.
* 🔁 **Webhook Sink (HTTP Echo Mock):** [http://localhost:8080](http://localhost:8080) — Replay target for webhook retries.
* ☁️ **LocalStack Gateway:** [http://localhost:4566](http://localhost:4566) — Local DynamoDB, SNS, and SQS endpoint.

```bash
# Verify the DynamoDB table created by init-aws.sh
aws --endpoint-url=http://localhost:4566 dynamodb list-tables
```

### Step 2: Run the .NET 10 Backend API

Navigate to the backend API project and start it in watch mode (or let Docker Compose run it on port `5050`):

```bash
cd backend/src/WebhookMonitor

# Restore NuGet dependencies
dotnet restore

# Run with hot-reload enabled
dotnet watch run
```
The API is available at `http://localhost:5000` (or `http://localhost:5050` when running under Docker Compose).
Swagger / OpenAPI: `http://localhost:5050/swagger` (or `http://localhost:5000/swagger`).

### Step 3: Run the Angular 22 Frontend

In a separate terminal, start the Angular development server:

```bash
cd frontend

# Install dependencies
npm install

# Start development server
ng serve --open
```
The monitoring dashboard will open at `http://localhost:4200`.

---

## 🧪 Validating the Pipeline End-to-End

### Option A: Via the Angular Dashboard (Fastest)
1. Open `http://localhost:4200`.
2. Confirm the top status indicator shows: **"WebSocket Connected"**.
3. Click the red button: **"🔥 Simulate Critical (500)"**.
4. Observe a new row with status `OPEN`, HTTP code `500`, and a red `CRITICAL` badge appear instantly without page refresh!
5. Click **"Acknowledge"** and watch the status update to `ACKNOWLEDGED`.
6. Open Mailpit at `http://localhost:8025` to see the notification email dispatched by SNS!

### Option B: Sending an HMAC-Signed Webhook via cURL
To simulate an authentic HMAC-SHA256 signed webhook from the terminal:

```bash
PAYLOAD='{"event":"payment_intent.failed","amount":8900,"currency":"usd"}'
SECRET='whsec_test_secret_123'

# Compute HMAC-SHA256 signature
SIGNATURE=$(echo -n "$PAYLOAD" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')

# Send request to API
curl -X POST http://localhost:5000/api/v1/webhooks/ingest/stripe-billing \
  -H "Content-Type: application/json" \
  -H "X-Hub-Signature-256: sha256=$SIGNATURE" \
  -d "$PAYLOAD"
```

---

## 🔧 Troubleshooting Common Issues

| Symptom | Probable Cause | Resolution |
| :--- | :--- | :--- |
| `ResourceNotFoundException: Cannot do operations on a non-existent table` | `init-aws.sh` has not finished running inside LocalStack. | Run `docker logs alert-pulse-localstack` to inspect bootstrap logs. |
| `401 Unauthorized` on cURL | Extra newline in HMAC computation (`echo` without `-n`). | Always use `echo -n "$PAYLOAD"` to avoid appending trailing newlines. |
| CORS Error in Angular Console | .NET API has not whitelisted `http://localhost:4200`. | Ensure `app.UseCors("AllowFrontend")` is registered before endpoint mappings in `Program.cs`. |

---

## 🔗 Related Notes
* Docker Compose configuration: [[LocalStack-Docker|Local Environment with Docker & LocalStack]].
* Operational runbook for incidents: [[Incident-Response|Incident Response Procedures]].
