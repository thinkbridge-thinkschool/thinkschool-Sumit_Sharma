import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Quote, QuoteRequest } from '../models/quote.model';

@Injectable({
  providedIn: 'root',
})
export class QuotesApi {
  private readonly http = inject(HttpClient);

  getQuotes(page: number, size: number): Observable<Quote[]> {
    return this.http.get<Quote[]>('/api/quotes', {
      params: { page, size },
    });
  }

  createQuote(request: QuoteRequest): Observable<Quote> {
    return this.http.post<Quote>('/api/quotes', request);
  }
}
