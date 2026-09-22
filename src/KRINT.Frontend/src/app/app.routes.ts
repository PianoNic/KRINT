import { Routes } from '@angular/router';
import { authGuard } from './shared/auth/auth-facade';
import { Login } from './login/login';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';
import { Databases } from './databases/databases';
import { Create } from './create/create';
import { Browser } from './browser/browser';
import { Backups } from './backups/backups';
import { Activity } from './activity/activity';
import { Console } from './console/console';
import { Nodes } from './nodes/nodes';
import { Settings } from './settings/settings';

export const routes: Routes = [
  // Local password login only; OIDC mode never routes here.
  { path: 'login', component: Login },
  {
    path: '',
    component: AppLayout,
    canActivateChild: [authGuard],
    children: [
      { path: '', component: Home },
      { path: 'create', component: Create },
      { path: 'instances', component: Databases },
      { path: 'browser', component: Browser },
      // Standalone /query was folded into /browser as a tab in the right pane.
      { path: 'query', redirectTo: 'browser' },
      { path: 'backups', component: Backups },
      { path: 'console', component: Console },
      { path: 'nodes', component: Nodes },
      { path: 'activity', component: Activity },
      { path: 'settings', component: Settings },
      // legacy alias
      { path: 'databases', redirectTo: 'instances' },
    ],
  },
  { path: '**', redirectTo: '' },
];
