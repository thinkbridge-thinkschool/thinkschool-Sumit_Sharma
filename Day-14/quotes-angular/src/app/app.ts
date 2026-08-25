import { Component, signal } from '@angular/core';
import { Quotes } from './quotes/quotes';
import { QuotesList } from './quotes-list/quotes-list';
import { CreateQuote } from './create-quote/create-quote';

@Component({
  selector: 'app-root',
  imports: [Quotes, QuotesList, CreateQuote],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  readonly activeTab = signal<'browse' | 'explorer' | 'create'>('browse');

  setTab(tab: 'browse' | 'explorer' | 'create'): void {
    this.activeTab.set(tab);
  }
}
