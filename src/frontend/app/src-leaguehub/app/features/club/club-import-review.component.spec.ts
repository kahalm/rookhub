import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { ClubClient } from '../../core/club-api.service';
import { ClubPreview, SideMatch } from '../../core/club.models';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ImportReview } from './import-review';

const M = (x: Partial<SideMatch> = {}): SideMatch =>
  ({ league: false, ambiguous: false, name: null, fide: null, club: false, candidates: [], ...x });
const PREVIEW: ClubPreview = { truncated: false, games: [
  { index: 1, year: 2024, result: '1-0', event: null, plies: 40, opening: '1.e4 c5', error: null, duplicate: false,
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
    expect(rows[1].textContent).toContain('mehrdeutig');
    expect(rows[1].textContent).toContain('nicht erkannt');
    expect(el.querySelector('.review-head')?.textContent).toContain('3 Partien gelesen — 2 werden importiert');
    expect(rows[2].textContent).toContain('nicht in Liga');
    expect(rows[2].textContent).toContain('nicht in Liga — anhaken zum Hinzufügen');
    expect((rows[2].querySelector('input[type=checkbox]') as HTMLInputElement).disabled).toBeFalse();
    expect(el.querySelector('.review-head')?.textContent).toContain('1 ohne Gegner aus der Liga');
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
