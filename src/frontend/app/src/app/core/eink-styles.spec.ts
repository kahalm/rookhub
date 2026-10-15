import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { setEinkActive } from './eink';
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
