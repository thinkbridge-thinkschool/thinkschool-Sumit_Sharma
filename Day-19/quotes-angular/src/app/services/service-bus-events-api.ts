import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AuditLogEntry,
  DeadLetteredMessage,
  DigestNotification,
  QuoteEvent,
} from '../models/service-bus-event.model';

@Injectable({
  providedIn: 'root',
})
export class ServiceBusEventsApi {
  private readonly http = inject(HttpClient);

  /** Publishes to the "quotes.events" topic - fans out to both subscriptions. */
  publishQuoteCreated(
    quoteId: number,
    author: string,
    text: string,
    simulateCrash: boolean,
  ): Observable<QuoteEvent> {
    return this.http.post<QuoteEvent>('/api/events/quote-created', {
      quoteId,
      author,
      text,
      simulateCrash,
    });
  }

  /** Publishes a message whose body every subscriber will fail to parse. */
  publishPoison(): Observable<{ messageId: string }> {
    return this.http.post<{ messageId: string }>('/api/events/publish-poison', {});
  }

  getAuditLog(): Observable<AuditLogEntry[]> {
    return this.http.get<AuditLogEntry[]>('/api/events/audit-log');
  }

  getDigest(): Observable<DigestNotification[]> {
    return this.http.get<DigestNotification[]>('/api/events/digest');
  }

  getDeadLetters(): Observable<DeadLetteredMessage[]> {
    return this.http.get<DeadLetteredMessage[]>('/api/events/dead-letters');
  }
}
