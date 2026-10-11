---
title: "NoSQL Data Modeling (DynamoDB Single-Table Design)"
tags:
  - data
  - dynamodb
  - nosql
  - single-table-design
date: 2026-10-11
status: approved
---

# NoSQL Data Modeling (DynamoDB Single-Table Design)

**Alert Pulse** employs the **Single-Table Design** pattern in Amazon DynamoDB. This architecture enables multiple logical entities (endpoint metadata and event logs) to share a single physical table, ensuring unbounded horizontal scalability, partition isolation, and predictable operational costs.

---

## 📋 Table Definition

* **Table Name:** `MonitoringHub`
* **Partition Key (PK):** `string`
* **Sort Key (SK):** `string`
* **Billing Mode:** `PAY_PER_REQUEST` (On-Demand)
* **TTL Attribute:** `ExpiresAt` (Unix Epoch timestamp in seconds)

```mermaid
erDiagram
    ENDPOINT ||--o{ EVENT : "has many events"
    ENDPOINT {
        string PK "ENDPOINT#{id}"
        string SK "METADATA"
        string Name
        string SecretKey
        string UrlPath
        string SeverityThreshold
        string CreatedAt
    }
    EVENT {
        string PK "ENDPOINT#{id}"
        string SK "EVENT#{timestamp}#{id}"
        string Id
        number StatusCode
        number LatencyMs
        string Severity
        string Status
        string SeverityStatus "CRITICAL#OPEN"
        string CreatedAt "ISO 8601"
        number ExpiresAt "Unix Epoch (TTL)"
        string Payload
        string ErrorMessage
    }
```

---

## 🗂️ Entity Mapping

| Entity | PK | SK | Primary Attributes | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Endpoint / Config** | `ENDPOINT#{id}` | `METADATA` | `Id`, `Name`, `SecretKey`, `UrlPath`, `SeverityThreshold`, `CreatedAt`, `IsActive` | Configuration and cryptographic secret for a registered webhook endpoint. |
| **Event / Webhook** | `ENDPOINT#{id}` | `EVENT#{timestamp}#{id}` | `Id`, `EndpointId`, `StatusCode`, `LatencyMs`, `Severity`, `Status`, `SeverityStatus`, `CreatedAt`, `ExpiresAt`, `Payload`, `ErrorMessage` | Ingested webhook record tracking execution details, status, and payload. |

### Item Example: Endpoint Configuration
```json
{
  "PK": { "S": "ENDPOINT#stripe-billing" },
  "SK": { "S": "METADATA" },
  "Id": { "S": "stripe-billing" },
  "Name": { "S": "Stripe Billing Webhook" },
  "SecretKey": { "S": "whsec_98fbc982a7bc40e882a93" },
  "UrlPath": { "S": "/api/v1/webhooks/ingest/stripe-billing" },
  "SeverityThreshold": { "S": "DEFAULT" },
  "CreatedAt": { "S": "2026-10-11T10:00:00.000Z" },
  "IsActive": { "BOOL": true }
}
```

### Item Example: Critical Failure Event
```json
{
  "PK": { "S": "ENDPOINT#stripe-billing" },
  "SK": { "S": "EVENT#1791724286000#d4f2910a3c" },
  "Id": { "S": "d4f2910a3c" },
  "EndpointId": { "S": "stripe-billing" },
  "StatusCode": { "N": "500" },
  "LatencyMs": { "N": "3420" },
  "Severity": { "S": "CRITICAL" },
  "Status": { "S": "OPEN" },
  "SeverityStatus": { "S": "CRITICAL#OPEN" },
  "CreatedAt": { "S": "2026-10-11T10:11:26.000Z" },
  "ExpiresAt": { "N": "1794316286" },
  "Payload": { "S": "{\"event\":\"invoice.payment_failed\",\"customer\":\"cus_991\"}" },
  "ErrorMessage": { "S": "Gateway Timeout: upstream billing server did not respond" }
}
```

---

## 🔍 Global Secondary Index: GSI1-Severity

To allow SRE operators and AppSync to retrieve unmitigated incidents without performing expensive full-table scans, we define the **GSI1-Severity** index.

* **Index Name:** `GSI1-Severity`
* **GSI1-PK:** `SeverityStatus` (e.g., `CRITICAL#OPEN`, `WARNING#OPEN`, `CRITICAL#ACKNOWLEDGED`)
* **GSI1-SK:** `CreatedAt` (ISO 8601 UTC string)
* **Projection:** `ALL`

### Querying GSI1 via AWS CLI:
```bash
# Query: Fetch all CRITICAL incidents in OPEN state, ordered from newest to oldest:
aws dynamodb query \
    --table-name MonitoringHub \
    --index-name GSI1-Severity \
    --key-condition-expression "SeverityStatus = :s AND CreatedAt > :cutoff" \
    --expression-attribute-values '{":s": {"S": "CRITICAL#OPEN"}, ":cutoff": {"S": "2026-10-01T00:00:00Z"}}' \
    --scan-index-forward false
```

---

## ⏳ Retention Policy: Time to Live (TTL)

* **Attribute:** `ExpiresAt`
* **Format:** Unix Epoch timestamp in seconds (`UnixTimeSeconds`).
* **Business Rule:** `ExpiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()`.
* **Benefit:** DynamoDB purges expired items automatically in the background with zero consumption of write units (WCUs) and zero cloud storage bloat.

---

## 🔗 Related Notes
* Matrix of all queries and writes: [[Access-Patterns|Access Patterns & Secondary Indexes]].
* Provisioning CLI command: [[AWS-Setup|AWS Setup (CLI)]].
* C# persistence implementation: [[Service-Implementations|C# Service Implementations]].
