import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { ContentHeader } from '../shared/components/content-header/content-header';

@Component({
  selector: 'app-home',
  imports: [ContentHeader, HlmButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="px-4 py-6 space-y-4">
      <h1 class="text-2xl font-semibold">KRINT</h1>
      <p class="text-muted-foreground text-sm">
        Keyed · Replicated · Isolated · Networked · Transactional
      </p>
      <button hlmBtn type="button">Test button</button>
    </section>
  `,
})
export class Home {}
