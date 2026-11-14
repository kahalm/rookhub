import { provideTestClub } from '../../core/club-context.testing';
import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubImportResult, ClubPreview, SideMatch } from '../../core/club.models';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ImportReview } from './import-review';
import { AUTO_MINE_KEY } from '../../core/auto-my-games';

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

  function create(preview: ClubPreview = PREVIEW): HTMLElement {
    client = jasmine.createSpyObj<ClubClient>('ClubClient', ['importPgn', 'players', 'match']);
    client.players.and.resolveTo([]);
    TestBed.configureTestingModule({ imports: [ClubImportReviewComponent], providers: [provideTestClub()] });
    fixture = TestBed.createComponent(ClubImportReviewComponent);
    fixture.componentRef.setInput('review', new ImportReview(preview, true));
    fixture.componentRef.setInput('client', client);
    fixture.componentRef.setInput('pgn', 'PGN');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('„Meintest du …": ein ähnlicher Ligaspieler mit einem Klick in der Zeile, ohne das Feld zu öffnen (0.596.0)', () => {
    const philip = { name: 'Hengl, Philip', fide: '222', teams: ['Absam'], club: false };
    const typo = { raw: 'Hengl, Phillip', elo: null, match: M({ similar: [philip] }), owner: false, replace: false };
    const g = PREVIEW.games[0];
    const el = create({ truncated: false, games: [
      { ...g, index: 1, black: typo },
      { ...g, index: 2, black: { ...typo } },
    ] });
    const chips = () => Array.from(el.querySelectorAll('.quick-pick button')) as HTMLButtonElement[];
    expect(chips().map(b => b.textContent!.trim())).toEqual(['Hengl, Philip', 'Hengl, Philip']);
    expect(chips()[0].getAttribute('title')).toBe('Absam');
    chips()[0].click();
    fixture.detectChanges();
    expect(el.querySelector('.side-edit')).toBeNull();                                // nichts aufgeklappt
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Hengl, Philip');
    expect(rows[0].textContent).toContain('im PGN: Hengl, Phillip');
    expect(rows[1].textContent).toContain('Hengl, Philip');                          // gleicher PGN-Name: mitgezogen
    expect(chips().length).toBe(0);
    expect(el.textContent).toContain('1 weitere Seite mit „Hengl, Phillip“ ebenso zugeordnet.');
  });

  it('zeigt wer gegen wen, Testdorf ersetzt, was importiert wird', () => {
    const el = create();
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Testdorf');
    expect(rows[0].textContent).toContain('Testdorf-Spieler');
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
      { index: 1, white: { name: null, fide: '111', replace: true }, black: { name: null, fide: '222', replace: false }, leagueGameId: 0 },
      { index: 2, white: { name: 'Huber, Franz', fide: '1', replace: false }, black: { name: null, fide: null, replace: false }, leagueGameId: 0 },
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
    TestBed.configureTestingModule({ imports: [ClubImportReviewComponent], providers: [provideTestClub()] });
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

    // G3-010: gespeicherte Zeilen sind gekennzeichnet und gesperrt — eine Änderung daran würde nicht mehr gesendet.
    const rows = Array.from(el.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.edit-row)'));
    expect(rows[0].querySelector('.st')!.textContent).toContain('gespeichert');
    expect(rows[0].querySelector<HTMLInputElement>('td.chk input')!.disabled).toBeTrue();
    expect(rows[0].querySelector<HTMLButtonElement>('.side-btn')!.disabled).toBeTrue();
    expect(rows[10].querySelector('.st')!.textContent).toContain('wird importiert');
    expect(rows[10].querySelector<HTMLInputElement>('td.chk input')!.disabled).toBeFalse();

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

  describe('„Automatisch zu meinen Partien hinzufügen“ (0.970.0, dieselbe Regel wie die Formular-Prüfseite)', () => {
    let toMine: jasmine.Spy;
    afterEach(() => localStorage.removeItem(AUTO_MINE_KEY));

    /** `n` Partien mit eigenem Text; `mine` = die Seite reicht die Ablage durch (eigene Liste eines Kontos). */
    function createSmall(n: number, mine = true): HTMLElement {
      const g = PREVIEW.games[0];
      const games = Array.from({ length: n }, (_, i) => ({ ...g, index: i + 1, pgn: `[White "Binder M"]\n[Black "Hengl P"]\n\n1. e4 e5 {${i + 1}} *\n` }));
      client = jasmine.createSpyObj<ClubClient>('ClubClient', ['importPgn', 'players', 'match']);
      client.players.and.resolveTo([]);
      toMine = jasmine.createSpy('toMyGames').and.callFake(async (pgn: string) => {
        const k = pgn.split('[White ').length - 1;
        return { imported: k, duplicates: 0, ids: Array.from({ length: k }, (_, i) => 100 + i) };
      });
      TestBed.configureTestingModule({ imports: [ClubImportReviewComponent], providers: [provideTestClub()] });
      fixture = TestBed.createComponent(ClubImportReviewComponent);
      fixture.componentRef.setInput('review', new ImportReview({ truncated: false, games }, true));
      fixture.componentRef.setInput('client', client);
      fixture.componentRef.setInput('pgn', 'GANZE DATEI');
      if (mine) fixture.componentRef.setInput('toMyGames', toMine);
      fixture.componentInstance.retryDelays = [0, 0];
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }
    async function run(): Promise<ClubImportResult> {
      let result: ClubImportResult | null = null;
      fixture.componentInstance.imported.subscribe(r => result = r);
      await fixture.componentInstance.run();
      return result!;
    }

    it('eine Partie: nach dem Speichern auch in „Meine Partien“, mit den Namen der Liste statt „Testdorf“', async () => {
      const el = createSmall(1);
      expect(el.querySelector<HTMLInputElement>('.auto-mine input')!.checked).toBeTrue();
      client.importPgn.and.resolveTo(ok(1));
      const result = await run();
      expect(client.importPgn).toHaveBeenCalledBefore(toMine);
      expect(toMine).toHaveBeenCalledOnceWith('[White "Binder M"]\n[Black "Hengl P"]\n\n1. e4 e5 {1} *\n');
      expect(result.mine).toEqual({ added: 1, present: 0, missing: 0 });
    });

    it('ein in der Übersicht korrigierter Name geht in die Kopie', async () => {
      createSmall(1);
      fixture.componentInstance.review.choosePerson(1, 'black', { name: 'Hengl, Philip', fide: '222', teams: [], club: false });
      client.importPgn.and.resolveTo(ok(1));
      await run();
      expect(toMine.calls.argsFor(0)[0]).toContain('[White "Binder M"]\n[Black "Hengl, Philip"]');
    });

    it('bis fünf Partien gehen mit — ohne die vom Verein abgelehnten', async () => {
      createSmall(5);
      client.importPgn.and.resolveTo(ok(4, [{ index: 3 }]));
      const result = await run();
      const sent: string = toMine.calls.argsFor(0)[0];
      expect(sent.split('[White ').length - 1).toBe(4);
      expect(sent).not.toContain('{3}');
      expect(result.mine).toEqual({ added: 4, present: 0, missing: 0 });
    });

    it('Massenimport (mehr als fünf): kein Häkchen, keine Kopie', async () => {
      const el = createSmall(6);
      expect(el.querySelector('.auto-mine')).toBeNull();
      client.importPgn.and.resolveTo(ok(6));
      const result = await run();
      expect(toMine).not.toHaveBeenCalled();
      expect(result.mine).toBeUndefined();
    });

    it('abgewählt: keine Kopie, und das Gerät merkt es sich', async () => {
      const el = createSmall(1);
      const box = el.querySelector<HTMLInputElement>('.auto-mine input')!;
      box.checked = false;
      box.dispatchEvent(new Event('change'));
      expect(localStorage.getItem(AUTO_MINE_KEY)).toBe('0');
      client.importPgn.and.resolveTo(ok(1));
      const result = await run();
      expect(toMine).not.toHaveBeenCalled();
      expect(result.added).toBe(1);
      expect(result.mine).toBeUndefined();
    });

    it('ohne Konto bzw. fremde Liste (keine Ablage durchgereicht): kein Häkchen, keine Kopie', async () => {
      const el = createSmall(1, false);
      expect(el.querySelector('.auto-mine')).toBeNull();
      client.importPgn.and.resolveTo(ok(1));
      const result = await run();
      expect(toMine).not.toHaveBeenCalled();
      expect(result.mine).toBeUndefined();
    });

    it('scheitert die Kopie (Kontingent, Netz), bleibt der Vereins-Import gültig — nur ein Hinweis', async () => {
      createSmall(2);
      client.importPgn.and.resolveTo(ok(2));
      toMine.and.rejectWith(new HttpErrorResponse({ status: 500 }));
      const result = await run();
      expect(result.added).toBe(2);
      expect(result.mine).toEqual({ added: 0, present: 0, missing: 2 });
      expect(fixture.componentInstance.error()).toBeNull();
    });

    it('nimmt „Meine Partien“ nur einen Teil (Kontingent), steht der Rest als nicht abgelegt da', async () => {
      createSmall(2);
      client.importPgn.and.resolveTo(ok(2));
      toMine.and.resolveTo({ imported: 1, duplicates: 0, ids: [100] });
      expect((await run()).mine).toEqual({ added: 1, present: 0, missing: 1 });
    });

    it('war die Partie schon in „Meine Partien“, zählt sie als vorhanden', async () => {
      createSmall(1);
      client.importPgn.and.resolveTo(ok(1));
      toMine.and.resolveTo({ imported: 0, duplicates: 1, ids: [77] });
      expect((await run()).mine).toEqual({ added: 0, present: 1, missing: 0 });
    });
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
