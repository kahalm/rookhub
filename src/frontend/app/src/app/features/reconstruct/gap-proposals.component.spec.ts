import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GapProposalsComponent } from './gap-proposals.component';
import { PartKind, ReconstructionPart } from './reconstruct.service';

function part(over: Partial<ReconstructionPart>): ReconstructionPart {
  return {
    id: 2, ordinal: 1, kind: PartKind.Position, moves: null, fen: 'x', fromPly: null,
    continuesPrevious: false, certain: true, blackToMove: false, generated: false, note: null, anchored: true, valid: true,
    startFen: null, endFen: null, plyCount: 0, startPly: null, firstBadMove: null, mismatch: false, ...over,
  } as ReconstructionPart;
}

describe('GapProposalsComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GapProposalsComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  function create(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(GapProposalsComponent);
    fixture.componentRef.setInput('target', part({}));
    fixture.componentRef.setInput('plyChoices', [2, 4, 6]);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  it('bietet an einer Stellung Suche und Tiefe an und meldet beides', () => {
    const fixture = create();
    const el: HTMLElement = fixture.nativeElement;
    let closed = 0;
    fixture.componentInstance.closeGap.subscribe(() => closed++);
    el.querySelector<HTMLButtonElement>('.gap-row > button.gap-close')!.click();
    expect(closed).toBe(1);

    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.depth-toggle button'));
    expect(toggles.map(b => b.textContent!.trim())).toEqual(['2', '4', '6']);
    toggles[2].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.plies()).toBe(6);
  });

  it('vor einer Zugfolge nur der Hinweis, keine Suche', () => {
    const el: HTMLElement = create({ target: part({ kind: PartKind.Moves }) }).nativeElement;
    expect(el.querySelector('.gap-close')).toBeNull();
    expect(el.textContent).toContain('reconstruct.gap.needPosition');
  });

  it('sperrt waehrend einer Suche und markiert das Duplikat', () => {
    const el: HTMLElement = create({ searching: true, duplicate: true }).nativeElement;
    const close = el.querySelector<HTMLButtonElement>('.gap-close')!;
    expect(close.disabled).toBeTrue();
    expect(close.classList).toContain('gap-close-dup');
  });

  it('Verwerfen nur mit Vorschlaegen', () => {
    expect(create().nativeElement.textContent).not.toContain('reconstruct.gap.discard');
    const fixture = create({ hasProposals: true });
    let discarded = 0;
    fixture.componentInstance.discard.subscribe(() => discarded++);
    const btn = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent!.includes('reconstruct.gap.discard'))!;
    btn.click();
    expect(discarded).toBe(1);
  });

  it('zeigt die Suche bzw. den Satz zum leeren Ergebnis', () => {
    expect(create({ searchingHere: true }).nativeElement.querySelector('.gap-note')?.textContent)
      .toContain('reconstruct.gap.searching');
    const result = { partId: 2, maxPlies: 4, nodes: 0, budgetExhausted: false, reason: 'too-far', solutions: [] };
    expect(create({ messageKey: 'reconstruct.gap.tooFar', result }).nativeElement.querySelector('.gap-note')?.textContent)
      .toContain('reconstruct.gap.tooFar');
    expect(create().nativeElement.querySelector('.gap-note')).toBeNull();
  });
});
