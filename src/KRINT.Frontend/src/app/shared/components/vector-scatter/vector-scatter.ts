import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, viewChild } from '@angular/core';

export interface VectorPoint {
  id: string;
  vector: number[];
  payload: string;
}
export interface VectorCluster {
  dimensions: number;
  points: VectorPoint[];
}

interface Group {
  name: string;
  x: number[];
  y: number[];
  z: number[];
  ids: string[];
}

@Component({
  selector: 'app-vector-scatter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!data() || data()!.points.length === 0) {
      <p class="text-muted-foreground flex h-full items-center justify-center text-sm">
        No points to plot.
      </p>
    }
    <div #plot class="h-full w-full" [class.hidden]="!data() || data()!.points.length === 0"></div>
  `,
  host: { class: 'block h-full w-full' },
})
export class VectorScatter {
  /** The fetched cluster payload (id + vector + payload per point). */
  readonly data = input<VectorCluster | null>(null);
  /** Payload field to colour points by. When unset, the first scalar field is used. */
  readonly colorField = input<string | null>(null);

  private readonly plotEl = viewChild<ElementRef<HTMLElement>>('plot');

  constructor() {
    const destroyRef = inject(DestroyRef);
    effect(() => {
      const el = this.plotEl()?.nativeElement;
      const data = this.data();
      const field = this.colorField();
      if (!el || !data || data.points.length === 0) return;
      void this.render(el, data, field);
    });
    destroyRef.onDestroy(() => {
      const el = this.plotEl()?.nativeElement;
      if (el) void import('plotly.js-dist-min').then((Plotly) => Plotly.purge(el));
    });
  }

  private async render(el: HTMLElement, data: VectorCluster, field: string | null): Promise<void> {
    const groups = await this.buildGroups(data, field);
    if (groups.length === 0) return;
    const { react } = await import('plotly.js-dist-min');
    const traces = groups.map((g) => ({
      type: 'scatter3d',
      mode: 'markers',
      name: g.name,
      x: g.x,
      y: g.y,
      z: g.z,
      text: g.ids,
      hovertemplate: 'id %{text}<extra>' + g.name + '</extra>',
      marker: { size: 4, opacity: 0.85 },
    }));
    const layout = {
      margin: { l: 0, r: 0, t: 0, b: 0 },
      paper_bgcolor: 'rgba(0,0,0,0)',
      showlegend: groups.length > 1,
      legend: { font: { size: 11 } },
      scene: {
        xaxis: { title: { text: '' } },
        yaxis: { title: { text: '' } },
        zaxis: { title: { text: '' } },
      },
    };
    await react(el, traces, layout, { displaylogo: false, responsive: true });
  }

  /** Reduce vectors to 3D (UMAP for >3 dims; small sets fall back to the raw first dims) and
   *  bucket points into colour groups by the chosen / first scalar payload field. */
  private async buildGroups(data: VectorCluster, colorField: string | null): Promise<Group[]> {
    const vectors = data.points.map((p) => p.vector);
    const coords = await this.reduceTo3D(vectors, data.dimensions);

    const field = colorField ?? this.firstScalarField(data.points);
    const byName = new Map<string, Group>();
    data.points.forEach((p, i) => {
      const name = this.groupKey(p.payload, field);
      let g = byName.get(name);
      if (!g) {
        g = { name, x: [], y: [], z: [], ids: [] };
        byName.set(name, g);
      }
      const c = coords[i] ?? [0, 0, 0];
      g.x.push(c[0] ?? 0);
      g.y.push(c[1] ?? 0);
      g.z.push(c[2] ?? 0);
      g.ids.push(p.id);
    });
    return [...byName.values()];
  }

  private async reduceTo3D(vectors: number[][], dims: number): Promise<number[][]> {
    if (dims <= 3 || vectors.length < 5) {
      return vectors.map((v) => [v[0] ?? 0, v[1] ?? 0, v[2] ?? 0]);
    }
    const { UMAP } = await import('umap-js');
    const nNeighbors = Math.max(2, Math.min(15, vectors.length - 1));
    const umap = new UMAP({ nComponents: 3, nNeighbors });
    return umap.fit(vectors);
  }

  private firstScalarField(points: VectorPoint[]): string | null {
    for (const p of points) {
      const obj = this.parse(p.payload);
      for (const [k, v] of Object.entries(obj)) {
        if (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean') return k;
      }
    }
    return null;
  }

  private groupKey(payload: string, field: string | null): string {
    if (!field) return 'points';
    const v = this.parse(payload)[field];
    return v === undefined || v === null ? '(none)' : String(v);
  }

  private parse(payload: string): Record<string, unknown> {
    try {
      const o = JSON.parse(payload);
      return o && typeof o === 'object' ? (o as Record<string, unknown>) : {};
    } catch {
      return {};
    }
  }
}
