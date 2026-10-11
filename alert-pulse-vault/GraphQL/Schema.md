---
title: "GraphQL Schema (AWS AppSync)"
tags:
  - graphql
  - appsync
  - schema
  - api
date: 2026-10-11
status: approved
---

# GraphQL Schema (AWS AppSync)

AWS AppSync exposes the unified GraphQL interface for **Alert Pulse**, bridging historical queries to DynamoDB and real-time subscription channels over WebSockets.

---

## 📜 Schema Definition (SDL)

```graphql
schema {
  query: Query
  mutation: Mutation
  subscription: Subscription
}

"""
Severity levels computed from HTTP status codes and response latencies.
"""
enum SeverityLevel {
  INFO
  WARNING
  CRITICAL
}

"""
Operational status of a webhook incident.
"""
enum IncidentStatus {
  OPEN
  ACKNOWLEDGED
  RESOLVED
}

"""
Represents a captured webhook occurrence.
"""
type WebhookEvent {
  id: ID!
  endpointId: ID!
  timestamp: String!
  statusCode: Int!
  latencyMs: Int!
  severity: SeverityLevel!
  status: IncidentStatus!
  payloadSnippet: String
  errorMessage: String
}

"""
Paginated collection of events for historical inspection.
"""
type EventConnection {
  items: [WebhookEvent!]!
  nextToken: String
}

type Query {
  """
  Retrieves recent events for a given endpoint in descending chronological order.
  """
  getRecentEvents(
    endpointId: ID!
    limit: Int = 20
    nextToken: String
  ): EventConnection!

  """
  Retrieves all open incidents filtered by severity level via GSI1-Severity.
  """
  getOpenIncidents(
    severity: SeverityLevel!
  ): [WebhookEvent!]!
}

type Mutation {
  """
  Publishes a newly ingested event. Invoked by the .NET API to trigger real-time subscriptions.
  """
  publishWebhookEvent(event: WebhookEventInput!): WebhookEvent!

  """
  Updates operational status of an incident (e.g., Acknowledge or Resolve).
  """
  updateEventStatus(
    id: ID!
    endpointId: ID!
    status: IncidentStatus!
  ): WebhookEvent!
}

type Subscription {
  """
  Receives all webhook events in real time. Supports optional filtering by endpointId.
  """
  onNewEvent(endpointId: ID): WebhookEvent
    @aws_subscribe(mutations: ["publishWebhookEvent"])

  """
  Receives only CRITICAL severity incidents in real time.
  """
  onCriticalIncident: WebhookEvent
    @aws_subscribe(mutations: ["publishWebhookEvent"])
}

"""
Input payload supplied by the .NET API when publishing events.
"""
input WebhookEventInput {
  id: ID!
  endpointId: ID!
  timestamp: String!
  statusCode: Int!
  latencyMs: Int!
  severity: SeverityLevel!
  status: IncidentStatus!
  payloadSnippet: String
  errorMessage: String
}
```

---

## 🔎 Directive Mechanics: `@aws_subscribe`

The `@aws_subscribe(mutations: ["publishWebhookEvent"])` directive instructs AppSync to pipe mutation return values into active WebSocket connections:
1. If `onNewEvent` is subscribed with an `endpointId` argument, AppSync delivers the event only if the mutation payload matches that `endpointId`.
2. If subscribed without arguments, the client receives all global events.
3. For `onCriticalIncident`, response mapping filters ensure that only events where `severity == 'CRITICAL'` are delivered.

---

## 🔑 Authentication Modes

Alert Pulse supports dual authentication modes:
1. **`API_KEY` (Local Development & Frontend SPA):** Static key passed via `x-api-key` header for rapid testing with LocalStack.
2. **`AWS_IAM` (Production Backend-to-AppSync):** The .NET API uses SigV4 signing via ECS/EKS IAM roles, ensuring that only trusted backend services can invoke `publishWebhookEvent`.

---

## 🔗 Related Notes
* How resolvers connect to DynamoDB: [[Resolvers-and-Realtime|Resolvers & Real-Time WebSockets]].
* Angular WebSocket client implementation: [[Realtime-Subscriptions|WebSocket Subscriptions]].
