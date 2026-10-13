import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WIDE_SCREEN_QUERY, wideScreen } from './wide-screen';

@Component({ standalone: true, template: '' })
class HostComponent {
  readonly changes: boolean[] = [];
  readonly wide = wideScreen(w => this.changes.push(w));
}

describe('wideScreen (Breitbild-Stufe ab 2400 px)', () => {
  let listeners: ((e: MediaQueryListEvent) => void)[];
  let matches: boolean;

  beforeEach(() => {
    listeners = [];
    matches = false;
    spyOn(window, 'matchMedia').and.callFake((q: string) => ({
      matches, media: q,
      addEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners.push(l),
      removeEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners = listeners.filter(x => x !== l),
    }) as unknown as MediaQueryList);
  });

  it('fragt die Stufe ab 2400 px ab und kippt mit dem Fenster', () => {
    const f = TestBed.createComponent(HostComponent);
    expect(window.matchMedia).toHaveBeenCalledWith(WIDE_SCREEN_QUERY);
    expect(WIDE_SCREEN_QUERY).toBe('(min-width: 2400px)');
    expect(f.componentInstance.wide()).toBeFalse();
    listeners.forEach(l => l({ matches: true } as MediaQueryListEvent));
    expect(f.componentInstance.wide()).toBeTrue();
    expect(f.componentInstance.changes).toEqual([true]);
    f.destroy();
    expect(listeners.length).toBe(0);
  });

  it('startet breit, wenn das Fenster schon breit ist', () => {
    matches = true;
    expect(TestBed.createComponent(HostComponent).componentInstance.wide()).toBeTrue();
  });
});
