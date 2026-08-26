import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { loadDevTokenFromLocalConfig } from './auth/load-dev-token';
import { API_INTERCEPTORS } from './http/api-interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors(API_INTERCEPTORS)),
    provideAppInitializer(loadDevTokenFromLocalConfig)
  ]
};
