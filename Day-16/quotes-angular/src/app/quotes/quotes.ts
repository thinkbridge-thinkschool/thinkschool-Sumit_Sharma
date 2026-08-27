import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { QuotesApi } from '../services/quotes-api';
import { Quote } from '../models/quote.model';
import { AppError } from '../http/app-error.model';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly quotesApi = inject(QuotesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly quotes = signal<Quote[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly authorFilter = signal('all');
  readonly lastEffectRun = signal<string | null>(null);

  private readonly queryParamMap = toSignal(this.route.queryParamMap, { requireSync: true });

  readonly authRequiredNotice = computed(() => this.queryParamMap().get('authRequired') === 'true');

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
      error: (err: AppError) => {
        this.error.set(err.message);
        this.loading.set(false);
      },
    });
  }

  selectAuthor(author: string): void {
    this.authorFilter.set(author);
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
