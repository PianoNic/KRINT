import { Routes } from '@angular/router';
import { authGuard } from './shared/auth/auth-facade';
import { Login } from './login/login';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';

// Home is the landing page and stays in the initial bundle; every other page is loaded on
// first visit so heavy dependencies (xterm, CodeMirror, the 3D scatter) stay out of it.
export const routes: Routes = [
  // Local password login only; OIDC mode never routes here.
  { path: 'login', component: Login },
  {
    path: '',
    component: AppLayout,
    canActivateChild: [authGuard],
    children: [
      { path: '', component: Home },
      { path: 'create', loadComponent: () => import('./create/create').then((m) => m.Create) },
      { path: 'instances', loadComponent: () => import('./databases/databases').then((m) => m.Databases) },
      { path: 'browser', loadComponent: () => import('./browser/browser').then((m) => m.Browser) },
      // Standalone /query was folded into /browser as a tab in the right pane.
      { path: 'query', redirectTo: 'browser' },
      { path: 'backups', loadComponent: () => import('./backups/backups').then((m) => m.Backups) },
      { path: 'console', loadComponent: () => import('./console/console').then((m) => m.Console) },
      { path: 'nodes', loadComponent: () => import('./nodes/nodes').then((m) => m.Nodes) },
      { path: 'activity', loadComponent: () => import('./activity/activity').then((m) => m.Activity) },
      { path: 'settings', loadComponent: () => import('./settings/settings').then((m) => m.Settings) },
      // legacy alias
      { path: 'databases', redirectTo: 'instances' },
    ],
  },
  { path: '**', redirectTo: '' },
];
