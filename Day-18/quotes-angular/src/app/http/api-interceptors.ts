import { HttpInterceptorFn } from '@angular/common/http';
import { authInterceptor } from '../auth/auth.interceptor';
import { activityInterceptor } from './activity.interceptor';
import { errorMappingInterceptor } from './error-mapping.interceptor';
import { retryInterceptor } from './retry.interceptor';

/**
 * Single source of truth for interceptor order, shared by app.config.ts and
 * by tests that need to exercise the real chain end to end. Order matters:
 *
 *  1. activityInterceptor — outermost, so it observes the FINAL outcome
 *     after auth/error-mapping/retry have all run.
 *  2. authInterceptor     — attaches the header before anything else sees
 *     the request.
 *  3. errorMappingInterceptor — maps whatever error retryInterceptor gives
 *     up on into a typed AppError.
 *  4. retryInterceptor    — innermost/closest to the backend, so a retry
 *     re-issues the already-authenticated request without re-running the
 *     outer interceptors.
 */
export const API_INTERCEPTORS: HttpInterceptorFn[] = [
  activityInterceptor,
  authInterceptor,
  errorMappingInterceptor,
  retryInterceptor,
];
