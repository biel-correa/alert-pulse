---
title: "Access Patterns & Secondary Indexes"
tags:
  - data
  - dynamodb
  - access-patterns
  - performance
date: 2026-10-11
status: approved
---

# Access Patterns & Secondary Indexes

This matrix documents all read and write operations executed against the `MonitoringHub` DynamoDB table.

---

## 📊 Access Patterns Matrix

| ID | Business Operation | SDK Call | Key Condition / Filter | Target Index | Complexity |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **AP-01** | Ingest new webhook event | `PutItem` | `PK = ENDPOINT#{id}`, `SK = EVENT#{timestamp}#{id}` | Base Table | O(1) Write |
| **AP-02** | Fetch endpoint config & HMAC secret | `GetItem` | `PK = ENDPOINT#{id}`, `SK = METADATA` | Base Table | O(1) Read |
| **AP-03** | List recent events for an endpoint | `Query` | `PK = ENDPOINT#{id}`, `SK begins_with EVENT#` | Base Table | O(k) Ordered Read |
| **AP-04** | List open incidents by severity | `Query` | `GSI1-PK = SeverityStatus`, `GSI1-SK > {cutoff}` | `GSI1-Severity` | O(k) No-Scan Read |
| **AP-05** | Acknowledge active incident | `UpdateItem` | `PK = ENDPOINT#{id}`, `SK = EVENT#{timestamp}#{id}` | Base Table | O(1) Write |
| **AP-06** | Retrieve stored payload for replay | `GetItem` | `PK = ENDPOINT#{id}`, `SK = EVENT#{timestamp}#{id}` | Base Table | O(1) Read |
| **AP-07** | List all registered endpoints | `Scan` / `Query` | `SK = METADATA` | GSI or filtered Scan | O(N endpoints) |

---

## 🛠️ Technical Breakdown of Key Operations

### AP-01: Ingest Webhook Event (`PutItem`)
* **Trigger:** Executed by the ingestion pipeline upon receiving an external webhook.
* **Payload:**
```json
{
  "TableName": "MonitoringHub",
  "Item": {
    "PK": { "S": "ENDPOINT#stripe-billing" },
    "SK": { "S": "EVENT#1791724286000#abc12345" },
    "Id": { "S": "abc12345" },
    "EndpointId": { "S": "stripe-billing" },
    "StatusCode": { "N": "500" },
    "LatencyMs": { "N": "4100" },
    "Severity": { "S": "CRITICAL" },
    "Status": { "S": "OPEN" },
    "SeverityStatus": { "S": "CRITICAL#OPEN" },
    "CreatedAt": { "S": "2026-10-11T10:11:26Z" },
    "ExpiresAt": { "N": "1794316286" },
    "Payload": { "S": "{...}" }
  }
}
```

### AP-03: Paginated Recent Events for an Endpoint (`Query`)
* **Trigger:** Used by AppSync to resolve `getRecentEvents(endpointId, limit, nextToken)`.
* **Expression:**
```json
{
  "TableName": "MonitoringHub",
  "KeyConditionExpression": "PK = :pk AND begins_with(SK, :skPrefix)",
  "ExpressionAttributeValues": {
    ":pk": { "S": "ENDPOINT#stripe-billing" },
    ":skPrefix": { "S": "EVENT#" }
  },
  "ScanIndexForward": false,
  "Limit": 25
}
```
> [!TIP]
> Setting `ScanIndexForward = false` traverses the range index in reverse chronological order, returning the most recent events first.

### AP-04: Retrieve Open Critical Incidents (`Query GSI1`)
* **Trigger:** Invoked on dashboard initialization to display unresolved failures.
* **Expression:**
```json
{
  "TableName": "MonitoringHub",
  "IndexName": "GSI1-Severity",
  "KeyConditionExpression": "SeverityStatus = :sevStatus",
  "ExpressionAttributeValues": {
    ":sevStatus": { "S": "CRITICAL#OPEN" }
  },
  "ScanIndexForward": false
}
```

### AP-05: Acknowledge Incident (`UpdateItem`)
* **Trigger:** Triggered when an operator clicks "Acknowledge" in the Angular dashboard.
* **Expression:**
```json
{
  "TableName": "MonitoringHub",
  "Key": {
    "PK": { "S": "ENDPOINT#stripe-billing" },
    "SK": { "S": "EVENT#1791724286000#abc12345" }
  },
  "UpdateExpression": "SET #st = :newStatus, SeverityStatus = :newSevStatus",
  "ExpressionAttributeNames": {
    "#st": "Status"
  },
  "ExpressionAttributeValues": {
    ":newStatus": { "S": "ACKNOWLEDGED" },
    ":newSevStatus": { "S": "CRITICAL#ACKNOWLEDGED" }
  }
}
```
> [!IMPORTANT]
> Updating `SeverityStatus` from `CRITICAL#OPEN` to `CRITICAL#ACKNOWLEDGED` immediately removes the item from the open incidents query view, keeping the active queue clean without extra filtering.

---

## ⚡ Mitigating Hot Partitions

For high-volume webhooks (thousands of events per second on a single endpoint), write sharding can be introduced:
* `PK = ENDPOINT#{id}#{hash % 10}`
In default operational workloads, webhooks are distributed naturally by `EndpointId`, effectively distributing write load across physical storage partitions.

---

## 🔗 Related Notes
* Base table schema: [[DynamoDB-Design|NoSQL Data Modeling]].
* Resolving these queries via AppSync: [[Resolvers-and-Realtime|Resolvers & Real-Time WebSockets]].
