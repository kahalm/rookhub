import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubPreview, SideMatch } from '../../core/club.models';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ImportReview } from './import-review';

const M = (x: Partial<SideMatch> = {}): SideMatch =>
  ({ league: false, ambiguous: false, name: null, fide: null, club: false, candidates: [], ...x });
const PREVIEW: ClubPreview = { truncated: false, games: [
  { index: 1, year: 2024, result: '1-0', event: 'Landesliga Tirol', plies: 40, opening: '1.e4 c5', error: null, duplicate: false,
    white: { raw: 'Binder M', elo: null, match: M({ league: true, name: 'Binder, Moriz', fide: '111', club: true }), owner: false, replace: true },
    black: { raw: 'Hengl P', elo: null, match: M({ league: true, name: 'Hengl, Philip', fide: '222' }), owner: false, replace: false } },
  { index: 2, year: 2023, result: '0-1', event: null, plies: 30, opening: '1.d4', error: null, duplicate: false,
    white: { raw: 'Huber, F.', elo: null, match: M({ league: true, ambiguous: true, candidates: [
      { name: 'Huber, Franz', fide: '1', teams: ['Absam'], club: false }, { name: 'Huber, Florian', fide: '2', teams: ['Hall'], club: false }] }), owner: false, replace: false },
    black: { raw: 'Unbekannt', elo: null, match: M(), owner: false, replace: false } },
  { index: 3, year: 2025, result: '1-0', event: null, plies: 40, opening: '1.e4', error: null, duplicate: false,
    white: { raw: 'Oberschmid, Patrik', elo: null, match: M({ league: true, name: 'Oberschmid, Patrik', fide: '900', club: true }), owner: true, replace: true },
    black: { raw: 'Bodrov, Timofey', elo: null, match: M({ mega: true, name: 'Bodrov, Timofey', fide: '14131781' }), owner: false, replace: false } },
] };

describe('ClubImportReviewComponent', () => {
  let fixture: ComponentFixture<ClubImportReviewComponent>;
  let client: jasmine.SpyObj<ClubClient>;

  function create(): HTMLElement {
    client = jasmine.createSpyObj<ClubClient>('ClubClient', ['importPgn', 'players', 'match']);
    client.players.and.resolveTo([]);
    TestBed.configureTestingModule({ imports: [ClubImportReviewComponent] });
    fixture = TestBed.createComponent(ClubImportReviewComponent);
    fixture.componentRef.setInput('review', new ImportReview(PREVIEW, true));
    fixture.componentRef.setInput('client', client);
    fixture.componentRef.setInput('pgn', 'PGN');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt wer gegen wen, Schwaz ersetzt, was importiert wird', () => {
    const el = create();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Schwaz');
    expect(rows[0].textContent).toContain('Schwaz-Spieler');
    expect(rows[0].textContent).toContain('Hengl, Philip');
    expect(rows[0].textContent).toContain('im PGN: Hengl P');
    expect(rows[0].textContent).toContain('wird importiert');
    expect(rows[0].querySelector('td.event')?.textContent?.trim()).toBe('Landesliga Tirol');
    expect(rows[1].querySelector('td.event')?.textContent?.trim()).toBe('');
    expect(rows[1].textContent).toContain('mehrdeutig');
    expect(rows[1].textContent).toContain('nicht erkannt');
    expect(el.querySelector('.review-head')?.textContent).toContain('3 Partien gelesen — 2 werden importiert');
    expect(rows[2].textContent).toContain('nicht in Liga');
    expect(rows[2].textContent).toContain('nicht in Liga — anhaken zum Hinzufügen');
    expect((rows[2].querySelector('input[type=checkbox]') as HTMLInputElement).disabled).toBeFalse();
    expect(el.querySelector('.review-head')?.textContent).toContain('1 ohne Gegner aus der Liga');
    const filters = Array.from(el.querySelectorAll('.seg button')).map(b => b.textContent!.trim());
    expect(filters).toEqual(['Alle (3)', 'Nicht importiert (1)', 'Noch nicht vorhanden (3)', 'Nicht erkannt (1)']);
    (el.querySelectorAll('.seg button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('am Handy (390 px) je Partie untereinander statt links-rechts scrollen', () => {
    const el = create();
    el.style.display = 'block';
    el.style.width = '390px';
    const box = el.querySelector('.review-scroll') as HTMLElement;
    expect(box.scrollWidth).toBeLessThanOrEqual(box.clientWidth);
    const row = el.querySelector('tbody tr') as HTMLElement;
    const white = row.querySelector('td.side.white') as HTMLElement, black = row.querySelector('td.side.black') as HTMLElement;
    expect(black.getBoundingClientRect().top).toBeGreaterThan(white.getBoundingClientRect().top);   // untereinander
    expect(getComputedStyle(el.querySelector('thead') as HTMLElement).display).toBe('none');
    el.style.width = '1000px';
    expect(getComputedStyle(el.querySelector('thead') as HTMLElement).display).not.toBe('none');     // breit: Tabelle
  });

  it('das Namensfeld sucht beim Öffnen gleich (Megabase mit), ein Treffer von dort wählt die Partie zum Import', fakeAsync(() => {
    const el = create();
    client.players.and.resolveTo([{ name: 'Hengl, Peter', fide: '777', teams: [], club: false, league: false, source: 'mega' }]);
    ((el.querySelectorAll('tbody tr')[1].querySelectorAll('button.side-btn'))[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    flush();
    fixture.detectChanges();
    expect(client.players).toHaveBeenCalledWith('Unbekannt', true);
    (el.querySelector('.psearch-results button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const row = el.querySelectorAll('tbody tr')[1];
    expect(row.textContent).toContain('Hengl, Peter');
    expect(row.textContent).toContain('nicht in Liga');
    expect(row.textContent).toContain('wird importiert');
    flush();
  }));

  it('einen mehrdeutigen Spieler aus den Kandidaten wählen, dann importieren', fakeAsync(() => {
    const el = create();
    const sideButtons = el.querySelectorAll('tbody tr')[1].querySelectorAll('button.side-btn');
    (sideButtons[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    const cand = Array.from(el.querySelectorAll('.cands-pick button')).find(b => b.textContent!.includes('Huber, Franz')) as HTMLButtonElement;
    cand.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('tbody tr')[1].textContent).toContain('Huber, Franz');
    client.importPgn.and.resolveTo({ added: 2, anonymized: 1, duplicates: 0, truncated: false, ids: [], failed: [] });
    let done = false;
    fixture.componentInstance.imported.subscribe(() => done = true);
    (el.querySelector('.actions .btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    expect(client.importPgn).toHaveBeenCalledWith('PGN', [
      { index: 1, white: { name: null, fide: '111', replace: true }, black: { name: null, fide: '222', replace: false } },
      { index: 2, white: { name: 'Huber, Franz', fide: '1', replace: false }, black: { name: null, fide: null, replace: false } },
    ]);
    expect(done).toBeTrue();
    flush();
  }));

  /** 0.590.0: zwölf Partien mit eigenem Text → zwei Portionen (10 + 2), jede sofort gespeichert. */
  function createPortioned(): HTMLElement {
    const g = PREVIEW.games[0];
    const games = Array.from({ length: 12 }, (_, i) => ({ ...g, index: i + 1, pgn: `P${i + 1}` }));
    client = jasmine.createSpyObj<ClubClient>('ClubClient', ['importPgn', 'players', 'match']);
    client.players.and.resolveTo([]);
    TestBed.configureTestingModule({ imports: [ClubImportReviewComponent] });
    fixture = TestBed.createComponent(ClubImportReviewComponent);
    fixture.componentRef.setInput('review', new ImportReview({ truncated: false, games }, true));
    fixture.componentRef.setInput('client', client);
    fixture.componentRef.setInput('pgn', 'GANZE DATEI');
    fixture.componentInstance.retryDelays = [0, 0];
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }
  const ok = (added: number, failed: { index: number }[] = []) => ({ added, anonymized: added, duplicates: 0, truncated: false, ids: [],
    failed: failed.map(f => ({ ...f, white: null, black: null, reason: 'onlyOwnClub' })) });

  it('importiert Portion für Portion — je Portion nur deren Text, Nummern ab 1, Ergebnis zusammengezählt', async () => {
    const el = createPortioned();
    client.importPgn.and.returnValues(Promise.resolve(ok(10)), Promise.resolve(ok(1, [{ index: 2 }])));
    let result: any = null;
    fixture.componentInstance.imported.subscribe(r => result = r);
    await fixture.componentInstance.run();
    expect(client.importPgn).toHaveBeenCalledTimes(2);
    const [text1, dec1] = client.importPgn.calls.argsFor(0);
    expect(text1).toBe(Array.from({ length: 10 }, (_, i) => `P${i + 1}`).join('\n'));
    expect(dec1.map(d => d.index)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    expect(client.importPgn.calls.argsFor(1)[0]).toBe('P11\nP12');
    expect(result.added).toBe(11);
    expect(result.failed.map((f: { index: number }) => f.index)).toEqual([12]);   // zurück auf die Nummer der Übersicht
    expect(el.textContent).not.toContain('GANZE DATEI');
  });

  it('reißt die Verbindung ab: Gespeichertes bleibt, „Weiter importieren" schickt nur den Rest', async () => {
    const el = createPortioned();
    const gone = new HttpErrorResponse({ status: 0 });
    client.importPgn.and.returnValues(Promise.resolve(ok(10)), Promise.reject(gone), Promise.reject(gone), Promise.reject(gone));
    let result: any = null;
    fixture.componentInstance.imported.subscribe(r => result = r);
    await fixture.componentInstance.run();
    fixture.detectChanges();
    expect(client.importPgn).toHaveBeenCalledTimes(4);                               // Portion 2 dreimal versucht
    expect(el.querySelector('.update-msg')!.textContent).toContain('10 Partien sind gespeichert');
    expect(el.querySelector('.actions .btn-pri')!.textContent).toContain('Weiter importieren (2 übrig)');
    expect(result).toBeNull();

    client.importPgn.calls.reset();
    client.importPgn.and.resolveTo(ok(2));
    await fixture.componentInstance.run();
    expect(client.importPgn).toHaveBeenCalledOnceWith('P11\nP12', jasmine.any(Array));
    expect(result.added).toBe(12);                                                   // beide Läufe zusammen
  });

  it('eine Absage (400) wird nicht wiederholt', async () => {
    createPortioned();
    client.importPgn.and.returnValue(Promise.reject(new HttpErrorResponse({ status: 400 })));
    await fixture.componentInstance.run();
    expect(client.importPgn).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.error()).toBe('Importieren hat nicht geklappt.');
  });

  it('getippter Name wird abgeglichen', fakeAsync(() => {
    const el = create();
    client.match.and.resolveTo({ white: M({ league: true, name: 'Hengl, Philip', fide: '222' }), black: M() });
    ((el.querySelectorAll('tbody tr')[1].querySelectorAll('button.side-btn'))[1] as HTMLButtonElement).click();
    fixture.componentInstance.typed('Hengl Philip');
    void fixture.componentInstance.apply();
    flushMicrotasks();
    fixture.detectChanges();
    expect(client.match).toHaveBeenCalledWith('Hengl Philip', '');
    expect(el.querySelectorAll('tbody tr')[1].textContent).toContain('Ligaspieler');
    flush();
  }));
});
