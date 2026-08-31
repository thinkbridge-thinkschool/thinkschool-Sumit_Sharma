import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthSession } from './auth-session';
import { DEV_TOKEN_STORAGE_KEY } from './dev-token';

// This sandbox's Node build exposes a broken native `localStorage` global
// (missing removeItem) instead of jsdom's — stub a minimal in-memory one,
// same workaround as auth.guard.spec.ts.
let store: Record<string, string>;

function stubLocalStorage() {
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
}

function setup() {
  stubLocalStorage();
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  return {
    authSession: TestBed.inject(AuthSession),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('AuthSession (soft sign-in/out, real dev-token credential)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('starts signed out when there is no existing token in storage', () => {
    const { authSession } = setup();
    expect(authSession.isAuthenticated()).toBe(false);
    expect(authSession.displayName()).toBeNull();
  });

  it('restores an existing session from a previous sign-in (token already in localStorage)', () => {
    store = { [DEV_TOKEN_STORAGE_KEY]: 'already-signed-in-token' };
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store[key] ?? null,
      setItem: (key: string, value: string) => {
        store[key] = value;
      },
      removeItem: (key: string) => {
        delete store[key];
      },
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const authSession = TestBed.inject(AuthSession);

    expect(authSession.isAuthenticated()).toBe(true);
  });

  it('signIn() fetches the shared dev-token credential and stores the display name, without checking a password', () => {
    const { authSession, httpMock } = setup();

    let result: boolean | undefined;
    authSession.signIn('Ada').subscribe((success) => (result = success));

    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });

    expect(result).toBe(true);
    expect(authSession.isAuthenticated()).toBe(true);
    expect(authSession.displayName()).toBe('Ada');
    expect(localStorage.getItem(DEV_TOKEN_STORAGE_KEY)).toBe('real-dev-token');
  });

  it('signIn() surfaces a friendly error and stays signed out if the credential is unavailable', () => {
    const { authSession, httpMock } = setup();

    let result: boolean | undefined;
    authSession.signIn('Ada').subscribe((success) => (result = success));

    httpMock.expectOne('/dev-config/dev-token.local.json').flush(null, {
      status: 404,
      statusText: 'Not Found',
    });

    expect(result).toBe(false);
    expect(authSession.isAuthenticated()).toBe(false);
    expect(authSession.error()).toBeTruthy();
  });

  it('signOut() clears the session and the stored credential', () => {
    const { authSession, httpMock } = setup();

    authSession.signIn('Ada').subscribe();
    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });
    expect(authSession.isAuthenticated()).toBe(true);

    authSession.signOut();

    expect(authSession.isAuthenticated()).toBe(false);
    expect(authSession.displayName()).toBeNull();
    expect(localStorage.getItem(DEV_TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('a second signIn() in the same session reuses the already-fetched credential instead of re-requesting it', () => {
    const { authSession, httpMock } = setup();

    authSession.signIn('Ada').subscribe();
    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });

    // e.g. re-visiting /login while already signed in under a different name
    authSession.signIn('Alan').subscribe();

    httpMock.expectNone('/dev-config/dev-token.local.json');
    expect(authSession.isAuthenticated()).toBe(true);
    expect(authSession.displayName()).toBe('Alan');
  });

  it('signOut() clears the cached credential too, so the next signIn() re-fetches it', () => {
    const { authSession, httpMock } = setup();

    authSession.signIn('Ada').subscribe();
    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });

    authSession.signOut();
    authSession.signIn('Alan').subscribe();

    httpMock.expectOne('/dev-config/dev-token.local.json').flush({ token: 'real-dev-token' });
    expect(authSession.isAuthenticated()).toBe(true);
    expect(authSession.displayName()).toBe('Alan');
  });
});
