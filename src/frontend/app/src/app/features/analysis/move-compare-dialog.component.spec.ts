import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MoveCompareDialogComponent, legalMoves } from './move-compare-dialog.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('MoveCompareDialogComponent', () => {
  function make(candidates = [{ uci: 'e2e4', evalText: '+0.30' }, { uci: 'd2d4', evalText: '+0.25' }, { uci: 'g1f3', evalText: '+0.20' }],
                engineAvailable = true) {
    const ref = jasmine.createSpyObj<MatDialogRef<MoveCompareDialogComponent>>('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [MoveCompareDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: ref },
        { provide: MAT_DIALOG_DATA, useValue: { fen: START, candidates } }],
    });
    const fixture = TestBed.createComponent(MoveCompareDialogComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/move-comparisons/status').flush({
      engineAvailable, ownEngine: false, explanations: true, maxCandidates: 4, defaultDepth: 22, maxDepth: 24,
      openComparisons: 0, maxOpen: 3,
    });
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http, ref };
  }

  it('lists every legal move, engine suggestions first and the first two preselected', () => {
    expect(legalMoves(START).length).toBe(20);
    const { c, fixture } = make();
    expect(c.engineMoves().map(m => m.san)).toEqual(['e4', 'd4', 'Nf3']);
    expect(c.otherMoves().length).toBe(17);
    expect(c.selected()).toEqual(['e2e4', 'd2d4']);
    expect(c.depthOptions()).toEqual([16, 18, 20, 22, 24]);   // Haus-Engine: höchstens 24
    expect(c.depth()).toBe(22);
    expect(fixture.nativeElement.textContent).toContain('moveCompare.dialog.houseEngine');
  });

  it('caps the selection at the maximum and needs at least two moves', () => {
    const { c } = make([]);
    expect(c.selected()).toEqual([]);
    expect(c.canSubmit()).toBeFalse();
    for (const uci of ['e2e4', 'd2d4', 'c2c4', 'g1f3', 'b1c3']) c.toggle(uci);
    expect(c.selected()).toEqual(['e2e4', 'd2d4', 'c2c4', 'g1f3']);
    expect(c.full()).toBeTrue();
    c.toggle('d2d4');
    expect(c.selected()).toEqual(['e2e4', 'c2c4', 'g1f3']);
    expect(c.canSubmit()).toBeTrue();
  });

  it('creates the comparison and goes to its page', () => {
    const { c, http, ref } = make();
    const nav = spyOn(TestBed.inject(Router), 'navigate');
    c.submit();
    const req = http.expectOne('/api/move-comparisons');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(jasmine.objectContaining({ fen: START, moves: ['e2e4', 'd2d4'], depth: 22 }));
    req.flush({ id: 7 });
    expect(ref.close).toHaveBeenCalled();
    expect(nav).toHaveBeenCalledWith(['/analysis/compare', 7]);
  });

  it('shows the reason of a refusal', () => {
    const { c, http, fixture } = make();
    c.submit();
    http.expectOne('/api/move-comparisons').flush({ reason: 'too-many-open', message: 'x' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(c.error()).toBe('moveCompare.error.too-many-open');
    expect(c.busy()).toBeFalse();
  });

  // d9-rk-vergleichen-dialog-engine-hinweis: ohne Server-Engine sagt der Dialog, dass der Vergleich auf dem SERVER rechnet
  // (die Browser-Engine zählt nicht), färbt das leise statt als Warnung, verlinkt das Profil, zeigt den Grund neben dem
  // gesperrten Knopf und keinen Zähler/keine Tiefe, die nichts bewirken.
  it('without a server engine explains it quietly, links the profile and names the reason next to the locked button', () => {
    const { c, fixture, ref } = make(undefined, false);
    const el: HTMLElement = fixture.nativeElement;
    expect(c.noEngine()).toBeTrue();
    expect(c.canSubmit()).toBeFalse();
    const note = el.querySelector('.note.no-engine')!;
    expect(note.textContent).toContain('moveCompare.dialog.noEngine');
    expect(el.querySelector('p.warn')).toBeNull();
    const link = note.querySelector('a[href="/profile"]') as HTMLAnchorElement;
    expect(link).not.toBeNull();
    link.click();
    expect(ref.close).toHaveBeenCalled();
    expect(el.querySelector('mat-dialog-actions .why')?.textContent).toContain('moveCompare.dialog.noEngineWhy');
    expect(el.querySelector('.foot')).toBeNull();
  });

  it('with an engine shows neither the note nor a reason', () => {
    const { fixture } = make();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.no-engine')).toBeNull();
    expect(el.querySelector('mat-dialog-actions .why')).toBeNull();
    expect(el.querySelector('.foot')).not.toBeNull();
  });
});
