import { Injectable, computed, inject, signal } from '@angular/core';
import { QuotesApi } from '../services/quotes-api';
import { Quote } from '../models/quote.model';
import { AppError } from '../http/app-error.model';

/**
 * Owns the Quotes feature's state: the source-of-truth signals (quotes,
 * loading, error, author filter) plus everything derivable from them.
 * Components read the readonly signals below and call the methods to
 * change state — they never touch a writable signal directly.
 */
@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly quotesApi = inject(QuotesApi);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _authorFilter = signal('all');

  // Exposed as readonly so nothing outside this service can mutate state
  // directly — every change to feature state goes through load()/selectAuthor().
  readonly quotes = this._quotes.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly authorFilter = this._authorFilter.asReadonly();

  readonly authors = computed(() => {
    const unique = new Set(this._quotes().map((q) => q.author));
    return ['all', ...Array.from(unique).sort()];
  });

  readonly authorCount = computed(() => this.authors().length - 1);

  readonly filteredQuotes = computed(() => {
    const filter = this._authorFilter();
    return filter === 'all'
      ? this._quotes()
      : this._quotes().filter((q) => q.author === filter);
  });

  readonly quoteCount = computed(() => this.filteredQuotes().length);

  readonly isEmpty = computed(
    () => !this._loading() && !this._error() && this.filteredQuotes().length === 0,
  );

  // Guards against a slower, earlier request resolving after a newer one
  // and clobbering fresher state — see "Bug caught and fixed" in the README.
  private latestRequestId = 0;

  load(page = 1, size = 50): void {
    const requestId = ++this.latestRequestId;

    this._loading.set(true);
    this._error.set(null);

    this.quotesApi.getQuotes(page, size).subscribe({
      next: (data) => {
        if (requestId !== this.latestRequestId) {
          return; // a newer load() has since started; this response is stale
        }
        this._quotes.set(data);
        this._loading.set(false);
      },
      error: (err: AppError) => {
        if (requestId !== this.latestRequestId) {
          return;
        }
        this._error.set(err.message);
        this._loading.set(false);
      },
    });
  }

  selectAuthor(author: string): void {
    this._authorFilter.set(author);
  }
}
