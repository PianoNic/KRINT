import { computed, effect, Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'krint.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly systemDark = signal(this.readSystem());
  readonly mode = signal<ThemeMode>(this.readStored());
  readonly resolved = computed(() =>
    this.mode() === 'system' ? (this.systemDark() ? 'dark' : 'light') : this.mode(),
  );

  constructor() {
    window
      .matchMedia('(prefers-color-scheme: dark)')
      .addEventListener('change', (e) => this.systemDark.set(e.matches));

    effect(() => {
      document.documentElement.classList.toggle('dark', this.resolved() === 'dark');
    });

    effect(() => localStorage.setItem(STORAGE_KEY, this.mode()));
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
  }

  private readSystem(): boolean {
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  private readStored(): ThemeMode {
    const v = localStorage.getItem(STORAGE_KEY);
    return v === 'light' || v === 'dark' || v === 'system' ? v : 'system';
  }
}
