import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Home },
    ],
  },
  { path: '**', redirectTo: '' },
];
