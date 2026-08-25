import { HttpInterceptorFn } from '@angular/common/http';
import { getDevBearerToken } from './dev-token';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = getDevBearerToken();

  if (!token || !req.url.startsWith('/api/')) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
