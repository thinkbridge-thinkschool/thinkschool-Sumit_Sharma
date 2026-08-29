import { HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { ApiActivityService } from './api-activity.service';
import { API_ACTIVITY_ID } from './api-activity.token';
import { AppError } from './app-error.model';

function isAppError(value: unknown): value is AppError {
  return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
}

function pathOf(urlWithParams: string): string {
  const parsed = new URL(urlWithParams, 'https://internal.local');
  return parsed.pathname + parsed.search;
}

/**
 * Outermost interceptor: opens one activity-log entry per logical request
 * and closes it with whatever the rest of the real chain (auth -> error
 * mapping -> retry) actually produced. Purely observational — never alters
 * the request or the response/error that flows back to callers.
 */
export const activityInterceptor: HttpInterceptorFn = (req, next) => {
  const activity = inject(ApiActivityService);

  const id = activity.start(req.method, pathOf(req.urlWithParams));
  req.context.set(API_ACTIVITY_ID, id);

  return next(req).pipe(
    tap((event) => {
      if (event.type === HttpEventType.Response) {
        activity.succeed(id, event.status);
      }
    }),
    catchError((error: unknown) => {
      const appError = isAppError(error) ? error : { status: 0, message: 'Request failed.' };
      activity.fail(id, appError.status, appError.message);
      return throwError(() => error);
    }),
  );
};
