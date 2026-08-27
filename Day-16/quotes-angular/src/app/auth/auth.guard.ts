import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { getDevBearerToken } from './dev-token';

export const authGuard: CanActivateFn = () => {
  if (getDevBearerToken()) {
    return true;
  }

  const router = inject(Router);
  return router.createUrlTree(['/quotes'], { queryParams: { authRequired: 'true' } });
};
