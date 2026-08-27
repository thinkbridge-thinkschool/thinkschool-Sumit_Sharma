import { inject, isDevMode } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, firstValueFrom, of } from 'rxjs';
import { DEV_TOKEN_STORAGE_KEY } from './dev-token';

interface DevTokenConfig {
  token: string;
}

export function loadDevTokenFromLocalConfig(): Promise<void> {
  if (!isDevMode()) {
    return Promise.resolve();
  }

  const http = inject(HttpClient);

  return firstValueFrom(
    http.get<DevTokenConfig>('/dev-config/dev-token.local.json').pipe(catchError(() => of(null))),
  ).then((config) => {
    if (config?.token) {
      localStorage.setItem(DEV_TOKEN_STORAGE_KEY, config.token);
    }
  });
}
