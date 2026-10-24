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

  it('d5-rk-analyse-pfeile-nur-farbe: Linienmarke schwarz umrandet mit schwarzer Ziffer, Pfeil-Nummern schwarz', () => {
    const host = mount('<span class="line-mark" style="background: rgb(255, 152, 0); color: #fff">1</span>');
    setEinkActive(true);
    const mark = host.querySelector('.line-mark') as HTMLElement;
    expect(getComputedStyle(mark).backgroundColor).toBe('rgb(255, 255, 255)');
    expect(getComputedStyle(mark).color).toBe('rgb(0, 0, 0)');
    expect(getComputedStyle(mark).boxShadow).toContain('rgb(0, 0, 0)');
    expect(einkRuleText()).toMatch(/\.cg-custom-svgs circle\[stroke="white"\] \{ fill: rgb\(0, 0, 0\) !important; fill-opacity: 1 !important; \}/);
  });

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

  it('d4-rk-segment-innenrahmen-abgeschnitten: End-Segmente einer Umschaltergruppe übernehmen deren Rundung', () => {
    const group = mount('<div class="mat-button-toggle-group" style="border-radius: 20px; overflow: hidden; display: flex">'
      + '<div class="mat-button-toggle">A</div><div class="mat-button-toggle">B</div>'
      + '<div class="mat-button-toggle mat-button-toggle-checked">C</div></div>').firstElementChild as HTMLElement;
    const [first, mid, last] = Array.from(group.children) as HTMLElement[];
    expect(getComputedStyle(last).borderTopRightRadius).toBe('0px');   // ohne Modus: eckig wie bisher
    setEinkActive(true);
    expect(getComputedStyle(first).borderTopLeftRadius).toBe('20px');
    expect(getComputedStyle(first).borderBottomLeftRadius).toBe('20px');
    expect(getComputedStyle(first).borderTopRightRadius).toBe('0px');
    expect(getComputedStyle(mid).borderTopLeftRadius).toBe('0px');
    expect(getComputedStyle(last).borderTopRightRadius).toBe('20px');
    expect(getComputedStyle(last).borderBottomRightRadius).toBe('20px');
    expect(getComputedStyle(last).boxShadow).toContain('inset');
  });

  it('d4-rk-progress-bulb-light-red: „mit Tipp“-Farbe im Modus dunkelgrau (Bedeutung trägt das Symbol)', () => {
    setEinkActive(true);
    expect(getComputedStyle(document.documentElement).getPropertyValue('--rh-hint').trim()).toBe('#4d4d4d');
  });

  it('d5-rk-eink-fokus-doppelrahmen: im Formularfeld ist der Feldrahmen der eine Fokusrahmen (3 px schwarz), kein Innenrahmen', () => {
    const css = einkRuleText();
    expect(css).toMatch(/html\.eink \.mat-mdc-form-field :is\(input, textarea, select, \.mat-mdc-select\):focus-visible \{ outline: none !important; \}/);
    setEinkActive(true);
    const root = getComputedStyle(document.documentElement);
    expect(root.getPropertyValue('--mat-form-field-outlined-focus-outline-width').trim()).toBe('3px');
    expect(root.getPropertyValue('--mat-form-field-outlined-focus-outline-color').trim()).toBe('#000');
    expect(root.getPropertyValue('--mat-form-field-outlined-focus-label-text-color').trim()).toBe('#000');
  });

  it('d5-rk-eink-fokus-doppelrahmen: fokussiertes Eingabefeld im Formularfeld ohne Umriss, Knopf daneben behält ihn', () => {
    const host = mount('<div class="mat-mdc-form-field"><input id="eink-ff-in"><button id="eink-ff-btn">x</button></div>'
      + '<input id="eink-plain-in">');
    setEinkActive(true);
    const inField = host.querySelector('#eink-ff-in') as HTMLInputElement;
    inField.focus();   // Textfelder sind bei Fokus immer :focus-visible
    expect(inField.matches(':focus-visible')).toBeTrue();
    expect(getComputedStyle(inField).outlineStyle).toBe('none');
    const plain = host.querySelector('#eink-plain-in') as HTMLInputElement;
    plain.focus();
    expect(getComputedStyle(plain).outlineWidth).toBe('3px');
  });

  it('d5-rk-edit-umschalter-schwarz-gefuellt: gewähltes Werkbank-Segment weiß mit Innenrahmen statt schwarz gefüllt', () => {
    componentStyle('.seg[x] button[aria-pressed="true"][x] { background: rgb(20, 20, 20); color: rgb(250, 250, 250); }');
    const host = mount('<app-sheet-workbench><div x class="seg"><button x aria-pressed="false">Ersetzen</button>'
      + '<button x aria-pressed="true">Davor einfügen</button></div></app-sheet-workbench>');
    const on = host.querySelectorAll('button')[1];
    expect(getComputedStyle(on).backgroundColor).toBe('rgb(20, 20, 20)');
    setEinkActive(true);
    const cs = getComputedStyle(on);
    expect(cs.backgroundColor).toBe('rgb(255, 255, 255)');
    expect(cs.color).toBe('rgb(0, 0, 0)');
    expect(cs.boxShadow).toContain('inset');
  });

  it('d5-rk-guess-anon-liste-ohne-innenabstand: die rahmenlose Listenkarte von /guess bleibt ohne Kartenrahmen', () => {
    const host = mount('<app-guess-list><div class="lists single"><div class="mat-mdc-card start-card">Liste</div></div>'
      + '<div class="mat-mdc-card other">Karte</div></app-guess-list>');
    setEinkActive(true);
    expect(getComputedStyle(host.querySelector('.start-card')!).borderTopStyle).toBe('none');
    // Jede andere Karte behält den 2-px-Rahmen.
    expect(getComputedStyle(host.querySelector('.other')!).borderTopWidth).toBe('2px');
  });

  it('d5-rk-eink-tipp-farbe: Tipp-Knopf weiß mit schwarzem Rahmen, Glühbirne in --rh-hint statt Bernstein', () => {
    componentStyle(`.board-hint-tip[x] { border: 1px solid rgb(224, 169, 58); background: rgb(251, 241, 222); }
      .board-hint-tip[x]:active { background: rgb(247, 228, 191); }
      .board-hint-tip[x] mat-icon[x] { color: rgb(200, 136, 31); }`);
    const host = mount('<button x class="board-hint-tip"><mat-icon x>lightbulb</mat-icon> Tipp (1/3)</button>');
    const btn = host.querySelector('button')!;
    expect(getComputedStyle(btn).backgroundColor).toBe('rgb(251, 241, 222)');
    setEinkActive(true);
    const cs = getComputedStyle(btn);
    expect(cs.backgroundColor).toBe('rgb(255, 255, 255)');
    expect(cs.borderTopWidth).toBe('2px');
    expect(cs.borderTopColor).toBe('rgb(0, 0, 0)');
    expect(cs.color).toBe('rgb(0, 0, 0)');
    expect(getComputedStyle(host.querySelector('mat-icon')!).color).toBe('rgb(77, 77, 77)');
    expect(einkRuleText()).toContain('.board-hint-tip:active');
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
