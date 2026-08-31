import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { DEV_TOKEN_STORAGE_KEY } from './dev-token';

describe('authGuard', () => {
  // This sandbox's Node build exposes a broken native `localStorage` global
  // (missing removeItem) instead of jsdom's — stub a minimal in-memory one,
  // same workaround as api-activity.integration.spec.ts.
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
      providers: [Router],
    });
  });

  it('allows activation when a dev bearer token is present', () => {
    localStorage.setItem(DEV_TOKEN_STORAGE_KEY, 'fake-token-for-test');

    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/create' } as never),
    );

    expect(result).toBe(true);
  });

  it('redirects to /login with the attempted URL preserved when no token is present', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/create' } as never),
    ) as UrlTree;

    expect(result).toBeInstanceOf(UrlTree);
    expect(result.toString()).toBe('/login?redirectTo=%2Fcreate');
  });
});
