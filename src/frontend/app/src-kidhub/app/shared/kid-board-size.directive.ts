import { DestroyRef, Directive, ElementRef, afterNextRender, inject, signal } from '@angular/core';

/**
 * Misst das Brett einer Kinderseite (`<app-puzzle-board kidBoardSize #board="kidBoardSize">`): `measured()` ist erst
 * wahr, wenn chessgrounds `cg-board` eine Breite und Höhe hat.
 *
 * <p>Warum: Sternenjagd (Sterne ab dem ersten Bild) und Level-Tipp setzen Markierungen, bevor das Brett vermessen ist —
 * chessground rechnet dann mit 0 × 0 (`0 / 0`) und zeichnet `<line x1="NaN">` bzw. `translate(NaN,NaN)` (Konsolenfehler
 * „Expected length, NaN"). RookHubs Bretter halten Pfeile dafür zurück (`applyArrows`/`arrowsPending` in chess-board und
 * analysis-board); KidHub tut dasselbe an seinem Brett: Markierungen gehen erst an das Brett, wenn es gemessen ist, und
 * verschwinden, solange es keine Größe hat. chessgrounds eigener ResizeObserver (früher angelegt, also früher gerufen)
 * hat die gemerkten Maße bis dahin schon verworfen.</p>
 */
@Directive({ selector: '[kidBoardSize]', standalone: true, exportAs: 'kidBoardSize' })
export class KidBoardSizeDirective {
  readonly measured = signal(false);

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    let observer: ResizeObserver | undefined;
    let mutations: MutationObserver | undefined;
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') { this.measured.set(true); return; }
      // chessground baut `cg-board` bei jedem redrawAll neu — das jeweils aktuelle beobachten, dazu immer den Host.
      let watched: Element | null = null;
      const check = () => {
        const board = host.querySelector<HTMLElement>('cg-board');
        if (board && board !== watched) {
          if (watched) observer!.unobserve(watched);
          observer!.observe(board);
          watched = board;
        }
        const r = (board ?? host).getBoundingClientRect();
        this.measured.set(r.width > 0 && r.height > 0);
      };
      observer = new ResizeObserver(check);
      observer.observe(host);
      // redrawAll ersetzt `cg-board`, oft ohne dass sich der Host in der Größe ändert: Der ResizeObserver hing dann am
      // abgehängten alten Brett, `measured()` blieb stehen und die Sterne kamen nie (UI-Sweep 28.10., Sternenjagd).
      // Darum nach jedem Neubau sofort neu messen.
      if (typeof MutationObserver !== 'undefined') {
        mutations = new MutationObserver(() => { if (host.querySelector('cg-board') !== watched) check(); });
        mutations.observe(host, { childList: true, subtree: true });
      }
      check();
    });
    inject(DestroyRef).onDestroy(() => { observer?.disconnect(); mutations?.disconnect(); });
  }
}
