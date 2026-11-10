import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { mountWithComponentStyles, setDarkTheme, textContrast } from '../../testing/contrast';
import { DeepAnalysisDialogComponent } from './deep-analysis-dialog.component';
import { DeepStored } from './deep-analysis.util';

describe('DeepAnalysisDialogComponent', () => {
  const FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
  const stored: DeepStored = {
    sf: { depth: 20, lines: [{ evalText: '+0.30', san: '1... e5', positive: true }] },
    lc0: { nodes: 100_000, lines: [{ evalText: '+0.25', san: '1... c5', positive: true }] },
  };
  const job = (id: number, p: object) => ({ id, fen: FEN, title: null, engineId: 'e', targetDepth: 40, multiPv: 3,
    status: 'running', reachedDepth: 0, resultJson: null, secondsSpent: 0, lastError: null, createdAt: '', updatedAt: '',
    lastRunAt: null, finishedAt: null, ...p });

  function setup() {
    TestBed.configureTestingModule({
      imports: [DeepAnalysisDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }), { provide: MAT_DIALOG_DATA, useValue: { fen: FEN, stored } }],
    });
    const fixture = TestBed.createComponent(DeepAnalysisDialogComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }
  const sans = (el: HTMLElement) => Array.from(el.querySelectorAll('.line-san')).map(e => e.textContent!.trim());

  it('startet die Analyse und zeigt bis zum Überholen die hinterlegten Linien', () => {
    const { fixture, http, el } = setup();
    const req = http.expectOne('/api/deep-analysis');
    expect(req.request.body).toEqual({ fen: FEN });
    req.flush({ stockfish: job(1, { reachedDepth: 18, resultJson: '{"depth":18,"pvs":[{"moves":["e7e5"],"cp":30}]}' }),
      lc0: job(2, { resultJson: '{"depth":8,"nodes":40000,"pvs":[{"moves":["c7c5"],"cp":20}]}' }),
      stockfishDepth: 40, lc0Nodes: 500000 });
    fixture.detectChanges();
    expect(sans(el)).toEqual(['1... e5', '1... c5']);
    expect(el.textContent).toContain('games.deep.storedDepth');
  });

  it('ist der Auftrag tiefer als das Hinterlegte, kommen seine Linien', () => {
    const { fixture, http, el } = setup();
    http.expectOne('/api/deep-analysis').flush({
      stockfish: job(1, { reachedDepth: 24, resultJson: '{"depth":24,"pvs":[{"moves":["c7c5","g1f3"],"cp":28}]}' }),
      lc0: null, stockfishDepth: 40, lc0Nodes: 500000 });
    fixture.detectChanges();
    expect(sans(el)[0]).toContain('c5');
    expect(el.textContent).toContain('games.deep.live');
    expect(el.textContent).toContain('games.deep.noLc0');
  });
  // UI-Sweep 2026-11-10 (d10-rk-sanpipe-dialoge-ebook-zugliste): Engine-Linien in den Figurenbuchstaben der Oberflächensprache.
  it('zeigt die Linien auf Deutsch mit deutschen Figurenbuchstaben (Sf3 statt Nf3)', () => {
    const { fixture, http, el } = setup();
    TestBed.inject(TranslateService).use('de');
    http.expectOne('/api/deep-analysis').flush({
      stockfish: job(1, { reachedDepth: 24, resultJson: '{"depth":24,"pvs":[{"moves":["g8f6","g1f3","b8c6"],"cp":28}]}' }),
      lc0: null, stockfishDepth: 40, lc0Nodes: 500000 });
    fixture.detectChanges();
    expect(sans(el)[0]).toContain('Sf6');
    expect(sans(el)[0]).toContain('Sf3');
    expect(sans(el)[0]).not.toContain('Nf');
  });

  // UI-Sweep 2026-11-10 (d10-rk-tiefe-analyse-lc0-orange-kontrast): „Lc0“ trug #ff9800 — hell nur ≈ 2:1.
  for (const dark of [false, true]) {
    it(`„Lc0“-Überschrift erreicht 4,5:1 auf der Dialogfläche (${dark ? 'dunkel' : 'hell'})`, () => {
      const was = setDarkTheme(dark);
      const dispose = mountWithComponentStyles(DeepAnalysisDialogComponent,
        '<section class="side lc0"><div class="side-head"><span class="name">Lc0</span></div></section>',
        'var(--mat-sys-surface-container-high)');
      try {
        const ratio = textContrast(document.querySelector('.side.lc0 .name')!);
        expect(ratio).withContext(`${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
      } finally { dispose(); setDarkTheme(was); }
    });
  }
});
