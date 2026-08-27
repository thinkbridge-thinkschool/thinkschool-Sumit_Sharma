import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { QuoteDetail } from './quote-detail';

function setup(id: string) {
  TestBed.configureTestingModule({
    imports: [QuoteDetail],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(convertToParamMap({ id })) },
      },
    ],
  });

  const fixture = TestBed.createComponent(QuoteDetail);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, httpMock, component: fixture.componentInstance };
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
});
