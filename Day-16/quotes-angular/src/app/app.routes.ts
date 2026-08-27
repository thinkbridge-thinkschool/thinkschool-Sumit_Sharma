import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { Quotes } from './quotes/quotes';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },
  { path: 'quotes', component: Quotes, title: 'Quotes' },
  {
    path: 'quotes/:id',
    loadComponent: () => import('./quote-detail/quote-detail').then((m) => m.QuoteDetail),
    title: 'Quote detail',
  },
  {
    path: 'create',
    loadComponent: () => import('./create-quote/create-quote').then((m) => m.CreateQuote),
    canActivate: [authGuard],
    title: 'Create a quote',
  },
  { path: '**', redirectTo: 'quotes' },
];
