import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { QuotesStore } from './quotes.store';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly store = inject(QuotesStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  // Purely presentational: this component reads the store's state and
  // derived signals, and forwards user actions back to the store. It owns
  // no feature state of its own.
  readonly quotes = this.store.quotes;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly authorFilter = this.store.authorFilter;
  readonly authors = this.store.authors;
  readonly authorCount = this.store.authorCount;
  readonly filteredQuotes = this.store.filteredQuotes;
  readonly quoteCount = this.store.quoteCount;
  readonly isEmpty = this.store.isEmpty;

  readonly lastEffectRun = signal<string | null>(null);

  private readonly queryParamMap = toSignal(this.route.queryParamMap, { requireSync: true });

  readonly authRequiredNotice = computed(() => this.queryParamMap().get('authRequired') === 'true');

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

  dismissAuthNotice(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { authRequired: null },
      queryParamsHandling: 'merge',
    });
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
