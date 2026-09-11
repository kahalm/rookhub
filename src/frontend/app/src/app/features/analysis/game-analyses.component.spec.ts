import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GameAnalysesComponent } from './game-analyses.component';
import { GameAnalysis } from './game-analysis.service';

function analysis(over: Partial<GameAnalysis> = {}): GameAnalysis {
  return {
    id: 1, title: 'A – B', white: 'A', black: 'B', result: '1-0', event: null,
    targetDepth: 30, multiPv: 5, engineId: 'eei_x', status: 'running',
    plyCount: 40, analyzedPlies: 10, lastError: null, isPublic: false, annotated: false,
    createdAt: '2026-09-05T10:00:00Z', finishedAt: null, ...over,
  };
}

describe('GameAnalysesComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GameAnalysesComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  /** Das Tempo holt die Seite bei JEDEM Abruf mit; fuer die meisten Tests ist es Beiwerk. */
  function drainThroughput(perMinute = 0, etaMinutes: number | null = null) {
    http.match('/api/game-analyses/throughput').forEach(r => r.flush({
      perMinute, analyzedInWindow: 0, windowMinutes: 0, remaining: 0, etaMinutes,
    }));
  }

  afterEach(() => {
    drainThroughput();
    http.verify();
  });

  it('zeigt den Fortschritt je Partie in Prozent', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis({ analyzedPlies: 10, plyCount: 40 })]);

    expect(fixture.componentInstance.percent(fixture.componentInstance.analyses[0])).toBe(25);
  });

  it('pollt NUR solange eine Partie offen ist', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis({ status: 'running' })]);
    expect(fixture.componentInstance.hasOpen()).toBeTrue();

    fixture.componentInstance.analyses = [analysis({ status: 'done' })];
    // Eine abgeschlossene Liste erzeugt keinen Verkehr mehr — sonst pollt die Seite ewig.
    expect(fixture.componentInstance.hasOpen()).toBeFalse();
  });

  it('meldet einen Startfehler mit der Server-Begruendung', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([]);

    fixture.componentInstance.pgn = '1. e4 e5';
    fixture.componentInstance.create();
    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/game-analyses');
    expect(req.request.body.targetDepth).toBe(30);
    // Der Nutzer hat gerade geklickt → keine stille Fehlerbehandlung.
    req.flush({ message: 'Keine Hintergrund-Engine konfiguriert' }, { status: 400, statusText: 'Bad Request' });
    expect(fixture.componentInstance.creating).toBeFalse();
    expect(fixture.componentInstance.pgn).toBe('1. e4 e5');   // Eingabe bleibt erhalten
  });

  it('nimmt die neue Analyse ohne Neuladen in die Liste', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([]);

    fixture.componentInstance.pgn = '1. e4 e5';
    fixture.componentInstance.create();
    http.expectOne('/api/game-analyses').flush(analysis({ id: 7, status: 'pending' }));

    expect(fixture.componentInstance.analyses.map(a => a.id)).toEqual([7]);
    expect(fixture.componentInstance.pgn).toBe('');
  });

  /**
   * Eine gescheiterte (bzw. zurueckgestellte) Analyse kommt nie voran. Zaehlte sie im
   * Gesamtbalken mit, stuende der still, obwohl die Engine arbeitet — genau so gemeldet
   * am 2026-09-10. Sichtbar bleibt sie trotzdem: die Zahl steht daneben.
   */
  it('der Gesamtfortschritt laesst gescheiterte Partien draussen', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([
      analysis({ id: 1, status: 'running', plyCount: 100, analyzedPlies: 50 }),
      analysis({ id: 2, status: 'failed', plyCount: 900, analyzedPlies: 0 }),
    ]);
    fixture.detectChanges();

    const c = fixture.componentInstance;
    expect(c.totalPlies).withContext('nur die laufende Partie').toBe(100);
    expect(c.analyzedPlies).toBe(50);
    expect(c.overallPercent).withContext('nicht 5 %').toBe(50);
    expect(c.failedCount).toBe(1);
  });

  /**
   * Der Balken bewegt sich bei tausend Stellungen um Bruchteile eines Prozents und sieht aus, als
   * stuende er. Die Rate beantwortet die eigentliche Frage — aber erst, wenn genug Zeit zwischen
   * den Proben liegt; aus zwei Abrufen im Sekundenabstand laesst sich nichts hochrechnen.
   */
  /** Der Knopf gegen die Sackgasse: ein Auftrag klebt an seiner Engine und wechselt nie von
   *  selbst. Der Server verwirft die alten Auftraege und legt sie neu an. */
  it('stoesst eine Partie neu an und uebernimmt den zurueckgemeldeten Stand', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis({ id: 5, status: 'failed', analyzedPlies: 30 })]);

    fixture.componentInstance.restart(fixture.componentInstance.analyses[0]);
    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/game-analyses/5/restart');
    req.flush(analysis({ id: 5, status: 'pending', analyzedPlies: 30, lastError: null }));

    expect(fixture.componentInstance.analyses[0].status).toBe('pending');
    expect(fixture.componentInstance.restarting).toBeNull();
  });

  it('gibt den Knopf nach einem Fehlschlag wieder frei', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis({ id: 5 })]);

    fixture.componentInstance.restart(fixture.componentInstance.analyses[0]);
    http.expectOne('/api/game-analyses/5/restart')
      .flush({ message: 'nope' }, { status: 500, statusText: 'Server Error' });

    expect(fixture.componentInstance.restarting).toBeNull();
  });

  /** Das Tempo kommt aus der HISTORIE: es steht mit dem ersten Abruf da, statt erst nach einer
   *  Minute offener Seite — und ueberlebt damit einen geschlossenen Reiter. */
  it('zeigt das Tempo sofort, ohne eigene Proben', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis({ analyzedPlies: 10, plyCount: 40 })]);
    drainThroughput(2.5, 90);

    expect(fixture.componentInstance.rate).toEqual({ perMinute: '2.5', eta: '1 h 30 min' });
  });

  it('zeigt kein Tempo, wenn der Server keines kennt', () => {
    const fixture = TestBed.createComponent(GameAnalysesComponent);
    fixture.detectChanges();
    http.expectOne('/api/game-analyses').flush([analysis()]);
    drainThroughput(0);

    expect(fixture.componentInstance.rate).toBeNull();
  });
});
