import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuotesStore } from './quotes.store';

const QUOTE_A = { id: 1, author: 'Ada Lovelace', text: 'A.', isDeleted: false };
const QUOTE_B = { id: 2, author: 'Alan Turing', text: 'B.', isDeleted: false };
const QUOTE_C = { id: 3, author: 'Ada Lovelace', text: 'C.', isDeleted: false };

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  return {
    store: TestBed.inject(QuotesStore),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('QuotesStore', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('starts with empty state', () => {
    const { store } = setup();
    expect(store.quotes()).toEqual([]);
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
    expect(store.authorFilter()).toBe('all');
  });

  it('exposes only readonly signals — nothing outside the store can mutate state directly', () => {
    const { store } = setup();
    // Readonly signals from `.asReadonly()` have no `.set`/`.update`.
    expect((store.quotes as unknown as { set?: unknown }).set).toBeUndefined();
    expect((store.loading as unknown as { set?: unknown }).set).toBeUndefined();
    expect((store.error as unknown as { set?: unknown }).set).toBeUndefined();
    expect((store.authorFilter as unknown as { set?: unknown }).set).toBeUndefined();
  });

  it('load() sets loading, then populates quotes on success, using the real endpoint and fields', () => {
    const { store, httpMock } = setup();

    store.load(1, 50);
    expect(store.loading()).toBe(true);

    const req = httpMock.expectOne((r) => r.url === '/api/quotes');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('50');

    req.flush([QUOTE_A, QUOTE_B]);

    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
    expect(store.quotes()).toEqual([QUOTE_A, QUOTE_B]);
  });

  it('load() sets a friendly error message on failure and clears loading', () => {
    const { store, httpMock } = setup();

    store.load();
    httpMock.expectOne('/api/quotes?page=1&size=50').flush(null, { status: 500, statusText: 'Server Error' });

    expect(store.loading()).toBe(false);
    expect(store.error()).toBeTruthy();
  });

  it('a successful reload clears an error from a previous failed load', () => {
    const { store, httpMock } = setup();

    store.load();
    httpMock.expectOne('/api/quotes?page=1&size=50').flush(null, { status: 500, statusText: 'Server Error' });
    expect(store.error()).toBeTruthy();

    store.load();
    httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A]);

    expect(store.error()).toBeNull();
    expect(store.quotes()).toEqual([QUOTE_A]);
  });

  describe('derived state', () => {
    it('computes authors, authorCount, filteredQuotes, quoteCount, and isEmpty from quotes + authorFilter', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A, QUOTE_B, QUOTE_C]);

      expect(store.authors()).toEqual(['all', 'Ada Lovelace', 'Alan Turing']);
      expect(store.authorCount()).toBe(2);
      expect(store.quoteCount()).toBe(3);
      expect(store.isEmpty()).toBe(false);

      store.selectAuthor('Ada Lovelace');

      expect(store.filteredQuotes()).toEqual([QUOTE_A, QUOTE_C]);
      expect(store.quoteCount()).toBe(2);
      // authorCount is derived from the full quote set, not the filtered
      // one — selecting a single author must not change it.
      expect(store.authorCount()).toBe(2);
    });

    it('isEmpty reflects the filtered set, not just the raw quote list', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A]);

      store.selectAuthor('Someone Not In The List');

      expect(store.quotes().length).toBe(1);
      expect(store.filteredQuotes().length).toBe(0);
      expect(store.isEmpty()).toBe(true);
    });
  });

  describe('soft-deleted quotes (the real API keeps them in GET responses with isDeleted: true)', () => {
    const QUOTE_DELETED = { id: 4, author: 'Ada Lovelace', text: 'D.', isDeleted: true };

    it('excludes soft-deleted quotes from filteredQuotes, authors, authorCount, and totalCount', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock
        .expectOne('/api/quotes?page=1&size=50')
        .flush([QUOTE_A, QUOTE_B, QUOTE_DELETED]);

      // the deleted quote's author (Ada Lovelace) still has QUOTE_A visible,
      // so the author list itself is unaffected here — but the deleted
      // quote itself must never appear in what's shown or counted.
      expect(store.quotes()).toEqual([QUOTE_A, QUOTE_B, QUOTE_DELETED]); // raw API response, untouched
      expect(store.filteredQuotes()).toEqual([QUOTE_A, QUOTE_B]);
      expect(store.quoteCount()).toBe(2);
      // totalCount ("Quotes" stat) must match what's actually shown, not
      // the raw count including invisible deleted rows.
      expect(store.totalCount()).toBe(2);
    });

    it('excludes an author who has no remaining non-deleted quotes from the author dropdown', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_B, QUOTE_DELETED]);

      expect(store.authors()).toEqual(['all', 'Alan Turing']);
      expect(store.authorCount()).toBe(1);
    });

    it('deleteQuote() calls the real DELETE endpoint, then marks the quote deleted locally', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A, QUOTE_B]);
      expect(store.filteredQuotes()).toEqual([QUOTE_A, QUOTE_B]);

      let completed = false;
      store.deleteQuote(QUOTE_A.id).subscribe(() => (completed = true));

      const deleteReq = httpMock.expectOne('/api/quotes/1');
      expect(deleteReq.request.method).toBe('DELETE');
      deleteReq.flush(null, { status: 204, statusText: 'No Content' });

      expect(completed).toBe(true);
      expect(store.filteredQuotes()).toEqual([QUOTE_B]);
      expect(store.quotes()).toEqual([{ ...QUOTE_A, isDeleted: true }, QUOTE_B]);
    });

    it('deleteQuote() leaves local state untouched if the real request fails', () => {
      const { store, httpMock } = setup();

      store.load();
      httpMock.expectOne('/api/quotes?page=1&size=50').flush([QUOTE_A]);

      let errored = false;
      store.deleteQuote(QUOTE_A.id).subscribe({ error: () => (errored = true) });
      httpMock
        .expectOne('/api/quotes/1')
        .flush(null, { status: 403, statusText: 'Forbidden' });

      expect(errored).toBe(true);
      expect(store.filteredQuotes()).toEqual([QUOTE_A]);
    });
  });

  it('a stale in-flight request must not clobber a fresher one (concurrent refresh)', () => {
    const { store, httpMock } = setup();

    // First load starts...
    store.load();
    const firstReq = httpMock.expectOne('/api/quotes?page=1&size=50');

    // ...then a second refresh is triggered before the first resolves
    // (e.g. the user double-clicks "Try again", or a route revisit fires
    // load() again while the previous request is still in flight).
    store.load();
    const secondReq = httpMock.expectOne('/api/quotes?page=1&size=50');

    // The SECOND (newer) request resolves first...
    secondReq.flush([QUOTE_B]);
    // ...then the FIRST (now-stale) request resolves after it.
    firstReq.flush([QUOTE_A]);

    // The store must reflect the latest *request*, not whichever response
    // happened to arrive last on the wire.
    expect(store.quotes()).toEqual([QUOTE_B]);
    expect(store.loading()).toBe(false);
  });
});
