import { ChangeDetectionStrategy, Component } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { simpleBuymeacoffee, simpleGithub } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

@Component({
  selector: 'app-content-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButtonImports, HlmSidebarImports, HlmTooltipImports],
  providers: [provideIcons({ simpleGithub, simpleBuymeacoffee })],
  templateUrl: './content-header.html',
})
export class ContentHeader {}
