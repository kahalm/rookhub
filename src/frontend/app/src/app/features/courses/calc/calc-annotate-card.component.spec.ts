import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { CalcAnnotateCardComponent } from './calc-annotate-card.component';

describe('CalcAnnotateCardComponent', () => {
  async function render() {
    await TestBed.configureTestingModule({
      imports: [CalcAnnotateCardComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    // Wie der Stub der Seiten-Specs: die Übersetzung liefert den Schlüssel zurück.
    spyOn(TestBed.inject(TranslateService), 'instant').and.callFake(((k: string) => k) as never);
    const fixture = TestBed.createComponent(CalcAnnotateCardComponent);
    fixture.componentRef.setInput('san', 'Nxe5');
    fixture.componentRef.setInput('glyph', '!');
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  // Aus calculation.component.spec.ts mitgezogen (Zerlegung Z20).
  it('erklärt jedes Symbol im Mouseover — Bedeutung plus Bedienhinweis', async () => {
    const { c } = await render();
    expect(c.evalTooltip('+−')).toBe('calc.eval.whiteWinning (calc.symbolToggleHint)');
    expect(c.evalTooltip('−+')).toBe('calc.eval.blackWinning (calc.symbolToggleHint)');
    expect(c.glyphTooltip('??')).toBe('calc.glyph.blunder (calc.symbolToggleHint)');
  });

  it('lässt keines der angebotenen Symbole ohne Erklärung', async () => {
    const { c } = await render();
    for (const g of c.glyphs) expect(c.glyphTooltip(g)).toContain('calc.glyph.');
    for (const e of c.evals) expect(c.evalTooltip(e)).toContain('calc.eval.');
  });

  it('zeigt den Zug, markiert das gesetzte Symbol und meldet Klicks', async () => {
    const { c, el } = await render();
    expect(el.querySelector('.calc-annotate-target')!.textContent!.trim()).toBe('Nxe5');
    const on = Array.from(el.querySelectorAll('.calc-sym--on')).map(b => b.textContent!.trim());
    expect(on).toEqual(['!']);
    const picked: string[] = [];
    c.glyphPicked.subscribe(g => picked.push(g));
    c.cleared.subscribe(() => picked.push('clear'));
    (el.querySelectorAll('.calc-symbols')[0].querySelector('.calc-sym') as HTMLButtonElement).click();
    (el.querySelector('.calc-sym--clear') as HTMLButtonElement).click();
    expect(picked).toEqual([c.glyphs[0], 'clear']);
  });

  it('Kommentar: zweiseitiger Entwurf, Enter speichert', async () => {
    const { fixture, c, el } = await render();
    await fixture.whenStable();
    const input = el.querySelector('.calc-comment-row input') as HTMLInputElement;
    input.value = 'gewinnt einen Bauern';
    input.dispatchEvent(new Event('input'));
    expect(c.comment()).toBe('gewinnt einen Bauern');
    let saved = 0;
    c.commentSaved.subscribe(() => saved++);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(saved).toBe(1);
  });
});
