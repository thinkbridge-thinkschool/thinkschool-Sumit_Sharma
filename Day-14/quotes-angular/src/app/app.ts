import { Component, signal } from '@angular/core';
import { Quotes } from './quotes/quotes';
import { QuotesList } from './quotes-list/quotes-list';
import { CreateQuote } from './create-quote/create-quote';
import { CreateQuoteSignal } from './create-quote-signal/create-quote-signal';

@Component({
  selector: 'app-root',
  imports: [Quotes, QuotesList, CreateQuote, CreateQuoteSignal],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  readonly activeTab = signal<'browse' | 'explorer' | 'create' | 'create-signal'>('browse');

  setTab(tab: 'browse' | 'explorer' | 'create' | 'create-signal'): void {
    this.activeTab.set(tab);
  }
}
