import { CUSTOM_ELEMENTS_SCHEMA, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { KidBoardSizeDirective } from './kid-board-size.directive';

/** Nachbau: ein „Brett" mit `cg-board` darin, dessen Größe der Test umschaltet. */
@Component({
  standalone: true,
  imports: [KidBoardSizeDirective],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: `<div kidBoardSize #board="kidBoardSize" style="position: relative" [style.width.px]="size()" [style.height.px]="size()">
    <cg-board style="display: block; width: 100%; height: 100%"></cg-board></div>
    <p class="out">{{ board.measured() }}</p>`,
})
class HostComponent {
  readonly size = signal(0);
}

/** Wartet, bis der ResizeObserver geliefert hat (ein Bild + ein Makro-Task). */
async function frame(): Promise<void> {
  await new Promise<void>(r => requestAnimationFrame(() => r()));
  await new Promise(r => setTimeout(r));
}

// Konsolenfehler „<line> attribute x1: Expected length, NaN" / „translate(NaN,NaN)" (Sternenjagd, Level-Tipp): Markierungen
// gingen an ein noch nicht vermessenes Brett. kidBoardSize meldet erst ein gemessenes Brett.
describe('KidBoardSizeDirective', () => {
  it('ungemessen (0 × 0) → false; sobald das Brett eine Größe hat → true; wieder 0 → false', async () => {
    const f = TestBed.createComponent(HostComponent);
    document.body.appendChild(f.nativeElement);
    try {
      f.detectChanges();
      await frame();
      f.detectChanges();
      const out = () => (f.nativeElement as HTMLElement).querySelector('.out')!.textContent!.trim();
      expect(out()).toBe('false');

      f.componentInstance.size.set(320);
      f.detectChanges();
      await frame();
      f.detectChanges();
      expect(out()).toBe('true');

      f.componentInstance.size.set(0);
      f.detectChanges();
      await frame();
      f.detectChanges();
      expect(out()).toBe('false');
    } finally {
      f.nativeElement.remove();
    }
  });

  // UI-Sweep 28.10.: chessground ersetzt cg-board bei redrawAll; der Host behält seine Größe. Vorher beobachtete die
  // Direktive weiter das abgehängte alte Brett, `measured()` blieb false und die Sterne kamen nie.
  it('misst neu, wenn cg-board ersetzt wird, auch ohne Größenänderung des Hosts', async () => {
    const f = TestBed.createComponent(HostComponent);
    document.body.appendChild(f.nativeElement);
    try {
      f.componentInstance.size.set(320);
      f.detectChanges();
      const host = (f.nativeElement as HTMLElement).querySelector('[kidboardsize]') as HTMLElement;
      const out = () => (f.nativeElement as HTMLElement).querySelector('.out')!.textContent!.trim();
      // Altes Brett noch ohne Größe (wie nach dem Schrumpfen auf < 8 px), Host aber schon groß.
      (host.querySelector('cg-board') as HTMLElement).style.cssText = 'display: block; width: 0; height: 0';
      await frame();
      f.detectChanges();
      expect(out()).toBe('false');

      // redrawAll: neues cg-board in voller Größe, der Host bleibt gleich groß.
      host.querySelector('cg-board')!.remove();
      const fresh = document.createElement('cg-board');
      fresh.style.cssText = 'display: block; width: 100%; height: 100%';
      host.appendChild(fresh);
      await frame();
      f.detectChanges();
      expect(out()).toBe('true');
    } finally {
      f.nativeElement.remove();
    }
  });
});
