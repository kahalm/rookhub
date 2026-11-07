import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';
import { Chess } from 'chess.js';
import { AnalysisEngineService } from '../../analysis/analysis-engine.service';
import { positionKey } from '../../../shared/player-card/opponent-positions';
import type { Deviations } from '../../../shared/player-card/training-lines';
import { DeviationTrainerComponent, DEVIATION_THRESHOLD_KEY } from './deviation-trainer.component';
import { DEV_OPP_DELAY_MS } from './deviation-session';

const fenAfter = (sans: string[]) => { const c = new Chess(); sans.forEach(s => c.move(s)); return c.fen(); };

const ANSWER: Deviations = {
  repertoires: [{ id: 3, name: 'Weiß', colors: ['w'] }], repertoire: null, color: 'w', colors: ['w'], games: 9, elo: 2143, total: 1,
  deviations: [{ path: ['e4'], fen: fenAfter(['e4']), san: 'c5', games: 7, reached: 9, lastYear: 2024, repertoire: ['e5'] }],
  more: 0,
  positions: {
    [positionKey(fenAfter([]))]: { n: 9, y: 2024, m: {} },
    [positionKey(fenAfter(['e4']))]: { n: 9, y: 2024, m: { c5: 7, e5: 2 } },
    [positionKey(fenAfter(['e4', 'c5']))]: { n: 7, y: 2024, m: {} },
  },
};

describe('DeviationTrainerComponent (Abweichungen trainieren, 0.892.0)', () => {
  let fixture: ComponentFixture<DeviationTrainerComponent>;
  let http: HttpTestingController;
  let engine: any;

  function setup(query: Record<string, string>): DeviationTrainerComponent {
    engine = {
      analysis$: new Subject<any>(), depthLimit: 22,
      analyze: jasmine.createSpy('analyze').and.returnValue(Promise.resolve()), stop: jasmine.createSpy('stop'),
      setMultiPv: () => {}, setDepth: (d: number) => { engine.depthLimit = d; },
    };
    TestBed.configureTestingModule({
      imports: [DeviationTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: (k: string) => query[k] ?? null } } } },
        { provide: AnalysisEngineService, useValue: engine },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DeviationTrainerComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => { localStorage.removeItem(DEVIATION_THRESHOLD_KEY); fixture?.destroy(); });

  it('Drag & Drop: dests und lastMove bleiben bei gleicher Stellung dasselbe Objekt (sonst springt die gezogene Figur zurück, 0.893.5)', () => {
    const c = setup({ deviations: 'league:1606921', color: 'w', name: 'Maras' });
    http.expectOne(r => r.url === '/api/league/player/1606921/deviations').flush(ANSWER);
    fixture.detectChanges();
    expect(c.s.playable).toBeTrue();
    const d1 = c.dests, d2 = c.dests;
    expect(d1.size).toBeGreaterThan(0);
    expect(d2).toBe(d1);
    expect(c.lastMove).toBe(c.lastMove);
    c.ngOnDestroy();
  });

  it('lädt die Abweichungen des Gegners mit Farbe und Filter, spielt den Pfad, fragt nach der Abweichung', fakeAsync(() => {
    const c = setup({ deviations: 'league:1606921', color: 'w', name: 'Maras', source: 'both', years: '3', evil: 'x' });
    const req = http.expectOne(r => r.url === '/api/league/player/1606921/deviations');
    expect(req.request.params.get('color')).toBe('w');
    expect(req.request.params.get('source')).toBe('both');
    expect(req.request.params.get('years')).toBe('3');
    expect(req.request.params.get('evil')).toBeNull();
    req.flush(ANSWER);
    fixture.detectChanges();
    expect(c.session.phase).toBe('PATH');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.opponent-note')!.textContent).toContain('opponentNote.reached');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    tick(DEV_OPP_DELAY_MS);
    fixture.detectChanges();
    expect(c.session.phase).toBe('ASK');
    expect(engine.analyze).toHaveBeenCalledWith(fenAfter(['e4', 'c5']));   // Engine rechnet schon, während ich überlege
    expect(el.querySelector('.ask')).not.toBeNull();
    expect(c.askText).toBe('deviations.askDeviation');
    c.ngOnDestroy();
    tick(5000);
  }));

  it('nach dem Ende seiner Partien: Maia-Angebot mit der Stufe des Gegners (2143 → 2200) ab der Schlussstellung', fakeAsync(() => {
    const c = setup({ deviations: 'prep:42', color: 'w', name: 'Maras', all: 'true' });
    const req = http.expectOne(r => r.url === '/api/prep/player/42/deviations');
    expect(req.request.params.get('all')).toBe('true');
    req.flush(ANSWER);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    tick(DEV_OPP_DELAY_MS);
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    tick(2600);                                                  // Urteil ohne Engine-Antwort → gut
    flushMicrotasks();
    c.session.tap();
    fixture.detectChanges();
    expect(c.session.phase).toBe('END');
    expect(c.maiaElo).toBe(2200);
    const url = new URL(c.maiaLink, 'http://x');
    expect(url.pathname).toBe('/analysis');
    expect(url.searchParams.get('maia')).toBe('2200');
    expect(url.searchParams.get('orientation')).toBe('white');
    expect(url.searchParams.get('fen')).toBe(fenAfter(['e4', 'c5', 'Nf3']));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('a.maia')!.getAttribute('href')).toBe(c.maiaLink);
    // ab 2.Nf3 keine Partien mehr: deutliche Meldung, die bleibt
    expect(el.querySelector('.opponent-note.left')).not.toBeNull();
    // Nachbetrachtung: ← klickt zurück
    c.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(c.reviewPly).toBe(2);
    expect(c.fen).toBe(fenAfter(['e4', 'c5']));
    c.ngOnDestroy();
  }));

  it('Hotfix 0.893.1: Farbe der Antwort gilt (Brett, Abfrage), Kapitelfarben der Karte gehen mit, „statt" erst nach der Abweichung', fakeAsync(() => {
    const per = JSON.stringify({ '3': { 'Französisch': 'b' } });
    const c = setup({ deviations: 'prep:42', color: 'w', chapterColors: per });
    const req = http.expectOne(() => true);
    expect(req.request.params.get('chapterColors')).toBe(per);
    const path = ['e4', 'e6'];
    req.flush({ ...ANSWER, color: 'b', colors: ['b'],
      deviations: [{ path, fen: fenAfter(path), san: 'Nf3', games: 2, reached: 2, lastYear: 2025, repertoire: ['d4'] }] });
    fixture.detectChanges();
    expect(c.orientation).toBe('black');
    expect(c.session.color).toBe('b');
    expect(c.insteadText).toBe('');                               // 1.e4 läuft gerade — noch keine Abweichung
    tick(DEV_OPP_DELAY_MS);
    expect(c.session.phase).toBe('PATH');
    expect(c.insteadText).toBe('');
    c.onMove({ orig: 'e7' as any, dest: 'e6' as any });
    tick(DEV_OPP_DELAY_MS);
    expect(c.session.phase).toBe('ASK');
    expect(c.insteadText).toBe('deviations.instead');
    c.ngOnDestroy();
    tick(5000);
  }));

  it('Schwelle je Gerät gemerkt; ungültige Adresse = leer ohne Anfrage', () => {
    const c = setup({ deviations: 'evil:1' });
    http.expectNone(() => true);
    expect(c.session.phase).toBe('EMPTY');
    c.setThreshold(50);
    expect(localStorage.getItem(DEVIATION_THRESHOLD_KEY)).toBe('50');
    c.setThreshold(7);
    expect(c.threshold).toBe(50);
  });

  it('schmal (390 px): die Seite läuft nicht seitlich über', fakeAsync(() => {
    setup({ deviations: 'prep:42', color: 'w', name: 'Ein sehr langer Name, Vorname Zweitname' });
    http.expectOne(() => true).flush(ANSWER);
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = '390px';
    document.body.appendChild(host);
    fixture.detectChanges();
    const dev = host.querySelector('.dev') as HTMLElement;
    expect(dev.scrollWidth).toBeLessThanOrEqual(dev.clientWidth + 1);
    host.remove();
    fixture.componentInstance.ngOnDestroy();
  }));
});
