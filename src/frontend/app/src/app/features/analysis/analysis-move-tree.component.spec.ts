import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { AnalysisMoveTreeComponent } from './analysis-move-tree.component';
import { AnalysisNode, createRoot, playSan } from './analysis-tree';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function play(parent: AnalysisNode, sans: string): AnalysisNode {
  let n = parent;
  for (const san of sans.split(' ')) n = playSan(n, san)!;
  return n;
}

/** Der Baum aus dem Screenshot: 1.e4 c5 2.f4 f6 (2...e6 3.g3 Nc6 (3...Na6 4.f5 Ke7)). */
function screenshotTree() {
  const root = createRoot(START);
  const f4 = play(root, 'e4 c5 f4');
  play(f4, 'f6');
  const g3 = play(f4, 'e6 g3');
  play(g3, 'Nc6');
  const ke7 = play(g3, 'Na6 f5 Ke7');
  root.children[0].evalText = '+0.20';
  return { root, f4, ke7 };
}

describe('AnalysisMoveTreeComponent', () => {
  function make(root: AnalysisNode, current: AnalysisNode) {
    TestBed.configureTestingModule({
      imports: [AnalysisMoveTreeComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(AnalysisMoveTreeComponent);
    fixture.componentRef.setInput('root', root);
    fixture.componentRef.setInput('current', current);
    fixture.componentRef.setInput('version', 1);
    fixture.detectChanges();
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    return { fixture, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, overlay };
  }

  it('shows the main line as a table with evals and the variation as a block', () => {
    const { root, ke7 } = screenshotTree();
    const { el } = make(root, ke7);
    const texts = (parent: Element, sel: string) => [...parent.querySelectorAll(sel)].map(e => e.textContent!.trim()).join(' ');
    const rows = [...el.querySelectorAll('.row')].map(r => texts(r, '.no, .san, .ev, .gap'));
    expect(rows).toEqual(['1 e4 +0.20 c5', '2 f4 f6']);
    expect(texts(el.querySelector('.vars')!, '.var-line > span')).toBe('2...e6 3.g3 Nc6 ( 3...Na6 4.f5 Ke7 )');
    expect(el.querySelector('.vmove.active')!.textContent).toContain('Ke7');
  });

  // UI-Sweep 2026-10-11 (r2-rg-notation): Anzeige in den Figurenbuchstaben der Sprache, der Baum bleibt englisch.
  it('shows moves in German piece letters when the UI is German', () => {
    const { root, ke7 } = screenshotTree();
    const { fixture, el } = make(root, ke7);
    TestBed.inject(TranslateService).use('de');
    fixture.componentRef.setInput('version', 2);
    fixture.detectChanges();
    const texts = (parent: Element, sel: string) => [...parent.querySelectorAll(sel)].map(e => e.textContent!.trim()).join(' ');
    expect(texts(el.querySelector('.vars')!, '.var-line > span')).toBe('2...e6 3.g3 Sc6 ( 3...Sa6 4.f5 Ke7 )');
    expect(ke7.parent!.parent!.san).toBe('Na6');
  });

  it('a click selects the move', () => {
    const { root, ke7 } = screenshotTree();
    const { c, el } = make(root, ke7);
    const picked: AnalysisNode[] = [];
    c.select.subscribe(n => picked.push(n));
    (el.querySelector('.row .move') as HTMLElement).click();
    expect(picked).toEqual([root.children[0]]);
  });

  it('right-click opens the menu; a variation offers promote and make-main-line, the main line only star and delete', () => {
    const { root, ke7 } = screenshotTree();
    const { c, el, fixture, overlay } = make(root, ke7);
    const actions: string[] = [];
    c.action.subscribe(a => actions.push(`${a.kind}:${a.node.san}`));

    const na6 = [...el.querySelectorAll<HTMLElement>('.vmove')].find(e => e.textContent!.includes('Na6'))!;
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 40, clientY: 50 });
    na6.dispatchEvent(ev);
    fixture.detectChanges();
    expect(ev.defaultPrevented).toBeTrue();
    const items = [...overlay.querySelectorAll<HTMLButtonElement>('[mat-menu-item]')];
    expect(items.map(b => b.textContent!.trim())).toEqual([
      'star_border analysis.star.add', 'arrow_upward analysis.tree.promote', 'vertical_align_top analysis.tree.makeMainline',
      'delete_outline analysis.tree.deleteFrom',
    ]);
    items[1].click();
    expect(actions).toEqual(['promote:Na6']);
  });

  it('a long press on a touch screen opens the menu too, and the lifted finger does not select', fakeAsync(() => {
    const { root, ke7 } = screenshotTree();
    const { c, el, fixture, overlay } = make(root, ke7);
    const picked: AnalysisNode[] = [];
    c.select.subscribe(n => picked.push(n));
    const e4 = el.querySelector('.row .move') as HTMLElement;
    e4.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch', clientX: 10, clientY: 10 }));
    tick(499);
    expect(overlay.querySelectorAll('[mat-menu-item]').length).toBe(0);
    tick(1);
    fixture.detectChanges();
    const items = [...overlay.querySelectorAll('[mat-menu-item]')].map(b => b.textContent!.trim());
    expect(items).toEqual(['star_border analysis.star.add', 'delete_outline analysis.tree.deleteFrom']);
    e4.click();
    expect(picked).toEqual([]);
    tick(1000);
  }));

  it('moving the finger is a swipe, not a long press', fakeAsync(() => {
    const { root, ke7 } = screenshotTree();
    const { el, fixture, overlay } = make(root, ke7);
    const e4 = el.querySelector('.row .move') as HTMLElement;
    e4.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch', clientX: 10, clientY: 10 }));
    e4.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'touch', clientX: 10, clientY: 40 }));
    tick(800);
    fixture.detectChanges();
    expect(overlay.querySelectorAll('[mat-menu-item]').length).toBe(0);
  }));

  it('rebuilds the table only when the tree changed, not when just the current move moves', () => {
    const { root, f4 } = screenshotTree();
    const { fixture, c } = make(root, f4);
    const before = c.items;
    fixture.componentRef.setInput('current', root.children[0]);
    fixture.detectChanges();
    expect(c.items).toBe(before);
    play(f4.children[0], 'Nc3');
    fixture.componentRef.setInput('version', 2);
    fixture.detectChanges();
    expect(c.items).not.toBe(before);
    expect(fixture.nativeElement.querySelectorAll('.row').length).toBe(3);
  });

  // b1-rk-zugbaum-bewertung-aktiv-dunkel: die Bewertung im gewählten Zug war fest weiß — auf der hellblauen Markierung
  // im dunklen Design ≈ 1,6:1. Sie folgt jetzt der Schrift der Markierung (on-primary), in hell wie dunkel.
  it('colours the eval of the active move from on-primary, not a fixed white', () => {
    const css = (((AnalysisMoveTreeComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
      .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    expect(css).not.toContain('rgba(255,255,255');
    expect(css).toMatch(/\.move\.active(\[[^\]]*\])?\.ev(\[[^\]]*\])?\{color:color-mix\(insrgb,var\(--mat-sys-on-primary\)85%,transparent\)/);
  });
});
