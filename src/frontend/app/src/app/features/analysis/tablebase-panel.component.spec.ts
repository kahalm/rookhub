import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { TablebasePanelComponent, TablebaseVerdict, verdictOf } from './tablebase-panel.component';
import { TablebaseResult, tablebasePieceCount } from './tablebase.service';

const KQK = '8/8/8/4k3/8/8/8/KQ6 w - - 0 1';
const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

const result = (extra: Partial<TablebaseResult> = {}): TablebaseResult => ({
  status: 'ok', category: 'win', dtz: 17, dtm: 19, checkmate: false, stalemate: false, insufficientMaterial: false,
  moves: [
    { uci: 'b1b5', san: 'Qb5+', category: 'win', dtz: 15, dtm: 17, zeroing: false, checkmate: false, stalemate: false },
    { uci: 'b1b8', san: 'Qb8+', category: 'win', dtz: 21, dtm: 23, zeroing: false, checkmate: false, stalemate: false },
  ],
  ...extra,
});

describe('TablebasePanelComponent', () => {
  function make(fen: string) {
    TestBed.configureTestingModule({
      imports: [TablebasePanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(TablebasePanelComponent);
    const verdicts: (TablebaseVerdict | null)[] = [];
    const played: string[] = [];
    fixture.componentInstance.verdict.subscribe(v => verdicts.push(v));
    fixture.componentInstance.playSan.subscribe(s => played.push(s));
    fixture.componentRef.setInput('fen', fen);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController), verdicts, played };
  }

  it('Steine zählen', () => {
    expect(tablebasePieceCount(KQK)).toBe(3);
    expect(tablebasePieceCount(START)).toBe(32);
    expect(tablebasePieceCount('kaputt')).toBe(-1);
  });

  it('ab 7 Steinen: fragt (verzögert), zeigt Ergebnis und Züge, meldet die Wahrheit an die Leiste, Klick spielt', fakeAsync(() => {
    const { fixture, el, http, verdicts, played } = make(KQK);
    expect(el.textContent).toContain('analysis.tablebase.asking');
    http.expectNone(r => r.url === '/api/tablebase');        // erst nach der Pause
    tick(200);
    const req = http.expectOne(r => r.url === '/api/tablebase');
    expect(req.request.params.get('fen')).toBe(KQK);
    req.flush(result());
    fixture.detectChanges();

    expect(el.querySelector('.verdict')!.classList).toContain('win');
    expect(el.textContent).toContain('analysis.tablebase.mateIn');
    expect([...el.querySelectorAll('.move .san')].map(e => e.textContent)).toEqual(['Qb5+', 'Qb8+']);
    expect(verdicts[verdicts.length - 1]).toEqual({ fen: KQK, whiteHeight: 100, text: '1-0 (TB)' });
    (el.querySelector('.move') as HTMLButtonElement).click();
    expect(played).toEqual(['Qb5+']);
  }));

  it('mehr als 7 Steine: nichts zu sehen, keine Anfrage', fakeAsync(() => {
    const { el, http } = make(START);
    tick(500);
    http.expectNone(r => r.url === '/api/tablebase');
    expect(el.querySelector('.tb')).toBeNull();
  }));

  it('Lichess antwortet nicht: Hinweis, Leiste bleibt bei der Engine, beim nächsten Besuch neu gefragt', fakeAsync(() => {
    const { fixture, el, http, verdicts } = make(KQK);
    tick(200);
    http.expectOne(r => r.url === '/api/tablebase').flush(result({ status: 'unavailable', category: null, moves: [] }));
    fixture.detectChanges();
    expect(el.textContent).toContain('analysis.tablebase.unavailable');
    expect(verdicts[verdicts.length - 1]).toBeNull();

    fixture.componentRef.setInput('fen', '8/8/8/4k3/8/8/8/KR6 w - - 0 1');
    fixture.detectChanges();
    tick(200);
    http.expectOne(r => r.url === '/api/tablebase').flush(result());
    fixture.componentRef.setInput('fen', KQK);
    fixture.detectChanges();
    tick(200);
    http.expectOne(r => r.url === '/api/tablebase');           // der Fehlschlag wurde nicht festgehalten
  }));

  it('verdictOf: Sicht der Seite am Zug → Weiß-Sicht; 50-Züge-Regel = Remis', () => {
    expect(verdictOf(KQK, result())!.whiteHeight).toBe(100);
    expect(verdictOf(KQK.replace(' w ', ' b '), result())!.text).toBe('0-1 (TB)');
    expect(verdictOf(KQK, result({ category: 'cursed-win' }))!.whiteHeight).toBe(50);
    expect(verdictOf(KQK, result({ category: 'draw' }))!.text).toBe('½ (TB)');
    expect(verdictOf(KQK, result({ category: 'unknown' }))).toBeNull();
  });
});
