import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Restricts a route to admins. Awaits the session probe (like authGuard) so the role is known
 * before deciding, then sends non-admins to their files rather than a dead page.
 *
 * This is a UX gate only: the server enforces the "Admin" policy on every /api/admin call, so a
 * non-admin who forced the route would just get 403s and see nothing.
 */
export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.ensureResolved();
  return auth.isAdmin() ? true : router.createUrlTree(['/files']);
};
