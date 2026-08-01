import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { passwordChangeGuard } from './core/password-change.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    // Public share viewer — deliberately no authGuard: the token in the URL is the authorization.
    path: 's/:token',
    loadComponent: () => import('./features/share/share-viewer').then((m) => m.ShareViewer),
  },
  {
    // Public account-claim page — like the share viewer, the token in the URL is the authorization.
    path: 'claim/:token',
    loadComponent: () => import('./features/invites/claim').then((m) => m.Claim),
  },
  {
    // The folder id is in the URL so a folder is linkable and the back button walks the tree.
    // No id = the owner's root, which has no row of its own server-side.
    path: 'files',
    canActivate: [authGuard, passwordChangeGuard],
    loadComponent: () => import('./features/files/files').then((m) => m.Files),
  },
  {
    path: 'files/:folderId',
    canActivate: [authGuard, passwordChangeGuard],
    loadComponent: () => import('./features/files/files').then((m) => m.Files),
  },
  {
    path: 'trash',
    canActivate: [authGuard, passwordChangeGuard],
    loadComponent: () => import('./features/trash/trash').then((m) => m.Trash),
  },
  {
    path: 'admin',
    canActivate: [authGuard, passwordChangeGuard, adminGuard],
    loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
  },
  {
    // Admin-only email-provider settings (#36). Same guards as the admin console.
    path: 'admin/email',
    canActivate: [authGuard, passwordChangeGuard, adminGuard],
    loadComponent: () => import('./features/admin/email-settings').then((m) => m.EmailSettings),
  },
  {
    // No passwordChangeGuard here — this is where a forced change is made, so it must stay reachable.
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
  },
  { path: '', pathMatch: 'full', redirectTo: 'files' },
  { path: '**', redirectTo: 'files' },
];
