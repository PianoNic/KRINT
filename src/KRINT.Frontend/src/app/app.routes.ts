import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';
import { Databases } from './databases/databases';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Home },
      { path: 'databases', component: Databases },
    ],
  },
  { path: '**', redirectTo: '' },
];
