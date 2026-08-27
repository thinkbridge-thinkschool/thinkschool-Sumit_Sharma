import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { ApiActivityService } from '../http/api-activity.service';
import { API_ACTIVITY_ID } from '../http/api-activity.token';
import { getDevBearerToken } from './dev-token';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = getDevBearerToken();
  const isApiRequest = req.url.startsWith('/api/');
  const willAttach = !!token && isApiRequest;

  const activity = inject(ApiActivityService);
  const activityId = req.context.get(API_ACTIVITY_ID);
  if (activityId !== null) {
    // Report only that credentials were attached — never the token itself.
    activity.setAuthAttached(activityId, willAttach);
  }

  if (!willAttach) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
