import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Async because auth state now comes from the server rather than from localStorage. On a fresh
 * load the answer is not known synchronously, and deciding before the probe resolves would bounce
 * a perfectly valid session to /login on every refresh.
 */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.ensureResolved();
  return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
