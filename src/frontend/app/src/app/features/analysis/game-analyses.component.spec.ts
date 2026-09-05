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
    plyCount: 40, analyzedPlies: 10, lastError: null,
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

  afterEach(() => http.verify());

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
});
