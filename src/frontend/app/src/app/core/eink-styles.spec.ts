import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { setEinkActive } from './eink';
import { FlashcardBoardComponent } from '../features/courses/flashcards/flashcard-board.component';
import { contrast, parseColor } from '../testing/contrast';

/**
 * Globale E-Ink-Regeln (`src/_eink.scss`, am Ende von styles.scss geladen) — gemessen an berechneten Stilen mit der
 * Klasse `eink` an <html>, und als Regeltext in den geladenen Stylesheets.
 */
describe('_eink.scss (E-Ink-Modus)', () => {
  let wasDark: boolean;
  const cleanup: (() => void)[] = [];
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => {
    while (cleanup.length) cleanup.pop()!();
    setEinkActive(false);
    document.documentElement.classList.toggle('dark-theme', wasDark);
  });

  function mount(html: string): HTMLElement {
    const host = document.createElement('div');
    host.innerHTML = html;
    document.body.appendChild(host);
    cleanup.push(() => host.remove());
    return host;
  }

  function einkRuleText(): string {
    const out: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) if (r.cssText.includes('.eink')) out.push(r.cssText);
    }
    return out.join('\n');
  }

  it('Regeltext: Animationen, Übergänge und Smooth-Scroll für alle Elemente aus', () => {
    const css = einkRuleText();
    let all: CSSStyleRule | undefined;
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (r instanceof CSSStyleRule && /^html\.eink \*,/.test(r.selectorText)) all = r;
      }
    }
    expect(all).withContext('Regel für alle Elemente').toBeDefined();
    expect(all!.style.getPropertyValue('animation-name')).toBe('none');
    expect(all!.style.getPropertyPriority('animation-name')).toBe('important');
    expect(all!.style.getPropertyValue('transition-property')).toBe('none');
    expect(all!.style.getPropertyPriority('transition-property')).toBe('important');
    expect(all!.style.getPropertyValue('scroll-behavior')).toBe('auto');
    expect(css).toContain('.mat-ripple-element');
  });

  it('wirkt: Übergang und Animation eines Elements sind aus, nur im Modus', () => {
    const el = mount('<div style="transition: opacity 300ms; animation: rh-badge-pop 220ms"></div>').firstElementChild as HTMLElement;
    expect(getComputedStyle(el).transitionDuration).toBe('0.3s');
    setEinkActive(true);
    expect(getComputedStyle(el).transitionProperty).toBe('none');
    expect(getComputedStyle(el).animationName).toBe('none');
  });

  it('erzwungen hell: Seite weiß, Text schwarz, Sekundärtext ≥ 7:1, Rahmen-Token schwarz', () => {
    document.documentElement.classList.add('dark-theme');   // selbst wenn die Klasse noch hinge
    setEinkActive(true);
    const body = getComputedStyle(document.body);
    expect(parseColor(body.backgroundColor)).toEqual(parseColor('rgb(255, 255, 255)'));
    expect(parseColor(body.color)).toEqual(parseColor('rgb(0, 0, 0)'));
    const probe = mount('<span style="color: var(--mat-sys-on-surface-variant); border: 1px solid var(--mat-sys-outline)">x</span>')
      .firstElementChild as HTMLElement;
    const cs = getComputedStyle(probe);
    expect(contrast(parseColor(cs.color), parseColor('rgb(255, 255, 255)'))).toBeGreaterThanOrEqual(7);
    expect(parseColor(cs.borderTopColor)).toEqual(parseColor('rgb(0, 0, 0)'));
  });

  it('Brett: helle Felder weiß, dunkle grau; letzter Zug und Auswahl als Rahmen statt Fläche; Pfeile volle Deckkraft', () => {
    const host = mount(`<div class="board-theme-blue"><div class="cg-wrap orientation-white"><cg-container><cg-board>
      <square class="last-move" style="position:absolute;width:40px;height:40px"></square>
      <square class="selected" style="position:absolute;width:40px;height:40px"></square>
      </cg-board><svg class="cg-shapes"></svg></cg-container></div></div>`);
    setEinkActive(true);
    const board = getComputedStyle(host.querySelector('cg-board')!);
    expect(parseColor(board.backgroundColor)).toEqual(parseColor('rgb(255, 255, 255)'));
    expect(board.backgroundImage).toContain('a3a3a3');
    // Keine Feldkanten im Kachelmuster: sie wurden am Kachelrand halbiert (ungleich dicke Linien).
    expect(board.backgroundImage).not.toContain('stroke');
    const last = getComputedStyle(host.querySelector('square.last-move')!);
    expect(last.backgroundColor).toBe('rgba(0, 0, 0, 0)');
    expect(last.boxShadow).toContain('inset');
    expect(getComputedStyle(host.querySelector('square.selected')!).boxShadow).toContain('inset');
    expect(getComputedStyle(host.querySelector('.cg-shapes')!).opacity).toBe('1');
  });

  it('E-Book-Leser: Diagrammzonen ohne Rahmen, auch die aktive nicht (seit 0.802.1)', () => {
    const host = mount(`<div class="pv-page" style="position:relative">
      <button class="pv-zone" style="border:2px dashed rgb(103, 80, 164)"></button>
      <button class="pv-zone pv-zone--active"></button></div>`);
    setEinkActive(true);
    const zones = host.querySelectorAll('.pv-zone');
    expect(getComputedStyle(zones[0]).borderTopColor).toBe('rgba(0, 0, 0, 0)');
    expect(getComputedStyle(zones[1]).borderTopColor).toBe('rgba(0, 0, 0, 0)');
  });

  /** Komponentenregel nachstellen (gekapselte Regeln haben die Spezifität einer Klasse + Attribut). */
  function componentStyle(css: string): void {
    const st = document.createElement('style');
    st.textContent = css;
    document.head.appendChild(st);
    cleanup.push(() => st.remove());
  }

  it('d3-rk-gate-banner-schliessen-blass: Snackbar-Aktion schwarz, fett, unterstrichen statt blassem Hellblau', () => {
    componentStyle('.mat-mdc-snack-bar-action[x] { color: rgb(170, 199, 255); }');
    const host = mount(`<div class="mat-mdc-snack-bar-container"><div class="mdc-snackbar__surface">
      <div class="mat-mdc-snack-bar-label">Hinweis</div>
      <div class="mdc-snackbar__actions"><button x class="mat-mdc-button mat-mdc-snack-bar-action">Schließen</button></div>
      </div></div>`);
    const action = host.querySelector('button')!;
    expect(getComputedStyle(action).color).toBe('rgb(170, 199, 255)');
    setEinkActive(true);
    const cs = getComputedStyle(action);
    expect(cs.color).toBe('rgb(0, 0, 0)');
    expect(cs.fontWeight).toBe('700');
    expect(cs.textDecorationLine).toBe('underline');
    expect(getComputedStyle(document.documentElement).getPropertyValue('--mat-snack-bar-button-color').trim()).toBe('#000');
  });

  it('d3-rk-eink-wahlkachel-haarlinie: Wahlkacheln mit dickem schwarzem Rahmen auf Weiß, auch beim Überfahren', () => {
    componentStyle('.sm-choice[x] { border: 1px solid rgba(0, 0, 0, 0.18); background: rgba(0, 0, 0, 0.04); }');
    const tile = mount('<button x class="sm-choice">Trainingsmodus</button>').firstElementChild as HTMLElement;
    setEinkActive(true);
    const cs = getComputedStyle(tile);
    expect(cs.borderTopWidth).toBe('2px');
    expect(cs.borderTopColor).toBe('rgb(0, 0, 0)');
    expect(cs.backgroundColor).toBe('rgb(255, 255, 255)');
  });

  it('d3-rk-eink-sekundaertext-hellgrau: Zugrecht unter dem Puzzle-Brett in #333 statt doppelt abgeblasst', () => {
    componentStyle(`.board-hint[x] { color: color-mix(in srgb, currentColor 60%, transparent); }
      .board-hint-tomove[x] { color: color-mix(in srgb, currentColor 85%, transparent); }`);
    const host = mount('<div x class="board-hint"><span x class="board-hint-tomove">Weiß am Zug</span></div>');
    setEinkActive(true);
    const tomove = getComputedStyle(host.querySelector('.board-hint-tomove')!);
    expect(parseColor(tomove.color)).toEqual(parseColor('rgb(51, 51, 51)'));
    expect(contrast(parseColor(tomove.color), parseColor('rgb(255, 255, 255)'))).toBeGreaterThanOrEqual(7);
  });

  @Component({ standalone: true, imports: [FlashcardBoardComponent],
    template: '<app-flashcard-board fen="8/8/8/8/8/8/8/4K2k w - - 0 1" />' })
  class BoardHost {}

  it('d3-rk-eink-flashcards-brett-farbig: SVG-Brett (Lernkarte/Aufgabenblatt) weiß/grau statt Holzfarben, mit Rahmen', () => {
    // Über einen Wirt: TestBed rendert die Komponente sonst in ein <div>, die Regeln hängen am Element app-flashcard-board.
    const fixture = TestBed.configureTestingModule({ imports: [BoardHost] }).createComponent(BoardHost);
    document.body.appendChild(fixture.nativeElement);
    cleanup.push(() => { fixture.nativeElement.remove(); TestBed.resetTestingModule(); });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const light = el.querySelector('rect.fb-l') as SVGRectElement;
    const dark = el.querySelector('rect.fb-d') as SVGRectElement;
    expect(el.querySelectorAll('rect.fb-l').length).toBe(32);
    expect(el.querySelectorAll('rect.fb-d').length).toBe(32);
    expect(getComputedStyle(dark).fill).toBe('rgb(181, 136, 99)');   // ohne Modus: Holz (#b58863)
    setEinkActive(true);
    expect(getComputedStyle(light).fill).toBe('rgb(255, 255, 255)');
    expect(getComputedStyle(dark).fill).toBe('rgb(163, 163, 163)');
    expect(getComputedStyle(el.querySelector('svg')!).outlineWidth).toBe('2px');
  });

  @Component({ standalone: true, imports: [MatProgressSpinnerModule], template: '<mat-spinner diameter="40" />' })
  class SpinnerHost {}

  it('Ladekreis dreht nicht: die drehenden Bögen sind weg, ein stehender gepunkteter Ring bleibt', () => {
    const fixture = TestBed.configureTestingModule({ imports: [SpinnerHost] }).createComponent(SpinnerHost);
    document.body.appendChild(fixture.nativeElement);
    cleanup.push(() => { fixture.nativeElement.remove(); TestBed.resetTestingModule(); });
    fixture.detectChanges();
    setEinkActive(true);
    const spinner = (fixture.nativeElement as HTMLElement).querySelector('.mat-mdc-progress-spinner') as HTMLElement;
    const arcs = spinner.querySelector('.mdc-circular-progress__indeterminate-container') as HTMLElement;
    expect(getComputedStyle(arcs).display).toBe('none');
    expect(getComputedStyle(spinner, '::after').borderTopStyle).toBe('dotted');
  });
});
