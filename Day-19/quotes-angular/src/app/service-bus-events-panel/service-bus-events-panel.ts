import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ServiceBusEventsApi } from '../services/service-bus-events-api';
import {
  AuditLogEntry,
  DeadLetteredMessage,
  DigestNotification,
} from '../models/service-bus-event.model';
import { AuthSession } from '../auth/auth-session';
import { AppError } from '../http/app-error.model';

const REFRESH_INTERVAL_MS = 2000;

/**
 * Demonstrates Day-19's Service Bus topic end to end: publishing fans out
 * to two subscriptions ("audit-log", consumed by one worker, and
 * "digest-notifications", consumed by three competing workers), handlers
 * dedupe on message id, and a deliberately malformed "poison" message ends
 * up in each subscription's dead-letter queue once MaxDeliveryCount is
 * exhausted.
 */
@Component({
  selector: 'app-service-bus-events-panel',
  imports: [FormsModule],
  templateUrl: './service-bus-events-panel.html',
  styleUrl: './service-bus-events-panel.css',
})
export class ServiceBusEventsPanel implements OnDestroy {
  private readonly api = inject(ServiceBusEventsApi);
  private refreshHandle: ReturnType<typeof setInterval> | null = null;
  private nextQuoteId = 1;

  protected readonly authSession = inject(AuthSession);
  protected readonly expanded = signal(false);

  protected readonly author = signal('Ada Lovelace');
  protected readonly text = signal('The Analytical Engine weaves algebraic patterns.');
  protected readonly simulateCrash = signal(false);

  protected readonly publishError = signal<string | null>(null);
  protected readonly lastPublished = signal<string | null>(null);

  protected readonly auditLog = signal<AuditLogEntry[]>([]);
  protected readonly digest = signal<DigestNotification[]>([]);
  protected readonly deadLetters = signal<DeadLetteredMessage[]>([]);

  toggle(): void {
    this.expanded.update((value) => !value);

    if (this.expanded()) {
      this.refresh();
      this.refreshHandle = setInterval(() => this.refresh(), REFRESH_INTERVAL_MS);
    } else if (this.refreshHandle !== null) {
      clearInterval(this.refreshHandle);
      this.refreshHandle = null;
    }
  }

  publish(): void {
    this.publishError.set(null);

    const quoteId = this.nextQuoteId++;

    this.api.publishQuoteCreated(quoteId, this.author(), this.text(), this.simulateCrash()).subscribe({
      next: (evt) =>
        this.lastPublished.set(
          `Published "${evt.eventType}" (event ${evt.eventId.slice(0, 8)}) for quote ${evt.quoteId} — fanning out to both subscriptions now.`,
        ),
      error: (err: AppError) => this.publishError.set(err.message),
    });
  }

  publishPoison(): void {
    this.publishError.set(null);

    this.api.publishPoison().subscribe({
      next: (result) =>
        this.lastPublished.set(
          `Published a poison message (${result.messageId.slice(0, 8)}) — every subscription's handler will fail to parse it and it will end up dead-lettered after a few seconds.`,
        ),
      error: (err: AppError) => this.publishError.set(err.message),
    });
  }

  private refresh(): void {
    this.api.getAuditLog().subscribe((entries) => this.auditLog.set(entries));
    this.api.getDigest().subscribe((entries) => this.digest.set(entries));
    this.api.getDeadLetters().subscribe((entries) => this.deadLetters.set(entries));
  }

  ngOnDestroy(): void {
    if (this.refreshHandle !== null) {
      clearInterval(this.refreshHandle);
    }
  }
}
