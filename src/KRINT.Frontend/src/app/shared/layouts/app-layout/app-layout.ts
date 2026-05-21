import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { Sidenav } from '../../../sidenav/sidenav';

@Component({
  selector: 'app-app-layout',
  imports: [RouterOutlet, HlmSidebarImports, Sidenav],
  templateUrl: './app-layout.html',
})
export class AppLayout {}
