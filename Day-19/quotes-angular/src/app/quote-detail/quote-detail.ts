import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { QuotesApi } from '../services/quotes-api';
import { QuotesStore } from '../quotes/quotes.store';
import { AuthSession } from '../auth/auth-session';
import { Quote } from '../models/quote.model';
import { AppError } from '../http/app-error.model';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly quotesApi = inject(QuotesApi);
  private readonly quotesStore = inject(QuotesStore);
  private readonly authSession = inject(AuthSession);

  // Reactive to route param changes (not just a one-time snapshot read), so
  // this component refetches correctly if the Router ever reuses this
  // instance across two different :id values instead of destroying it.
  private readonly paramMap = toSignal(this.route.paramMap, { requireSync: true });

  readonly quote = signal<Quote | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);

  readonly isAuthenticated = this.authSession.isAuthenticated;

  readonly confirmingDelete = signal(false);
  readonly deleting = signal(false);
  readonly deleteError = signal<string | null>(null);

  readonly initials = computed(() => {
    const quote = this.quote();
    if (!quote) {
      return '';
    }
    return quote.author
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  });

  constructor() {
    effect(() => {
      const rawId = this.paramMap().get('id');
      const id = Number(rawId);

      this.quote.set(null);
      this.error.set(null);
      this.notFound.set(false);
      this.confirmingDelete.set(false);
      this.deleteError.set(null);

      if (!rawId || !Number.isInteger(id) || id <= 0) {
        this.loading.set(false);
        this.notFound.set(true);
        return;
      }

      this.loading.set(true);

      this.quotesApi.getQuoteById(id).subscribe({
        next: (data) => {
          this.quote.set(data);
          this.loading.set(false);
        },
        error: (err: AppError) => {
          this.loading.set(false);
          if (err.status === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(err.message);
          }
        },
      });
    });
  }

  requestDelete(): void {
    this.deleteError.set(null);
    this.confirmingDelete.set(true);
  }

  cancelDelete(): void {
    this.confirmingDelete.set(false);
  }

  confirmDelete(): void {
    const quote = this.quote();
    if (!quote || this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.quotesStore.deleteQuote(quote.id).subscribe({
      next: () => {
        this.router.navigateByUrl('/quotes');
      },
      error: (err: AppError) => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.deleteError.set(err.message);
      },
    });
  }
}
