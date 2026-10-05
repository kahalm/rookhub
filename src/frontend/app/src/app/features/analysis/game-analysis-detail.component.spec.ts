import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { GameAnalysisDetailComponent } from './game-analysis-detail.component';

const POSITIONS = [
  { ply: 0, moveNumber: 1, white: true,  san: 'e4',  uci: 'e2e4', fen: 'start-fen', evalText: '+0.30', depth: 30, analyzed: true },
  { ply: 1, moveNumber: 1, white: false, san: 'e5',  uci: 'e7e5', fen: 'fen-after-e4', evalText: '-0.20', depth: 30, analyzed: true },
  { ply: 2, moveNumber: 2, white: true,  san: 'Nf3', uci: 'g1f3', fen: 'fen-after-e5', evalText: null, depth: 0, analyzed: false },
];

describe('GameAnalysisDetailComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GameAnalysisDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load() {
    const fixture = TestBed.createComponent(GameAnalysisDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses/5').flush({
      id: 5, title: 'A – B', white: 'A', black: 'B', result: '1-0', event: null,
      targetDepth: 30, multiPv: 5, engineId: 'eei_x', status: 'running',
      plyCount: 3, analyzedPlies: 2, lastError: null,
      createdAt: '2026-09-05T10:00:00Z', finishedAt: null, positions: POSITIONS,
    });
    return fixture;
  }

  it('startet in der Ausgangsstellung und blaettert vor und zurueck', () => {
    const c = load().componentInstance;
    expect(c.index).toBe(-1);
    expect(c.currentFen).toBe('start-fen');    // Stellung VOR dem ersten Zug

    c.go(1);
    expect(c.index).toBe(0);
    expect(c.currentFen).toBe('fen-after-e4'); // nach 1.e4
    expect(c.lastMove).toEqual(['e2', 'e4']);

    c.go(-1);
    expect(c.index).toBe(-1);
    // Vor dem ersten Zug gibt es keinen letzten Zug zum Hervorheben.
    expect(c.lastMove).toBeUndefined();
  });

  it('laeuft nicht ueber die Partiegrenzen hinaus', () => {
    const c = load().componentInstance;
    c.go(-1); c.go(-1);
    expect(c.index).toBe(-1);

    for (let i = 0; i < 10; i++) c.go(1);
    expect(c.index).toBe(POSITIONS.length - 1);
    // Am Ende bleibt die Schlussstellung stehen (kein undefined-FEN).
    expect(c.currentFen).toBe('fen-after-e5');
  });

  it('zeigt am Ende die SCHLUSSSTELLUNG, nicht die davor', () => {
    // Gespeichert ist je Zeile nur die Stellung VOR dem Zug. Ohne Nachspielen des letzten Zuges
    // zeigte das Brett beim letzten Halbzug dieselbe Stellung wie einen Zug zuvor — die
    // Schlussstellung liess sich gar nicht ansehen.
    const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const afterE4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';
    const fixture = TestBed.createComponent(GameAnalysisDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses/5').flush({
      id: 5, title: 'A – B', white: 'A', black: 'B', result: '*', event: null,
      targetDepth: 30, multiPv: 5, engineId: 'eei_x', status: 'done',
      plyCount: 2, analyzedPlies: 2, lastError: null,
      createdAt: '2026-09-05T10:00:00Z', finishedAt: null,
      positions: [
        { ply: 0, moveNumber: 1, white: true, san: 'e4', uci: 'e2e4', fen: start, evalText: '+0.30', depth: 30, analyzed: true },
        { ply: 1, moveNumber: 1, white: false, san: 'e5', uci: 'e7e5', fen: afterE4, evalText: '-0.20', depth: 30, analyzed: true },
      ],
    });
    const c = fixture.componentInstance;

    c.go(1);
    expect(c.currentFen).toBe(afterE4);
    c.go(1);
    expect(c.index).toBe(1);
    expect(c.currentFen).not.toBe(afterE4);
    expect(c.currentFen).toContain('4p3');   // nach 1.e4 e5
  });

  it('zeigt den Fortschritt, solange noch gerechnet wird', () => {
    const c = load().componentInstance;
    expect(c.percent).toBe(67);   // 2 von 3
    expect(c.positions.filter(p => !p.analyzed).length).toBe(1);
  });

  it('blaettert nicht, wenn die Pfeiltaste einem Eingabefeld oder offenen Menue gehoert', () => {
    const c = load().componentInstance;
    const press = (target: EventTarget, init: KeyboardEventInit = {}) => {
      const e = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true, ...init });
      Object.defineProperty(e, 'target', { value: target });
      c.onKey(e);
    };
    const overlay = document.createElement('div');
    overlay.className = 'cdk-overlay-container';
    const item = document.createElement('button');
    overlay.appendChild(item);
    document.body.appendChild(overlay);
    try {
      press(item);
      press(document.createElement('input'));
      press(document.body, { altKey: true });
      expect(c.index).toBe(-1);
    } finally {
      overlay.remove();
    }
    press(document.body);
    expect(c.index).toBe(0);
  });

  // ── Konvergenz (Knotenanalyse) ──

  function loadNodes(): ComponentFixtureLike {
    const fixture = TestBed.createComponent(GameAnalysisDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses/5').flush({
      id: 5, title: 'A – B (50k nodes)', white: 'A', black: 'B', result: '1-0', event: null,
      targetDepth: 30, targetNodes: 50000, multiPv: 3, engineId: 'eei_lc0', status: 'done',
      plyCount: 3, analyzedPlies: 3, lastError: null,
      createdAt: '2026-09-05T10:00:00Z', finishedAt: null, positions: POSITIONS,
    });
    return fixture;
  }
  type ComponentFixtureLike = ReturnType<typeof TestBed.createComponent<GameAnalysisDetailComponent>>;

  it('holt die Konvergenz NUR bei einer Knotenanalyse (die Tiefenanalyse oben fragt sie nie)', () => {
    load();
    http.expectNone('/api/game-analyses/5/convergence');   // load() ist eine Tiefenanalyse
  });

  it('zeigt die Konvergenz-Tabelle: Zeile je Stufe, Abstand in cp und Prozentpunkten', () => {
    const fixture = loadNodes();
    http.expectOne('/api/game-analyses/5/convergence').flush({
      targetNodes: 50000, positions: 12,
      rows: [
        { threshold: 10000, positions: 12, sameMovePercent: 58.3, medianCp: 18, p90Cp: 140, medianWinPct: 1.4, p90WinPct: 9.8,
          moveChanges: 0, mateMismatch: 1, meanNodes: 9120 },
        { threshold: 50000, positions: 12, sameMovePercent: 100, medianCp: 0, p90Cp: 0, medianWinPct: 0, p90WinPct: 0,
          moveChanges: 2, mateMismatch: 0, meanNodes: 49800 },
      ],
    });
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.conv-table tbody tr');
    expect(rows.length).toBe(2);
    const first = rows[0].textContent!.replace(/\s+/g, ' ');
    expect(first).toContain('58');         // gleicher Zug in %
    expect(first).toContain('140');        // 90 %-Abstand in cp
    expect(fixture.componentInstance.mateMismatches).toBe(1);
  });

  it('sagt es, wenn es noch keine Zwischenstaende gibt', () => {
    const fixture = loadNodes();
    http.expectOne('/api/game-analyses/5/convergence').flush({ targetNodes: 50000, positions: 0, rows: [] });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.conv-table')).toBeNull();
    expect(el.querySelector('.conv')).not.toBeNull();
  });

  it('bleibt ohne Tabelle, wenn der Abruf der Konvergenz scheitert (die Partie steht trotzdem da)', () => {
    const fixture = loadNodes();
    http.expectOne('/api/game-analyses/5/convergence').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(fixture.componentInstance.convergence).toBeNull();
    expect(fixture.componentInstance.positions.length).toBe(3);
  });
});
