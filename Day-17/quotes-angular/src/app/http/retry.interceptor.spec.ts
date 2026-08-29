import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { retryInterceptor } from './retry.interceptor';

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  it('retries a GET on a transient failure and succeeds once the backend recovers', async () => {
    const resultPromise = firstValueFrom(http.get('/api/quotes'));

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await wait(300); // first backoff: 200ms
    httpMock.expectOne('/api/quotes').flush([{ id: 1, author: 'A', text: 'T', isDeleted: false }]);

    const result = await resultPromise;
    expect(result).toEqual([{ id: 1, author: 'A', text: 'T', isDeleted: false }]);
  }, 10000);

  it('gives up after the small retry limit for a GET that keeps failing transiently', async () => {
    const resultPromise = firstValueFrom(http.get('/api/quotes')).catch((err) => err);

    // Initial attempt + MAX_RETRIES(2) retries = 3 total requests.
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await wait(300); // first backoff: 200ms
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await wait(500); // second backoff: 400ms
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    const result = await resultPromise;
    expect(result.status).toBe(503);
  }, 10000);

  it('does not retry a GET on a non-transient 4xx failure', async () => {
    const result$ = firstValueFrom(http.get('/api/quotes/999999'));
    const resultPromise = result$.catch((err) => err);

    httpMock.expectOne('/api/quotes/999999').flush(null, { status: 404, statusText: 'Not Found' });

    const result = await resultPromise;
    expect(result.status).toBe(404);
    httpMock.verify();
  });

  it('does not retry a POST, even on a transient failure', async () => {
    const result$ = firstValueFrom(http.post('/api/quotes', { author: 'A', text: 'T' }));
    const resultPromise = result$.catch((err) => err);

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    const result = await resultPromise;
    expect(result.status).toBe(503);
    httpMock.verify();
  });
});
