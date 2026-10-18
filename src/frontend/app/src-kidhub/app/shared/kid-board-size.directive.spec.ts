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
});
