---
title: "REST Endpoints Catalog"
tags:
  - backend
  - api
  - rest
  - endpoints
date: 2026-10-11
status: approved
---

# REST Endpoints Catalog

The .NET 10 API exposes high-speed HMAC-protected ingestion endpoints for third parties, as well as operational endpoints consumed by the Angular dashboard.

---

## 📥 1. Webhook Ingestion

### `POST /api/v1/webhooks/ingest/{endpointId}`
Ingests third-party payloads, validates HMAC signatures, calculates latency and severity, persists records, and broadcasts alerts.

* **Required Headers:**
  * `X-Hub-Signature-256`: `sha256=<hex_digest>`
  * `Content-Type`: `application/json`
* **Route Parameters:**
  * `endpointId` (string): Unique endpoint identifier (e.g., `stripe-billing`).

#### Sample Request:
```http
POST /api/v1/webhooks/ingest/stripe-billing HTTP/1.1
Host: api.monitor.internal
Content-Type: application/json
X-Hub-Signature-256: sha256=d25b1842e030e463a8a3a93f1f7d2be92bf4c478a2f45f94d1b32d204d80582d

{
  "event": "invoice.payment_failed",
  "customer_id": "cus_991823",
  "amount_due": 4900,
  "currency": "usd"
}
```

#### Responses:
* `202 Accepted`:
```json
{
  "eventId": "f4892c902b4d45a9a1038e6cb73da2",
  "endpointId": "stripe-billing",
  "status": "PROCESSED",
  "severity": "CRITICAL",
  "processedAt": "2026-10-11T10:11:26Z"
}
```
* `401 Unauthorized`: Missing or invalid HMAC signature.
* `404 Not Found`: Unknown `endpointId`.

---

## 🧪 2. Operational & Simulation Endpoints

### `POST /api/v1/webhooks/simulate`
Generates synthetic webhook events to verify the entire pipeline (ingestion -> persistence -> SNS -> Angular WebSocket) without relying on external providers.

#### Sample Request:
```json
{
  "endpointId": "stripe-billing",
  "statusCode": 500,
  "payload": "{\"event\": \"ping\", \"test\": true}",
  "latencyMs": 3200,
  "errorMessage": "Simulated Gateway Timeout"
}
```

#### Response:
* `200 OK`:
```json
{
  "eventId": "sim_84820a1b2c",
  "status": "SIMULATED",
  "severity": "CRITICAL"
}
```

---

### `POST /api/v1/incidents/{endpointId}/{eventId}/acknowledge`
Marks an open incident as acknowledged by an on-call engineer, updating DynamoDB and notifying AppSync subscribers.

* **Parameters:**
  * `endpointId` (string)
  * `eventId` (string)

#### Response:
* `200 OK`:
```json
{
  "eventId": "d4f2910a3c",
  "status": "ACKNOWLEDGED",
  "acknowledgedAt": "2026-10-11T10:14:00Z"
}
```

---

### `POST /api/v1/incidents/{endpointId}/{eventId}/retry`
Retrieves the raw payload preserved in DynamoDB and replays the request against the destination service.

* **Parameters:**
  * `endpointId` (string)
  * `eventId` (string)

#### Response:
* `200 OK`:
```json
{
  "status": "RETRY_DISPATCHED",
  "eventId": "d4f2910a3c",
  "targetResponseStatusCode": 200
}
```

---

## ⚙️ 3. Endpoint Configuration (CRUD)

### `GET /api/v1/endpoints`
Lists all registered webhook endpoint configurations.

#### Response `200 OK`:
```json
[
  {
    "id": "stripe-billing",
    "name": "Stripe Billing Events",
    "urlPath": "/api/v1/webhooks/ingest/stripe-billing",
    "severityThreshold": "DEFAULT",
    "createdAt": "2026-10-11T10:00:00Z",
    "isActive": true
  }
]
```

### `POST /api/v1/endpoints`
Registers a new endpoint and auto-generates its cryptographic HMAC secret key.

#### Sample Request:
```json
{
  "id": "github-deployments",
  "name": "GitHub CI/CD Webhooks",
  "severityThreshold": "DEFAULT"
}
```

#### Response `201 Created`:
```json
{
  "id": "github-deployments",
  "name": "GitHub CI/CD Webhooks",
  "secretKey": "whsec_7d8291a92e81b2390a829104c8f2b189",
  "urlPath": "/api/v1/webhooks/ingest/github-deployments",
  "createdAt": "2026-10-11T10:15:00Z"
}
```
> [!WARNING]
> The `secretKey` must be recorded immediately and configured in the external webhook provider. For security reasons, secrets are not exposed in open listing endpoints.

---

## 🔗 Related Notes
* Service implementation details: [[Service-Implementations|C# Service Implementations]].
* Consumption by frontend: [[Monitoring-Service|Telemetry & State Service]].
