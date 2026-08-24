import { Component, signal } from '@angular/core';
import { Quotes } from './quotes/quotes';
import { QuotesList } from './quotes-list/quotes-list';

@Component({
  selector: 'app-root',
  imports: [Quotes, QuotesList],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  readonly activeTab = signal<'browse' | 'explorer'>('browse');

  setTab(tab: 'browse' | 'explorer'): void {
    this.activeTab.set(tab);
  }
}
