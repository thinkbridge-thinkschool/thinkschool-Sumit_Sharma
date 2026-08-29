import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { retry, throwError, timer } from 'rxjs';
import { ApiActivityService } from './api-activity.service';
import { API_ACTIVITY_ID } from './api-activity.token';

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 200;
const TRANSIENT_STATUSES = new Set([0, 502, 503, 504]);

function isTransient(error: unknown): boolean {
  return error instanceof HttpErrorResponse && TRANSIENT_STATUSES.has(error.status);
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  const activity = inject(ApiActivityService);
  const activityId = req.context.get(API_ACTIVITY_ID);

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryCount) => {
        if (!isTransient(error)) {
          return throwError(() => error);
        }

        if (activityId !== null) {
          activity.retrying(activityId, retryCount);
        }

        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    }),
  );
};
