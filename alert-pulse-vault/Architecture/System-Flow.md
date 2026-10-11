---
title: "System Flows & Lifecycles"
tags:
  - architecture
  - sequence-diagram
  - flow
date: 2026-10-11
status: approved
---

# System Flows & Lifecycles

This document details the end-to-end sequences governing the webhook ingestion lifecycle, critical alert fan-out, operational simulation, and incident mitigation workflows.

---

## 1. Primary Flow: Webhook Ingestion & Critical Alerting

```mermaid
sequenceDiagram
    autonumber
    actor External as External Provider (Stripe/GitHub)
    participant API as .NET Web API
    participant DDB as DynamoDB (MonitoringHub)
    participant SNS as AWS SNS (Topic)
    participant Sub as SNS Subscribers (Email/NOC)
    participant AppSync as AWS AppSync
    actor Operator as Angular Frontend (SRE)

    External->>API: POST /api/v1/webhooks/ingest/{endpointId}<br/>[Header: X-Hub-Signature-256]
    activate API
    API->>API: Validate HMAC SHA-256 Signature
    API->>API: Measure Latency & Classify Severity

    API->>DDB: PutItem (PK=ENDPOINT#{id}, SK=EVENT#{ts}#{id}, TTL)
    activate DDB
    DDB-->>API: 200 OK
    deactivate DDB

    alt Severity == CRITICAL
        API->>SNS: PublishAsync (MessageAttributes: Severity=CRITICAL)
        activate SNS
        SNS->>Sub: Fan-out push (Email / SMS / PagerDuty)
        SNS-->>API: 200 OK (MessageId)
        deactivate SNS
    end

    API->>AppSync: HTTP POST GraphQL Mutation (publishWebhookEvent)
    activate AppSync
    AppSync-->>API: 200 OK
    AppSync-->>Operator: WebSocket Subscription (onNewEvent / onCriticalIncident)
    deactivate AppSync

    API-->>External: 202 Accepted { eventId, status: "PROCESSED" }
    deactivate API
```

### Step-by-Step Breakdown:
1. **Reception:** The external webhook provider posts a JSON payload with signature header `X-Hub-Signature-256`.
2. **Validation:** The API fetches the configured secret for the given `endpointId` and validates the HMAC using `FixedTimeEquals`. If invalid, the request is immediately rejected with `401 Unauthorized`.
3. **Classification:** If `statusCode >= 500` or `latencyMs > 5000`, the event is classified as `CRITICAL`.
4. **Guaranteed Persistence:** The record is saved in `MonitoringHub` with initial status `OPEN` and TTL attribute `ExpiresAt` (Epoch + 30 days).
5. **SNS Fan-Out:** If critical, SNS immediately broadcasts notifications across emergency alerting channels.
6. **Real-Time Push:** An AppSync GraphQL mutation triggers an instant WebSocket broadcast to connected Angular dashboards.
7. **Response:** The external caller receives a `202 Accepted` response with the unique `eventId`.

---

## 2. Operational Flow: Incident Simulation

The simulation workflow enables engineers to verify the entire alert pipeline end-to-end without waiting for external upstream outages.

```mermaid
sequenceDiagram
    autonumber
    actor SRE as Operator (Angular Dashboard)
    participant API as .NET Web API
    participant DDB as DynamoDB
    participant SNS as AWS SNS
    participant AppSync as AWS AppSync

    SRE->>API: POST /api/v1/webhooks/simulate<br/>{ endpointId, simulateError: true }
    activate API
    API->>API: Construct synthetic IngestWebhookCommand<br/>(StatusCode=500, Latency=3200ms)
    API->>DDB: PutItem (Synthetic Record in DynamoDB)
    API->>SNS: PublishAsync (Critical Alert to SNS)
    API->>AppSync: publishWebhookEvent Mutation
    AppSync-->>SRE: WebSocket Push: New Incident Appears in Table
    API-->>SRE: 200 OK { eventId }
    deactivate API
```

---

## 3. Mitigation Flow: Incident Acknowledgement (*Acknowledge*)

When an engineer takes ownership of an active incident, they acknowledge it in the UI to notify the team and prevent alert duplicates.

```mermaid
sequenceDiagram
    autonumber
    actor SRE as Operator (Angular Dashboard)
    participant API as .NET Web API
    participant DDB as DynamoDB
    participant AppSync as AWS AppSync

    SRE->>API: POST /api/v1/incidents/{endpointId}/{eventId}/acknowledge
    activate API
    API->>DDB: UpdateItem (Status = 'ACKNOWLEDGED', SeverityStatus = 'CRITICAL#ACKNOWLEDGED')
    activate DDB
    DDB-->>API: 200 OK
    deactivate DDB
    API->>AppSync: Mutation: updateEventStatus(id, ACKNOWLEDGED)
    AppSync-->>SRE: Broadcast updated status (Badge updates to Acknowledged)
    API-->>SRE: 200 OK
    deactivate API
```

---

## 4. Recovery Flow: Payload Replay (*Retry*)

When an upstream endpoint recovers from a transient outage, operators can replay stored webhook payloads directly from the dashboard:

```mermaid
sequenceDiagram
    autonumber
    actor SRE as Operator (Angular Dashboard)
    participant API as .NET Web API
    participant DDB as DynamoDB
    participant Dest as Destination Service

    SRE->>API: POST /api/v1/incidents/{endpointId}/{eventId}/retry
    activate API
    API->>DDB: GetItem (Retrieve Event Payload & Metadata)
    activate DDB
    DDB-->>API: Returns Item with original Payload
    deactivate DDB
    API->>Dest: POST /reprocess (Re-posts original payload)
    Dest-->>API: 200 OK (Success)
    API->>DDB: UpdateItem (Status = 'RESOLVED')
    API-->>SRE: 200 OK { status: 'REPLAY_SUCCESS' }
    deactivate API
```

---

## ⏱️ Latency Budget

| Pipeline Step | Target Latency (p95) | Fallback / Error Handling |
| :--- | :--- | :--- |
| HMAC Signature Validation | < 2 ms | Immediate rejection (`401 Unauthorized`) |
| DynamoDB Persistence | < 15 ms | Abort and return `500 Internal Server Error` |
| SNS Notification Publish | < 30 ms | Executed asynchronously; warning logged on failure |
| AppSync Mutation Dispatch | < 40 ms | Dispatched without blocking HTTP response to caller |
| **Total Ingestion Round-Trip** | **< 90 ms** | **Guaranteed `202 Accepted` delivered to caller** |

---

## 🔗 Related Notes
* Messaging details: [[Event-Driven-Design|Event-Driven Patterns]].
* DynamoDB key modeling: [[DynamoDB-Design|NoSQL Data Modeling]].
* REST API specifications: [[Operational-Endpoints|REST Endpoints Catalog]].
