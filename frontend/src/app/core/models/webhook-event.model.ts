export type SeverityLevel = 'INFO' | 'WARNING' | 'CRITICAL';
export type IncidentStatus = 'OPEN' | 'ACKNOWLEDGED' | 'RESOLVED';

export interface WebhookEvent {
  id: string;
  endpointId: string;
  timestamp: string;
  statusCode: number;
  latencyMs: number;
  severity: SeverityLevel;
  status: IncidentStatus;
  payloadSnippet?: string;
  errorMessage?: string;
}

export interface WebhookEventInput {
  id: string;
  endpointId: string;
  timestamp: string;
  statusCode: number;
  latencyMs: number;
  severity: SeverityLevel;
  status: IncidentStatus;
  payloadSnippet?: string;
  errorMessage?: string;
}

export interface EventConnection {
  items: WebhookEvent[];
  nextToken?: string | null;
}
