export interface QuoteEvent {
  eventId: string;
  quoteId: number;
  author: string;
  text: string;
  eventType: string;
  occurredAt: string;
}

export interface AuditLogEntry {
  id: number;
  messageId: string;
  eventType: string;
  quoteId: number;
  author: string;
  text: string;
  deliveryCount: number;
  receivedAt: string;
}

export interface DigestNotification extends AuditLogEntry {
  workerId: string;
}

export interface DeadLetteredMessage {
  id: number;
  subscription: string;
  messageId: string;
  deadLetterReason: string;
  deadLetterErrorDescription: string;
  bodyPreview: string;
  deliveryCount: number;
  deadLetteredAt: string;
}
