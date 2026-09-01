import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { DEV_TOKEN_STORAGE_KEY } from '../auth/dev-token';
import { API_INTERCEPTORS } from './api-interceptors';
import { ApiActivityService } from './api-activity.service';

/**
 * Exercises the REAL Day-15 interceptor chain — imported from api-interceptors.ts,
 * the same array app.config.ts registers, so this can't silently drift out of
 * sync with production wiring — and asserts the ApiActivityService (which the
 * "API Activity" panel renders from) reflects real request/response/error
 * events. No UI-only simulation.
 */
describe('activity interceptor (full real chain)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let activity: ApiActivityService;

  const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  // This sandbox's Node build exposes a broken native `localStorage` global
  // (missing removeItem) instead of jsdom's — stub a minimal in-memory one
  // so the real auth interceptor's localStorage.getItem() call has
  // something functional to read from, same as it would in a real browser.
  let store: Record<string, string>;

  beforeEach(() => {
    store = {};
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store[key] ?? null,
      setItem: (key: string, value: string) => {
        store[key] = value;
      },
      removeItem: (key: string) => {
        delete store[key];
      },
      clear: () => {
        store = {};
      },
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors(API_INTERCEPTORS)), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    activity = TestBed.inject(ApiActivityService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.unstubAllGlobals();
  });

  it('records a successful GET with credentials attached when a dev token exists', async () => {
    localStorage.setItem(DEV_TOKEN_STORAGE_KEY, 'fake-test-token');

    const result$ = firstValueFrom(http.get('/api/quotes', { params: { page: 1, size: 50 } }));
    const req = httpMock.expectOne((r) => r.url === '/api/quotes');
    expect(req.request.headers.get('Authorization')).toBe('Bearer fake-test-token');
    req.flush([{ id: 1, author: 'A', text: 'T', isDeleted: false }]);

    await result$;

    const entry = activity.recent()[0];
    expect(entry.method).toBe('GET');
    expect(entry.path).toBe('/api/quotes?page=1&size=50');
    expect(entry.state).toBe('success');
    expect(entry.status).toBe(200);
    expect(entry.authAttached).toBe(true);
  });

  it('records "No credentials" when no dev token is present', async () => {
    const result$ = firstValueFrom(http.get('/api/quotes'));
    httpMock.expectOne('/api/quotes').flush([]);
    await result$;

    expect(activity.recent()[0].authAttached).toBe(false);
  });

  it('maps a real ValidationProblemDetails 400 into the friendly message shown by the panel', async () => {
    localStorage.setItem(DEV_TOKEN_STORAGE_KEY, 'fake-test-token');

    const result$ = firstValueFrom(http.post('/api/quotes', { author: '', text: '' })).catch((e) => e);
    httpMock.expectOne('/api/quotes').flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { quote: ['Author must be between 1 and 200 characters.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    await result$;

    const entry = activity.recent()[0];
    expect(entry.state).toBe('error');
    expect(entry.status).toBe(400);
    expect(entry.message).toBe('Author must be between 1 and 200 characters.');
    // The raw ProblemDetails JSON must never leak into what the panel shows.
    expect(entry.message).not.toContain('traceId');
    expect(entry.message).not.toContain('{');
  });

  it('shows retrying for a transient GET failure, then success', async () => {
    const result$ = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    expect(activity.recent()[0].state).toBe('retrying');
    expect(activity.recent()[0].retryAttempt).toBe(1);

    await wait(300);
    httpMock.expectOne('/api/quotes').flush([{ id: 1, author: 'A', text: 'T', isDeleted: false }]);

    await result$;
    expect(activity.recent()[0].state).toBe('success');
    expect(activity.recent()[0].retryAttempt).toBe(1);
  }, 10000);

  it('never retries a POST, even on a transient failure', async () => {
    const result$ = firstValueFrom(http.post('/api/quotes', { author: 'A', text: 'T' })).catch((e) => e);

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await result$;

    const entry = activity.recent()[0];
    expect(entry.method).toBe('POST');
    expect(entry.state).toBe('error');
    expect(entry.retryAttempt).toBeUndefined();
    httpMock.verify();
  });
});
