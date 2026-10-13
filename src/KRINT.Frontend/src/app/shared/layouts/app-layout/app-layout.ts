import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { Sidenav } from '../../../sidenav/sidenav';
import { AppInfoService } from '../../services/app-info.service';

@Component({
  selector: 'app-app-layout',
  imports: [RouterOutlet, HlmSidebarImports, Sidenav],
  templateUrl: './app-layout.html',
})
export class AppLayout {
  protected readonly appInfo = inject(AppInfoService);
}
