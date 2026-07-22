import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    // The folder id is in the URL so a folder is linkable and the back button walks the tree.
    // No id = the owner's root, which has no row of its own server-side.
    path: 'files',
    canActivate: [authGuard],
    loadComponent: () => import('./features/files/files').then((m) => m.Files),
  },
  {
    path: 'files/:folderId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/files/files').then((m) => m.Files),
  },
  {
    path: 'trash',
    canActivate: [authGuard],
    loadComponent: () => import('./features/trash/trash').then((m) => m.Trash),
  },
  { path: '', pathMatch: 'full', redirectTo: 'files' },
  { path: '**', redirectTo: 'files' },
];
