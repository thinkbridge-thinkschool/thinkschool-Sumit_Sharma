import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from './quotes.store';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly store = inject(QuotesStore);

  // Purely presentational: this component reads the store's state and
  // derived signals, and forwards user actions back to the store. It owns
  // no feature state of its own.
  readonly quotes = this.store.quotes;
  readonly totalCount = this.store.totalCount;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly authorFilter = this.store.authorFilter;
  readonly authors = this.store.authors;
  readonly authorCount = this.store.authorCount;
  readonly filteredQuotes = this.store.filteredQuotes;
  readonly quoteCount = this.store.quoteCount;
  readonly isEmpty = this.store.isEmpty;

  readonly lastEffectRun = signal<string | null>(null);

  constructor() {
    effect(() => {
      const author = this.authorFilter();
      const count = this.quoteCount();
      const label =
        author === 'all'
          ? `Quotes (${count})`
          : `Quotes — ${author} (${count})`;

      document.title = label;
      this.lastEffectRun.set(label);
    });

    this.store.load();
  }

  load(): void {
    this.store.load();
  }

  selectAuthor(author: string): void {
    this.store.selectAuthor(author);
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
