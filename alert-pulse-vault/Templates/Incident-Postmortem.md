---
title: "Incident Post-Mortem Template"
tags:
  - template
  - post-mortem
  - sre
  - incident
date: 2026-10-11
status: draft
---

# Incident Post-Mortem Template (Blameless)

> [!NOTE]
> This template follows a blameless post-mortem philosophy. The focus is to understand process and system failure modes rather than assigning personal fault.

---

## 📋 Executive Summary

| Field | Detail |
| :--- | :--- |
| **Incident ID** | `INC-YYYYMMDD-001` |
| **Incident Date** | YYYY-MM-DD |
| **Severity** | `CRITICAL` / `WARNING` |
| **Incident Duration** | XX minutes |
| **Impacted Service** | `stripe-billing` Endpoint / .NET Ingestion API |
| **Incident Commander** | @engineer.name |
| **Mitigation Status** | `RESOLVED` |

---

## 💥 Business Impact & SLA Breaches

* **Dropped / Delayed Webhooks:** ~X events.
* **Customer Facing Impact:** Users observed delayed payment receipts or billing retries.
* **SLA Breaches:** Initial response time was XX minutes (SLA threshold: 15 minutes).

---

## ⏱️ Timeline of Events (UTC)

* **10:11:** External provider started sending failing payloads (HTTP 500 or latency > 5000ms).
* **10:11:** .NET Ingestion Service detected `CRITICAL`, persisted record to DynamoDB, and published alert to SNS.
* **10:12:** On-call engineer received PagerDuty alert and opened Alert Pulse Dashboard.
* **10:14:** Engineer clicked *Acknowledge* on dashboard and began log analysis.
* **10:25:** Identified transient connection saturation on upstream billing dependency.
* **10:35:** Upstream service recovered; engineer triggered batch *Retry* from dashboard.
* **10:38:** All pending payloads reprocessed successfully (HTTP 200). Incident closed.

---

## 🔍 Root Cause Analysis (5 Whys)

1. **Why was the critical alert triggered?** Because the webhook endpoint began responding with HTTP 500 and response latency exceeding 4 seconds.
2. **Why did the endpoint respond 500?** Because the database connection pool reached maximum capacity.
3. **Why was connection capacity exhausted?** Because a heavy analytical batch report was triggered during peak webhook hours.
4. **Why did the report run during peak hours?** Because the analytical cron lacked a maintenance window constraint.
5. **Root Cause:** Lack of isolation between background batch analytics and the transactional connection pool used for incoming webhooks.

---

## 💡 Lessons Learned

### What went well?
* **Alert Pulse** detected the anomaly within 90ms and dispatched the SNS emergency notification immediately.
* The *Retry* mechanism enabled full event recovery without requiring manual database intervention.

### What went wrong?
* Responders experienced brief delay locating raw error traces prior to viewing the dashboard detail snippet.

### Where did we get lucky?
* The 30-day DynamoDB TTL ensured no payloads were prematurely dropped while triage was underway.

---

## 📌 Corrective & Preventive Action Items

| Action Item | Category | Owner | Due Date | Tracking Ticket |
| :--- | :--- | :--- | :--- | :--- |
| Migrate heavy batch queries to read replica | Fix | @data-team | YYYY-MM-DD | `PROJ-101` |
| Add batch replay button to Angular Dashboard | UI Enhancement | @frontend-team | YYYY-MM-DD | `PROJ-102` |
| Add SMS fallback escalation if PagerDuty unacked | Resilience | @sre-team | YYYY-MM-DD | `PROJ-103` |
