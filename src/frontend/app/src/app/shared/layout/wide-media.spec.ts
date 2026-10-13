import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WIDE_MEDIA_QUERY, mediaSignal } from './wide-media';

@Component({ standalone: true, template: '' })
class HostComponent {
  readonly wide = mediaSignal();
}

describe('mediaSignal (u1, Breitbild-Stufe)', () => {
  let listeners: ((e: MediaQueryListEvent) => void)[];
  let removed: number;

  function fakeMatchMedia(matches: boolean): jasmine.Spy {
    listeners = []; removed = 0;
    return spyOn(window, 'matchMedia').and.callFake(() => ({
      matches,
      addEventListener: (_: string, l: any) => listeners.push(l),
      removeEventListener: () => removed++,
    }) as any);
  }

  it('fragt die Breitbild-Grenze 2400 px ab und folgt Größenänderungen', () => {
    const spy = fakeMatchMedia(false);
    const fixture = TestBed.createComponent(HostComponent);
    expect(spy).toHaveBeenCalledWith(WIDE_MEDIA_QUERY);
    expect(WIDE_MEDIA_QUERY).toBe('(min-width: 2400px)');
    expect(fixture.componentInstance.wide()).toBeFalse();

    listeners.forEach(l => l({ matches: true } as MediaQueryListEvent));
    expect(fixture.componentInstance.wide()).toBeTrue();

    fixture.destroy();
    expect(removed).toBe(1);
  });

  it('ohne Injektionskontext: nur der Startwert, kein Listener', () => {
    fakeMatchMedia(true);
    expect(mediaSignal()()).toBeTrue();
    expect(listeners.length).toBe(0);
  });

  it('startet true, wenn das Fenster schon breit ist', () => {
    fakeMatchMedia(true);
    expect(TestBed.createComponent(HostComponent).componentInstance.wide()).toBeTrue();
  });
});
