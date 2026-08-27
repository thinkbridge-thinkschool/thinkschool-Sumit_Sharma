import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { Quotes } from './quotes';

const QUOTE_A = { id: 1, author: 'Ada Lovelace', text: 'A.', isDeleted: false };
const QUOTE_B = { id: 2, author: 'Alan Turing', text: 'B.', isDeleted: false };

function setup() {
  TestBed.configureTestingModule({
    imports: [Quotes],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
      { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
    ],
  });

  const fixture = TestBed.createComponent(Quotes);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges(); // triggers the constructor's store.load()
  return { fixture, httpMock, component: fixture.componentInstance };
}

describe('Quotes (presentational, backed by QuotesStore)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('requests the real GET /api/quotes?page=1&size=50 on init and exposes the store’s data', () => {
    const { httpMock, component } = setup();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('50');
    req.flush([QUOTE_A, QUOTE_B]);

    expect(component.quotes()).toEqual([QUOTE_A, QUOTE_B]);
    expect(component.quoteCount()).toBe(2);
    expect(component.authorCount()).toBe(2);
    expect(component.loading()).toBe(false);
  });

  it('selectAuthor() delegates to the store and the filtered/derived signals update together', () => {
    const { httpMock, component } = setup();

    httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A, QUOTE_B]);

    component.selectAuthor('Ada Lovelace');

    expect(component.authorFilter()).toBe('Ada Lovelace');
    expect(component.filteredQuotes()).toEqual([QUOTE_A]);
    expect(component.quoteCount()).toBe(1);
    // authorCount is derived from the unfiltered set — filtering to one
    // author must not make it look like there's only one author overall.
    expect(component.authorCount()).toBe(2);
  });

  it('component holds no writable state of its own for quotes/loading/error — it re-exposes the store’s readonly signals', () => {
    const { httpMock, component } = setup();
    httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A]);

    expect((component.quotes as unknown as { set?: unknown }).set).toBeUndefined();
    expect((component.loading as unknown as { set?: unknown }).set).toBeUndefined();
    expect((component.error as unknown as { set?: unknown }).set).toBeUndefined();
  });
});
