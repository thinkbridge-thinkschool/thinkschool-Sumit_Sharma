import { Component, computed, effect, inject, signal } from '@angular/core';
import { QuotesApi } from '../services/quotes-api';
import { Quote } from '../models/quote.model';

@Component({
  selector: 'app-quotes',
  imports: [],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly quotesApi = inject(QuotesApi);

  readonly quotes = signal<Quote[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly authorFilter = signal('all');
  readonly lastEffectRun = signal<string | null>(null);

  readonly authors = computed(() => {
    const unique = new Set(this.quotes().map((q) => q.author));
    return ['all', ...Array.from(unique).sort()];
  });

  readonly filteredQuotes = computed(() => {
    const filter = this.authorFilter();
    return filter === 'all'
      ? this.quotes()
      : this.quotes().filter((q) => q.author === filter);
  });

  readonly quoteCount = computed(() => this.filteredQuotes().length);

  constructor() {
    effect(() => {
      const author = this.authorFilter();
      const count = this.quoteCount();
      const label =
        author === 'all'
          ? `Quotes (${count})`
          : `Quotes — ${author} (${count})`;

      document.title = label;
      console.log(`[effect] author filter changed to "${author}" — ${count} quote(s) visible`);
      this.lastEffectRun.set(label);
    });

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
      error: () => {
        this.error.set('Failed to load quotes from the Week-1 API.');
        this.loading.set(false);
      },
    });
  }

  selectAuthor(author: string): void {
    this.authorFilter.set(author);
  }

  readonly skeletonRows = [0, 1, 2, 3];

  initials(author: string): string {
    return author
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  }
}
