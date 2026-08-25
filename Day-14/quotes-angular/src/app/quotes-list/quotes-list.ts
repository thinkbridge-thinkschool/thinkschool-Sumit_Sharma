import { Component, computed, inject, signal } from '@angular/core';
import { QuotesApi } from '../services/quotes-api';
import { Quote } from '../models/quote.model';
import { QuoteDetail } from '../quote-detail/quote-detail';

@Component({
  selector: 'app-quotes-list',
  imports: [QuoteDetail],
  templateUrl: './quotes-list.html',
  styleUrl: './quotes-list.css',
})
export class QuotesList {
  private readonly quotesApi = inject(QuotesApi);

  readonly quotes = signal<Quote[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly selectedQuote = signal<Quote | null>(null);

  readonly isEmpty = computed(
    () => !this.loading() && !this.error() && this.quotes().length === 0,
  );

  readonly skeletonRows = [0, 1, 2, 3];

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.quotesApi.getQuotes(1, 50).subscribe({
      next: (data) => {
        this.quotes.set(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('GET /api/quotes failed', err);
        this.error.set('The Week-1 API did not respond. Please try again.');
        this.loading.set(false);
      },
    });
  }

  retry(): void {
    this.load();
  }

  selectQuote(quote: Quote): void {
    this.selectedQuote.set(quote);
  }

  backToList(): void {
    this.selectedQuote.set(null);
  }
}
