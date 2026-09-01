import { TestBed } from '@angular/core/testing';
import { ApiActivityService } from './api-activity.service';

describe('ApiActivityService', () => {
  let service: ApiActivityService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ApiActivityService);
  });

  it('starts unknown and reports online after a successful request', () => {
    expect(service.connectionStatus()).toBe('unknown');

    const id = service.start('GET', '/api/quotes');
    expect(service.recent()[0]).toMatchObject({ id, method: 'GET', path: '/api/quotes', state: 'pending' });

    service.succeed(id, 200);
    expect(service.recent()[0]).toMatchObject({ state: 'success', status: 200 });
    expect(service.connectionStatus()).toBe('online');
  });

  it('reports offline only for a network-level failure (status 0)', () => {
    const id = service.start('GET', '/api/quotes');
    service.fail(id, 0, 'Could not reach the Quotes API.');

    expect(service.recent()[0]).toMatchObject({ state: 'error', status: 0 });
    expect(service.connectionStatus()).toBe('offline');
  });

  it('reports online for a real 4xx/5xx (the server responded)', () => {
    const id = service.start('POST', '/api/quotes');
    service.fail(id, 400, 'Author must be between 1 and 200 characters.');

    expect(service.connectionStatus()).toBe('online');
  });

  it('tracks retry attempts without changing the final success outcome', () => {
    const id = service.start('GET', '/api/quotes');
    service.retrying(id, 1);
    expect(service.recent()[0]).toMatchObject({ state: 'retrying', retryAttempt: 1 });

    service.retrying(id, 2);
    expect(service.recent()[0]).toMatchObject({ state: 'retrying', retryAttempt: 2 });

    service.succeed(id, 200);
    expect(service.recent()[0]).toMatchObject({ state: 'success', status: 200, retryAttempt: 2 });
  });

  it('records whether the auth interceptor attached credentials, never a token value', () => {
    const id = service.start('POST', '/api/quotes');
    service.setAuthAttached(id, true);

    const entry = service.recent()[0] as unknown as Record<string, unknown>;
    expect(entry['authAttached']).toBe(true);
    expect(JSON.stringify(entry)).not.toContain('Bearer');
    expect(Object.keys(entry)).not.toContain('token');
  });

  it('keeps the most recent entry first and caps history', () => {
    for (let i = 0; i < 25; i++) {
      const id = service.start('GET', `/api/quotes/${i}`);
      service.succeed(id, 200);
    }

    const recent = service.recent();
    expect(recent.length).toBeLessThanOrEqual(20);
    expect(recent[0].path).toBe('/api/quotes/24');
  });
});
