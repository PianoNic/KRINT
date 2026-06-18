// Minimal typings for the parts of plotly.js-dist-min we use (the package ships no .d.ts).
declare module 'plotly.js-dist-min' {
  export interface PlotData {
    [key: string]: unknown;
  }
  export interface Layout {
    [key: string]: unknown;
  }
  export interface Config {
    [key: string]: unknown;
  }
  export function newPlot(
    root: HTMLElement,
    data: Array<Partial<PlotData>>,
    layout?: Partial<Layout>,
    config?: Partial<Config>,
  ): Promise<void>;
  export function react(
    root: HTMLElement,
    data: Array<Partial<PlotData>>,
    layout?: Partial<Layout>,
    config?: Partial<Config>,
  ): Promise<void>;
  export function purge(root: HTMLElement): void;
}
