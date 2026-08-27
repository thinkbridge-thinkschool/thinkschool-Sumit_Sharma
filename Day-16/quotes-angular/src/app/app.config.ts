import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withViewTransitions } from '@angular/router';
import { loadDevTokenFromLocalConfig } from './auth/load-dev-token';
import { API_INTERCEPTORS } from './http/api-interceptors';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors(API_INTERCEPTORS)),
    provideAppInitializer(loadDevTokenFromLocalConfig),
    provideRouter(routes, withViewTransitions())
  ]
};
