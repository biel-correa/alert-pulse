---
title: "Ingestion Pipeline & HMAC Validation"
tags:
  - backend
  - ingestion
  - security
  - hmac
  - dotnet
date: 2026-10-11
status: approved
---

# Ingestion Pipeline & HMAC Validation

The ingestion pipeline of **Alert Pulse** is the gateway responsible for receiving webhooks, verifying cryptographic authenticity, classifying event severity, and orchestrating persistence and downstream alerting.

---

## 🔒 1. Cryptographic HMAC SHA-256 Validation

To guarantee that incoming webhooks originate legitimately from trusted providers (preventing malicious payloads or spoofing attacks), Alert Pulse implements the industry-standard HMAC SHA-256 scheme adopted by GitHub, Stripe, and Shopify via the `X-Hub-Signature-256` header.

```mermaid
flowchart TD
    req["Webhook HTTP POST\nHeader: X-Hub-Signature-256 = sha256={hash}"] --> getSecret["Fetch Endpoint SecretKey\n(DynamoDB: PK=ENDPOINT#{id}, SK=METADATA)"]
    getSecret --> readBody["Read Raw Request Body (UTF-8 Bytes)"]
    readBody --> computeHmac["Compute HMAC-SHA256(RawBody, SecretKey)"]
    computeHmac --> compare{"CryptographicOperations.\nFixedTimeEquals(Computed, HeaderHash)?"}
    compare -- Yes --> proceed["Proceed to Classification & Ingestion"]
    compare -- No --> reject["401 Unauthorized\n(Invalid Signature)"]
```

### Mitigating Timing Attacks
> [!IMPORTANT]
> Never compare cryptographic hashes using standard string equality (`hashA == hashB`). Standard equality operators abort comparison at the first mismatched byte, allowing attackers to incrementally discover hash bytes by measuring sub-millisecond latency differentials.
> Always utilize `CryptographicOperations.FixedTimeEquals(computedBytes, headerBytes)`.

---

## ⚖️ 2. Severity Classification Logic

Incident severity is evaluated from the combined status code and measured round-trip response latency (`latencyMs`):

```csharp
public static string ClassifySeverity(int statusCode, int latencyMs)
{
    // Server errors or intolerable latency (> 5s) trigger immediate critical alerts
    if (statusCode >= 500 || latencyMs > 5000)
    {
        return "CRITICAL";
    }

    // Client errors or degraded performance (> 2s) trigger warning notices
    if (statusCode >= 400 || latencyMs > 2000)
    {
        return "WARNING";
    }

    // Healthy responses within SLA are informational
    return "INFO";
}
```

### Decision Matrix:
| Status Code | Latency (ms) | Resulting Severity | Triggered Actions |
| :---: | :---: | :---: | :--- |
| `200 - 299` | `<= 2000` | **INFO** | Save to DynamoDB + Notify AppSync |
| `200 - 299` | `> 2000` and `<= 5000` | **WARNING** | Save to DynamoDB + Notify AppSync |
| `200 - 299` | `> 5000` | **CRITICAL** | Save to DynamoDB + Notify AppSync + **Publish SNS Alert** |
| `400 - 499` | `<= 5000` | **WARNING** | Save to DynamoDB + Notify AppSync |
| `400 - 499` | `> 5000` | **CRITICAL** | Save to DynamoDB + Notify AppSync + **Publish SNS Alert** |
| `>= 500` | Any | **CRITICAL** | Save to DynamoDB + Notify AppSync + **Publish SNS Alert** |

---

## 🔄 3. Three-Step Execution Pipeline

```mermaid
sequenceDiagram
    participant Svc as WebhookIngestionService
    participant DDB as DynamoDB
    participant SNS as AWS SNS
    participant AppSync as AWS AppSync

    Note over Svc: Input: IngestWebhookCommand(endpointId, code, body, latency)

    rect rgb(240, 248, 255)
        Note over Svc, DDB: Step 1: Mandatory Persistence
        Svc->>DDB: PutItemAsync(Item with 30-day TTL)
    end

    rect rgb(255, 240, 240)
        opt Severity == CRITICAL
            Note over Svc, SNS: Step 2: Fan-Out Emergency Alert
            Svc->>SNS: PublishAsync(Subject, Payload, MessageAttribute: CRITICAL)
        end
    end

    rect rgb(240, 255, 240)
        Note over Svc, AppSync: Step 3: Real-Time UI Broadcast
        Svc->>AppSync: NotifyDashboardAsync(GraphQL Mutation)
    end
```

### Fault Isolation:
* If DynamoDB persistence fails, the operation aborts and returns `500`, guaranteeing state consistency.
* If DynamoDB succeeds, downstream network failures in AppSync are caught and logged as warnings, preventing transient WebSocket hiccups from dropping valid webhooks or blocking SNS alerts.

---

## 🔗 Related Notes
* Full C# service implementation: [[Service-Implementations|C# Service Implementations]].
* REST endpoint contracts: [[Operational-Endpoints|REST Endpoints Catalog]].
