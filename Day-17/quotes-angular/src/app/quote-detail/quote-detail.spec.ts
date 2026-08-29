import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { QuoteDetail } from './quote-detail';
import { DEV_TOKEN_STORAGE_KEY } from '../auth/dev-token';

// This sandbox's Node build exposes a broken native `localStorage` global
// (missing removeItem) instead of jsdom's — stub a minimal in-memory one, same
// workaround as auth.guard.spec.ts, since AuthSession reads it on construction.
let localStore: Record<string, string>;

function stubLocalStorage() {
  localStore = {};
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => localStore[key] ?? null,
    setItem: (key: string, value: string) => {
      localStore[key] = value;
    },
    removeItem: (key: string) => {
      delete localStore[key];
    },
    clear: () => {
      localStore = {};
    },
  });
}

function setup(id: string, { authenticated = false }: { authenticated?: boolean } = {}) {
  stubLocalStorage();
  if (authenticated) {
    localStorage.setItem(DEV_TOKEN_STORAGE_KEY, 'fake-token-for-test');
  }

  const navigateByUrl = vi.fn().mockResolvedValue(true);

  TestBed.configureTestingModule({
    imports: [QuoteDetail],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(convertToParamMap({ id })) },
      },
      { provide: Router, useValue: { navigateByUrl } },
    ],
  });

  const fixture = TestBed.createComponent(QuoteDetail);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, httpMock, component: fixture.componentInstance, navigateByUrl };
}

describe('QuoteDetail', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('reads the real :id route param and requests GET /api/quotes/{id} (not a made-up endpoint)', () => {
    const { httpMock } = setup('5');

    const req = httpMock.expectOne('/api/quotes/5');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });
  });

  it('shows the fetched quote (author, text, id) once the request resolves', () => {
    const { httpMock, component } = setup('5');

    httpMock
      .expectOne('/api/quotes/5')
      .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });

    expect(component.loading()).toBe(false);
    expect(component.quote()).toEqual({
      id: 5,
      author: 'Ada Lovelace',
      text: 'A sample quote.',
      isDeleted: false,
    });
    expect(component.notFound()).toBe(false);
  });

  it('handles a missing quote id cleanly as "not found", not an unhandled error', () => {
    const { httpMock, component } = setup('999999');

    httpMock.expectOne('/api/quotes/999999').flush(null, { status: 404, statusText: 'Not Found' });

    expect(component.loading()).toBe(false);
    expect(component.notFound()).toBe(true);
    expect(component.error()).toBeNull();
  });

  it('handles a non-numeric id without ever calling the API', () => {
    const { httpMock, component } = setup('not-a-number');

    expect(httpMock.match('/api/quotes/NaN').length).toBe(0);
    expect(component.loading()).toBe(false);
    expect(component.notFound()).toBe(true);
  });

  it('shows a "deleted" state instead of the quote content when the API returns isDeleted: true', () => {
    const { httpMock, component } = setup('5');

    httpMock
      .expectOne('/api/quotes/5')
      .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: true });

    expect(component.quote()?.isDeleted).toBe(true);
  });

  describe('delete flow (real DELETE /api/quotes/{id} via QuotesStore)', () => {
    it('is not authenticated by default, so no delete action is available', () => {
      const { httpMock, component } = setup('5');
      httpMock
        .expectOne('/api/quotes/5')
        .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });

      expect(component.isAuthenticated()).toBe(false);
    });

    it('confirmDelete() calls the real endpoint, then navigates to /quotes on success', () => {
      const { httpMock, component, navigateByUrl } = setup('5', { authenticated: true });
      httpMock
        .expectOne('/api/quotes/5')
        .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });

      expect(component.isAuthenticated()).toBe(true);

      component.requestDelete();
      expect(component.confirmingDelete()).toBe(true);

      component.confirmDelete();
      const deleteReq = httpMock.expectOne('/api/quotes/5');
      expect(deleteReq.request.method).toBe('DELETE');
      deleteReq.flush(null, { status: 204, statusText: 'No Content' });

      expect(navigateByUrl).toHaveBeenCalledWith('/quotes');
    });

    it('shows a friendly error and stays on the page if the delete request fails', () => {
      const { httpMock, component, navigateByUrl } = setup('5', { authenticated: true });
      httpMock
        .expectOne('/api/quotes/5')
        .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });

      component.requestDelete();
      component.confirmDelete();
      httpMock
        .expectOne('/api/quotes/5')
        .flush(null, { status: 500, statusText: 'Server Error' });

      expect(component.deleting()).toBe(false);
      expect(component.deleteError()).toBeTruthy();
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('cancelDelete() clears the confirmation without calling the API again', () => {
      const { httpMock, component } = setup('5', { authenticated: true });
      httpMock
        .expectOne('/api/quotes/5')
        .flush({ id: 5, author: 'Ada Lovelace', text: 'A sample quote.', isDeleted: false });

      component.requestDelete();
      component.cancelDelete();

      expect(component.confirmingDelete()).toBe(false);
      httpMock.verify();
    });
  });
});
