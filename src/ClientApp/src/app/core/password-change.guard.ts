import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ProfileStore } from './profile.store';

/**
 * Redirects an account that must change its password to the profile screen, where the only path
 * forward is setting a new one. Applied to the app's main routes (files/trash/admin) after
 * authGuard, and deliberately NOT to /profile itself, so the forced screen is reachable.
 *
 * The flag comes from an admin-set initial password or the bootstrap admin (§4.1). Cleared by a
 * successful change, after which this guard lets the account through. See
 * docs/feature-36-account-provisioning.md §7.3.
 */
export const passwordChangeGuard: CanActivateFn = async () => {
  const store = inject(ProfileStore);
  const router = inject(Router);

  await store.ensureLoaded();
  return store.mustChangePassword()
    ? router.createUrlTree(['/profile'], { queryParams: { changePassword: 1 } })
    : true;
};
