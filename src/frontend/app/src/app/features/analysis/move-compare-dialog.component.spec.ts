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
  function make(candidates = [{ uci: 'e2e4', evalText: '+0.30' }, { uci: 'd2d4', evalText: '+0.25' }, { uci: 'g1f3', evalText: '+0.20' }]) {
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
      engineAvailable: true, ownEngine: false, explanations: true, maxCandidates: 4, defaultDepth: 22, maxDepth: 24,
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
});
