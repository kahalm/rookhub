import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { GuessListComponent } from './guess-list.component';
import { GuessSession } from './guess.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { AuthService } from '../../core/auth.service';

function analysis(over: Partial<GameAnalysis> = {}): GameAnalysis {
  return {
    id: 1, title: 'A – B', white: 'A', black: 'B', result: '1-0', event: null,
    targetDepth: 20, multiPv: 5, engineId: 'eei_x', status: 'running',
    plyCount: 40, analyzedPlies: 0, lastError: null, isPublic: false, annotated: false,
    createdAt: '2026-09-10T10:00:00Z', finishedAt: null, ...over,
  };
}

/**
 * Die Punktepartie-Übersicht mit dem EINWURF: eine eigene Partie hineinwerfen, ohne Tiefe und ohne
 * Linienzahl — beides setzt der Server. Geprüft wird deshalb vor allem, was NICHT mitgeschickt wird.
 */
describe('GuessListComponent', () => {
  let http: HttpTestingController;

  /** Angemeldet: nur dann gibt es eigene Analysen und den Einwurf. */
  function setup(loggedIn = true) {
    TestBed.configureTestingModule({
      imports: [GuessListComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(GuessListComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  /** Ohne diesen Abruf wüsste die Seite nicht, ob überhaupt eine Engine bereitsteht. Der
   *  Filter-Zustand (`view-state`) hängt am Konto und kommt bei jedem Aufbau mit. */
  function flushInitial(own: GameAnalysis[] = [], status: Partial<{ engineAvailable: boolean; ownEngine: boolean; openGames: number; maxGames: number }> = {}) {
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush(own);
    http.expectOne('/api/game-analyses/guess/status').flush({
      engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5, ...status,
    });
    http.expectOne('/api/guess-sessions').flush([]);
  }

  it('wirft ein PGN OHNE Tiefe und Linienzahl ein — die setzt der Server', () => {
    const fixture = setup();
    flushInitial();

    fixture.componentInstance.pgn = '1. e4 e5';
    fixture.componentInstance.upload();

    const req = http.expectOne(r => r.method === 'POST' && r.url === '/api/game-analyses/guess');
    expect(req.request.body.pgn).toBe('1. e4 e5');
    // Die eiserne Regel dieses Wegs: kein Regler, also auch kein Feld.
    expect(req.request.body.targetDepth).toBeUndefined();
    expect(req.request.body.multiPv).toBeUndefined();
    req.flush(analysis({ id: 7 }));

    // Danach frischt die Seite Liste und Kontingent auf.
    http.expectOne('/api/game-analyses').flush([analysis({ id: 7 })]);
    http.expectOne('/api/game-analyses/guess/status').flush({
      engineAvailable: true, ownEngine: false, openGames: 1, maxGames: 5,
    });
    expect(fixture.componentInstance.pgn).toBe('');
    expect(fixture.componentInstance.uploading).toBeFalse();
  });

  it('behält das PGN, wenn der Server absagt', () => {
    const fixture = setup();
    flushInitial();

    fixture.componentInstance.pgn = 'kein PGN';
    fixture.componentInstance.upload();
    http.expectOne('/api/game-analyses/guess')
      .flush({ reason: 'invalid-pgn' }, { status: 400, statusText: 'Bad Request' });

    // Eingabe stehen lassen: der Nutzer soll sie korrigieren können, nicht neu suchen.
    expect(fixture.componentInstance.pgn).toBe('kein PGN');
    expect(fixture.componentInstance.uploading).toBeFalse();
  });

  /** Eine gerade eingeworfene Partie hat NULL gerechnete Stellungen. Stünde sie deshalb nicht in
   *  der Liste, hielte der Nutzer den Einwurf für verloren und wiederholte ihn. */
  it('zeigt auch die noch rechnende Partie, aber ungespielt', () => {
    const fixture = setup();
    flushInitial([analysis({ id: 3, analyzedPlies: 0, status: 'pending' })]);

    expect(fixture.componentInstance.ownGames.length).toBe(1);
    expect(fixture.componentInstance.percent(fixture.componentInstance.ownGames[0])).toBe(0);

    fixture.componentInstance.ownGames = [analysis({ analyzedPlies: 10, plyCount: 40 })];
    expect(fixture.componentInstance.percent(fixture.componentInstance.ownGames[0])).toBe(25);
  });

  it('fragt ohne Anmeldung weder eigene Partien noch das Kontingent ab', () => {
    setup(false);
    http.expectOne('/api/game-analyses/public').flush([]);
    // Ohne Konto laeuft alles ueber die anonyme Sitzung — und der Einwurf gar nicht.
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    http.expectNone('/api/game-analyses/guess/status');
    http.expectNone('/api/game-analyses');
  });

  /** 655 Meisterpartien auf einmal waren am Handy 85 000 px (Codereview W5 UX-016): die Liste kommt
   *  seitenweise, und eine neue Suche beginnt wieder auf Seite eins. */
  it('rendert den Bestand seitenweise und setzt die Seite bei neuer Suche zurueck', () => {
    const fixture = setup(false);
    const games = Array.from({ length: 120 }, (_, i) =>
      analysis({ id: i + 1, title: `Partie ${i + 1}`, isPublic: true, analyzedPlies: 40, status: 'done' }));
    http.expectOne('/api/game-analyses/public').flush(games);
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const rows = () => el.querySelectorAll('.start-card .game-row').length;
    const more = () => el.querySelector<HTMLButtonElement>('button.more');

    expect(rows()).toBe(GuessListComponent.PageSize);
    expect(more()?.textContent).toContain('guess.showMore');
    more()!.click();
    fixture.detectChanges();
    expect(rows()).toBe(100);
    more()!.click();
    fixture.detectChanges();
    expect(rows()).toBe(120);
    expect(more()).toBeNull();

    // Andere Liste = wieder Seite eins (sonst stuenden nach einer Suche ploetzlich 120 Zeilen da).
    fixture.componentInstance.setQuery('Partie');
    fixture.detectChanges();
    expect(rows()).toBe(GuessListComponent.PageSize);
    // Der Reiter zaehlt weiterhin ALLE Treffer, nicht nur die gerenderten.
    expect(fixture.componentInstance.curatedShown.length).toBe(120);
  });

  /** Am Handy sprang „Spielen" je nach Zeile zwischen rechts und links unten — die Chips standen lose
   *  neben dem Knopf. Jetzt traegt jede Zeile genau Titel, EINE Metazeile und den Knopf. */
  it('fasst Zugzahl, Seite und Chips in einer Metazeile zusammen — der Knopf steht daneben', () => {
    const fixture = setup(false);
    http.expectOne('/api/game-analyses/public').flush([
      analysis({ id: 1, isPublic: true, annotated: true, guessWhite: true, analyzedPlies: 40, status: 'done' }),
      analysis({ id: 2, isPublic: true, annotated: false, guessWhite: false, analyzedPlies: 40, status: 'done' }),
    ]);
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    fixture.detectChanges();
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.start-card .game-row'));
    expect(rows.length).toBe(2);
    for (const row of rows) {
      const kinds = Array.from(row.children)
        .filter(c => !c.classList.contains('spacer'))
        .map(c => c.classList.contains('g-title') ? 'title' : c.classList.contains('g-meta') ? 'meta' : c.tagName.toLowerCase());
      expect(kinds).toEqual(['title', 'meta', 'button']);
      expect(row.querySelector('.g-meta button')).toBeNull();
    }
    expect(rows[0].querySelector('.g-meta .chip')).not.toBeNull();
  });

  it('sortiert den Bestand nach Laenge und Turnier — ohne Turnier ans Ende', () => {
    const fixture = setup(false);
    http.expectOne('/api/game-analyses/public').flush([
      analysis({ id: 1, plyCount: 80, event: 'Wien 1873', isPublic: true }),
      analysis({ id: 2, plyCount: 20, event: null, isPublic: true }),
      analysis({ id: 3, plyCount: 50, event: 'Berlin 1881', isPublic: true }),
    ]);
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    const c = fixture.componentInstance;
    const ids = () => c.curatedShown.map(g => g.id);

    expect(ids()).toEqual([1, 2, 3]);           // Vorgabe: Reihenfolge des Servers
    c.setSort('short');
    expect(ids()).toEqual([2, 3, 1]);
    c.setSort('long');
    expect(ids()).toEqual([1, 3, 2]);
    c.setSort('event');
    expect(ids()).toEqual([3, 1, 2]);
    c.setSort('title');
    expect(ids()).toEqual([1, 2, 3]);
  });

  /** Der Muelleimer an „Deine Durchlaeufe" loeschte einen Lauf samt Punkten mit einem Klick (Codereview
   *  W5 F4-013). Jetzt erst nach Ja — ueber den ConfirmService, nicht window.confirm. */
  it('loescht einen Durchlauf erst nach Rueckfrage — abgelehnt geht kein DELETE raus', () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush([]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    const run = (id: number, title: string | null) => ({ id, title, guessWhite: true, status: 'done', points: 3, maxPoints: 10 } as GuessSession);
    http.expectOne('/api/guess-sessions').flush([run(4, 'A – B'), run(5, null)]);
    const c = fixture.componentInstance;
    const native = spyOn(window, 'confirm').and.returnValue(true);
    const ask = spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(false));

    c.remove(c.sessions[0]);
    expect(ask).toHaveBeenCalledWith('guess.deleteConfirm', { title: 'A – B' });
    http.expectNone(r => r.method === 'DELETE');
    expect(c.sessions.length).toBe(2);

    // Ohne Titel nennt die Frage „Ohne Titel" wie die Liste.
    c.remove(c.sessions[1]);
    expect(ask).toHaveBeenCalledWith('guess.deleteConfirm', { title: 'guess.untitled' });
    http.expectNone(r => r.method === 'DELETE');

    ask.and.returnValue(of(true));
    c.remove(c.sessions[0]);
    http.expectOne({ method: 'DELETE', url: '/api/guess-sessions/4' }).flush(null);
    expect(c.sessions.map(x => x.id)).toEqual([5]);
    expect(native).not.toHaveBeenCalled();
  });
});
