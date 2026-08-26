import { Component, input, output } from '@angular/core';
import { Quote } from '../models/quote.model';

@Component({
  selector: 'app-quote-detail',
  imports: [],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  readonly quote = input.required<Quote>();
  readonly back = output<void>();

  goBack(): void {
    this.back.emit();
  }

  initials(): string {
    return this.quote()
      .author.split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  }
}
