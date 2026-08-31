import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { DEV_TOKEN_STORAGE_KEY, getDevBearerToken } from './dev-token';

const DISPLAY_NAME_STORAGE_KEY = 'QUOTES_APP_DISPLAY_NAME';

interface DevTokenConfig {
  token: string;
}

/**
 * A "soft" sign-in/out session: this app has no real user credential system
 * wired up (see README - the real POST /api/auth/login exists on the API
 * but its tokens carry no `scope` claim, so they can never satisfy the
 * CanEditQuotes policy the real backend enforces). Signing in establishes a
 * local-only session under a display name and reuses the same shared
 * dev-token bearer credential the guard/interceptor already used - it does
 * not verify a password against the server, and every "signed in" user
 * shares one credential rather than having a distinct identity.
 */
@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly http = inject(HttpClient);

  private readonly _displayName = signal<string | null>(
    localStorage.getItem(DISPLAY_NAME_STORAGE_KEY),
  );
  private readonly _isAuthenticated = signal(!!getDevBearerToken());
  private readonly _signingIn = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly displayName = this._displayName.asReadonly();
  readonly isAuthenticated = this._isAuthenticated.asReadonly();
  readonly signingIn = this._signingIn.asReadonly();
  readonly error = this._error.asReadonly();

  private cachedToken: string | null = null;

  signIn(displayName: string): Observable<boolean> {
    this._signingIn.set(true);
    this._error.set(null);

    return this.resolveToken().pipe(
      tap((token) => {
        this._signingIn.set(false);

        if (!token) {
          this._error.set('Sign-in is unavailable right now. Please try again.');
          return;
        }

        localStorage.setItem(DEV_TOKEN_STORAGE_KEY, token);
        localStorage.setItem(DISPLAY_NAME_STORAGE_KEY, displayName);
        this._displayName.set(displayName);
        this._isAuthenticated.set(true);
      }),
      map((token) => !!token),
    );
  }

  signOut(): void {
    localStorage.removeItem(DEV_TOKEN_STORAGE_KEY);
    localStorage.removeItem(DISPLAY_NAME_STORAGE_KEY);
    this.cachedToken = null;
    this._displayName.set(null);
    this._isAuthenticated.set(false);
  }

  private resolveToken(): Observable<string | null> {
    if (this.cachedToken) {
      return of(this.cachedToken);
    }

    return this.http.get<DevTokenConfig>('/dev-config/dev-token.local.json').pipe(
      map((config) => {
        this.cachedToken = config.token;
        return config.token;
      }),
      catchError(() => of(null)),
    );
  }
}
