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

  // Codereview F4-012: die beiden Umschalter trugen feste englische Vorlesetexte („filter“, „side“).
  it('benennt Filter- und Seiten-Umschalter übersetzt statt mit festem Englisch', () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([analysis({ id: 2, status: 'done', analyzedPlies: 40, annotated: true, isPublic: true })]);
    http.expectOne('/api/game-analyses').flush([analysis({ id: 3, status: 'done', analyzedPlies: 40 })]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    http.expectOne('/api/guess-sessions').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-button-toggle-group.small-toggle')?.getAttribute('aria-label')).toBe('guess.filterLabel');

    fixture.componentInstance.tab = 1;
    fixture.detectChanges();
    fixture.detectChanges();
    expect(el.querySelector('.side-pick mat-button-toggle-group')?.getAttribute('aria-label')).toBe('guess.sideLabel');
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

  /** d2-rk-guess-durchlaeufe-unten: ein angefangener Durchlauf stand erst am Seitenende (2–3 Bildschirme tief). Er steht
   *  jetzt als „Weiterspielen" über dem Suchfeld; beendete bleiben unten unter „Deine Durchläufe". */
  it('zeigt laufende Durchläufe als „Weiterspielen" über der Suche, beendete unten', () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush([]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    const run = (id: number, status: 'running' | 'done') =>
      ({ id, title: 'Lauf ' + id, guessWhite: true, status, points: 3, maxPoints: 10 } as GuessSession);
    http.expectOne('/api/guess-sessions').flush([run(4, 'running'), run(5, 'done')]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const resume = el.querySelectorAll('mat-card.resume');
    expect(resume.length).toBe(1);
    expect(resume[0].textContent).toContain('Lauf 4');
    expect(resume[0].querySelector('a.resume-btn')?.getAttribute('href')).toBe('/guess/4');
    // vor dem Suchfeld
    const search = el.querySelector('mat-form-field.search')!;
    expect(resume[0].compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const below = Array.from(el.querySelectorAll('mat-card.run:not(.resume)')).map(e => e.textContent);
    expect(below.length).toBe(1);
    expect(below[0]).toContain('Lauf 5');
  });

  /** z1-rk-guess-einzeltab: ohne Konto gibt es nur die Meisterpartien — keine Reiterleiste mit einem Reiter; die Anzahl
   *  steht in der Filterzeile, der Konto-Hinweis im Untertitel. */
  it('ohne Konto: keine Reiterleiste, Anzahl in der Filterzeile, Hinweis im Untertitel', () => {
    const fixture = setup(false);
    http.expectOne('/api/game-analyses/public').flush([
      analysis({ id: 1, isPublic: true, analyzedPlies: 40, status: 'done' }),
      analysis({ id: 2, isPublic: true, analyzedPlies: 40, status: 'done' }),
    ]);
    http.expectOne(r => r.url === '/api/guess-sessions/anonymous').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-tab-group')).toBeNull();
    expect(el.querySelectorAll('.start-card .game-row').length).toBe(2);
    expect(el.querySelector('.sec-head .count')?.textContent).toContain('guess.gamesCount');
    expect(el.querySelector('p.intro .anon-hint')?.textContent).toContain('guess.anonHint');
    // z2-rk-guess-anon-leerzeichen: zwischen den beiden Sätzen steht ein Leerzeichen.
    expect(el.querySelector('p.intro')!.textContent!.replace(/\s+/g, ' ')).toContain('guess.intro guess.anonHint');
    expect(el.querySelectorAll('p.small').length).withContext('keine eigene Hinweiszeile mehr').toBe(0);
  });

  /** z1-rk-guess-vorlauf: Suche, Filter und Sortieren (⇅ mit Menü) in EINER Zeile über der Liste; mit Konto in den Reitern. */
  it('legt Suche und Sortieren in die Filterzeile der Liste; ⇅ öffnet das Sortiermenü', async () => {
    const fixture = setup();
    flushInitial();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-tab-group')).not.toBeNull();
    const head = el.querySelector('.start-card .sec-head')!;
    expect(head.querySelector('mat-form-field.search')).not.toBeNull();
    const sortBtn = head.querySelector<HTMLButtonElement>('button.sort-btn')!;
    expect(sortBtn).not.toBeNull();
    sortBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const items = Array.from(document.querySelectorAll<HTMLButtonElement>('.sort-option'));
    expect(items.length).toBe(4);
    items[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.sort).toBe('short');
    expect(sortBtn.classList).toContain('active');
  });

  /** z2-rk-guess-vorlauf: am Handy steht „Alle/Kommentierte" als Häkchen „Nur kommentierte" im ⇅-Menü, damit Suchfeld und
   *  ⇅ eine Zeile bleiben. */
  it('bietet „Nur kommentierte" im ⇅-Menü an und schaltet damit den Filter', async () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([
      analysis({ id: 1, status: 'done', analyzedPlies: 40, annotated: true, isPublic: true }),
      analysis({ id: 2, status: 'done', analyzedPlies: 40, annotated: false, isPublic: true }),
    ]);
    http.expectOne('/api/game-analyses').flush([]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    http.expectOne('/api/guess-sessions').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('.start-card .sec-head button.sort-btn')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const item = document.querySelector<HTMLButtonElement>('.annotated-option')!;
    expect(item).not.toBeNull();
    expect(item.getAttribute('aria-checked')).toBe('false');
    item.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.annotatedOnly).toBeTrue();
    expect(fixture.componentInstance.curatedShown.length).toBe(1);
    http.expectOne(r => r.method === 'PUT' && r.url === '/api/view-state/guess.list').flush({});
  });

  /** z1-rk-guess-vorlauf: die Weiterspielen-Karte ist EINE Zeile — Titel + Meta links, ▶ und Löschen rechts, ohne Umbruch. */
  it('verdichtet „Weiterspielen" zu einer Zeile mit Titel, Meta, ▶ und Löschen', () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush([]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    http.expectOne('/api/guess-sessions').flush([{ id: 4, title: 'Lauf 4', guessWhite: true, status: 'running', points: 3, maxPoints: 10 } as GuessSession]);
    fixture.detectChanges();
    const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('mat-card.resume .resume-row')!;
    expect(getComputedStyle(row).flexWrap).toBe('nowrap');
    const kinds = Array.from(row.children).map(c => c.classList.contains('resume-main') ? 'main' : c.tagName.toLowerCase());
    expect(kinds).toEqual(['main', 'a', 'button']);
    expect(row.querySelector('.resume-main .resume-meta')?.textContent).toContain('guess.score');
    expect(row.querySelector('a.resume-btn')?.getAttribute('aria-label')).toBe('guess.resume');
  });

  // d3-rk-punktepartie-null-von-null: „Weiß · 0 von 0 Punkten“ vor dem ersten Zug — die Punkte fallen dann weg.
  it('lässt in „Weiterspielen" die Punkte weg, solange es keine zu holen gab', () => {
    const fixture = setup();
    http.expectOne('/api/view-state/guess.list').flush({ annotatedOnly: false });
    http.expectOne('/api/game-analyses/public').flush([]);
    http.expectOne('/api/game-analyses').flush([]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    http.expectOne('/api/guess-sessions').flush([{ id: 4, title: 'Lauf 4', guessWhite: true, status: 'running', points: 0, maxPoints: 0 } as GuessSession]);
    fixture.detectChanges();
    const meta = (fixture.nativeElement as HTMLElement).querySelector('mat-card.resume .resume-meta')!;
    expect(meta.textContent).toContain('guess.white');
    expect(meta.textContent).not.toContain('guess.score');
    expect(meta.querySelector('.pts')).toBeNull();
  });

  /** d2-rk-guess-vorlauf: Erklärtext ein Satz, Rest und „Seite des Gewinners" hinter dem ⓘ am Titel. */
  it('hält den Erklärtext kurz: Rest und Gewinner-Hinweis hinter dem ⓘ', () => {
    const fixture = setup();
    flushInitial();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('h1 app-help-hint button')?.getAttribute('aria-label') ?? '';
    expect(hint).toContain('guess.introMore');
    expect(hint).toContain('guess.curatedHint');
    // z2-rk-guess-vorlauf: angemeldet kein offener Untertitel mehr — der ⓘ-Text beginnt mit ihm.
    expect(hint).toContain('guess.intro guess.introMore');
    expect(el.querySelector('p.intro')).withContext('Untertitel nur ohne Konto').toBeNull();
    expect(el.textContent!.split('guess.curatedHint').length).withContext('nicht mehr als eigene Zeile').toBe(1);
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

/** a1-rk-guess-tablet-spielen: ab 600 px festes Raster, „Spielen“ rechts über Titel- und Metazeile (nie allein in einer Zeile). */
describe('GuessListComponent Zeilenraster', () => {
  const css = ((GuessListComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s+/g, '')
    .replace(/\[_ngcontent-%COMP%\]/g, '');
  const phoneAt = css.indexOf('@media(max-width:599.98px)');
  const base = css.slice(0, phoneAt);
  it('außerhalb der Handy-Stufe: Raster „title play“ / „meta play“, kein Flex-Spacer', () => {
    expect(phoneAt).toBeGreaterThan(-1);
    expect(base).toMatch(/\.game-row\{display:grid;grid-template-columns:minmax\(0,1fr\)auto;grid-template-areas:["']titleplay["']["']metaplay["']/);
    expect(base).toContain('.game-row.g-play{grid-area:play;}');
    expect(base).toContain('.game-row.spacer{display:none;}');
  });
  it('am Handy bleibt die Metazeile unter Titel und Knopf über die volle Breite', () => {
    expect(css.slice(phoneAt)).toMatch(/grid-template-areas:["']titleplay["']["']metameta["']/);
  });
  // Falz-Runde 16.10.: Laptop 41 % Vorlauf → engere Abstände über dem Kopf und um die Suchzeile
  it('Kopf 12 px unter der Leiste, Suchzeile mit 4 px Abstand', () => {
    expect(css).toContain('.gl-container{max-width:min(var(--page-max-width),96vw);margin:12pxauto16px;');
    expect(css).toMatch(/\.sec-head\{[^}]*margin-bottom:4px/);
    expect(css).toMatch(/\.mat-mdc-tab-body-content\{padding-top:4px/);
  });
});
