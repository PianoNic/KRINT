import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

@Component({
  selector: 'app-content-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSidebarImports],
  templateUrl: './content-header.html',
})
export class ContentHeader {}
