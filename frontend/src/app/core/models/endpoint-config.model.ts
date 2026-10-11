export interface EndpointConfig {
  id: string;
  name: string;
  urlPath: string;
  severityThreshold: string;
  createdAt: string;
  isActive: boolean;
  secretKey?: string;
}

export interface CreateEndpointPayload {
  id: string;
  name: string;
  severityThreshold?: string;
}
