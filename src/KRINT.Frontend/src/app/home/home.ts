import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ContentHeader } from '../shared/components/content-header/content-header';

@Component({
  selector: 'app-home',
  imports: [ContentHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="px-4 py-6">
      <h1 class="text-2xl font-semibold">KRINT</h1>
      <p class="text-muted-foreground mt-1 text-sm">
        Keyed · Replicated · Isolated · Networked · Transactional
      </p>
    </section>
  `,
})
export class Home {}
