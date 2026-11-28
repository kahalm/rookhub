import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { EloCalcDialogData, EloCalculatorDialogComponent, deriveRatings } from './elo-calculator-dialog.component';
import { PlayerHistory, PlayerHistoryEntry } from './tournament-history.model';

function entry(over: Partial<PlayerHistoryEntry> = {}): PlayerHistoryEntry {
  return {
    chessResultsId: '1', name: 'Open', endDate: '2025-08-30', rank: 1, playerCount: 10, rounds: 9,
    points: 5, performanceRating: 1900, ratingChange: 6.4, ratingBefore: 1917, hasResult: true,
    cardFetched: true, speed: 'standard', gamesPlayed: 9, ratingNational: 1958, ...over,
  };
}

function history(over: Partial<PlayerHistory> = {}): PlayerHistory {
  return { userId: 1, displayName: 'Ich', status: 'ok', entries: [entry()], pending: 0, ...over };
}

describe('deriveRatings', () => {
  it('nimmt die juengste Karte: FIDE = Beginn + Aenderung, national = Beginn', () => {
    const r = deriveRatings(history({
      birthYear: 1982,
      entries: [
        entry({ name: 'Alt', endDate: '2024-01-01', ratingBefore: 1800, ratingNational: 1850 }),
        entry({ name: 'Neu', endDate: '2025-08-30' }),
        entry({ name: 'Kommt', endDate: '2027-01-01', ratingBefore: null, ratingNational: null }),
      ],
    }));
    expect(r.international).toBe(1923);
    expect(r.national).toBe(1958);
    expect(r.birthYear).toBe(1982);
    expect(r.source?.name).toBe('Neu');
  });

  it('ohne Wertung bleibt das Feld leer (0 heisst „ohne")', () => {
    const r = deriveRatings(history({ entries: [entry({ ratingBefore: 0, ratingNational: null })] }));
    expect(r.international).toBeNull();
    expect(r.national).toBeNull();
    expect(deriveRatings(null).international).toBeNull();
  });
});

describe('EloCalculatorDialogComponent', () => {
  let fixture: ComponentFixture<EloCalculatorDialogComponent>;
  let component: EloCalculatorDialogComponent;
  let http: HttpTestingController;

  afterEach(() => TestBed.resetTestingModule());

  async function mount(data: EloCalcDialogData) {
    await TestBed.configureTestingModule({
      imports: [EloCalculatorDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();
    TestBed.inject(TranslateService).use('de');
    fixture = TestBed.createComponent(EloCalculatorDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  const players = [
    { key: 'u:1', kind: 'account' as const, id: 1, label: 'Ich' },
    { key: 't:7', kind: 'tracked' as const, id: 7, label: 'Gegner, Max' },
  ];

  it('A ist vorausgewaehlt und aus dem geladenen Verlauf gefuellt', async () => {
    await mount({ players, meKey: 'u:1', loaded: { 'u:1': history() } });
    const [a] = component.sides;
    expect(a.playerKey()).toBe('u:1');
    expect(a.international()).toBe(1923);
    expect(a.national()).toBe(1958);
    expect(a.fideK()).toBe(20);
    expect(a.nationalK()).toBe(20.8);   // (3400 − 1958)² / 1e5 = 20,79…
  });

  it('B per Auswahl laedt den Verlauf nach; die Tabelle rechnet beide Seiten', async () => {
    await mount({ players, meKey: 'u:1', loaded: { 'u:1': history() } });
    const [, b] = component.sides;
    component.choosePlayer(b, 't:7');
    http.expectOne('/api/tournament-history/tracked/7').flush(history({
      userId: 0, entries: [entry({ ratingBefore: 2100, ratingChange: 0, ratingNational: 2100 })],
    }));
    expect(b.international()).toBe(2100);

    // A 1923 gegen B 2100 (We .27), K 20: Sieg +14,6; B verliert dabei mit K 20: 20 · (0 − .73) = −14,6.
    expect(component.change(0, 'fide', 'win')).toBe('+14,6');
    expect(component.change(0, 'fide', 'loss')).toBe('−5,4');
    expect(component.change(1, 'fide', 'win')).toBe('−14,6');
  });

  it('manuell: Elo und K selbst eintragen, ohne Wertung „–"', async () => {
    await mount({ players: [], meKey: null, loaded: {} });
    const [a, b] = component.sides;
    expect(a.mode()).toBe('manual');
    expect(component.change(0, 'fide', 'win')).toBeNull();

    component.setInternational(a, 2000);
    component.setInternational(b, 1800);
    component.setFideK(a, 40);
    expect(component.change(0, 'fide', 'win')).toBe('+9,6');    // 40 · (1 − .76)

    component.setNational(a, 2000);
    component.setNational(b, 1800);
    expect(a.nationalK()).toBe(19.6);
    expect(component.change(0, 'national', 'win')).toBe('+4,7');
    component.setNationalK(a, 30);
    expect(component.change(0, 'national', 'win')).toBe('+7,2');

    fixture.detectChanges();
    const cells = Array.from(fixture.nativeElement.querySelectorAll('tr[data-result="win"] td'))
      .map(td => (td as HTMLElement).textContent!.trim());
    expect(cells[0]).toBe('+9,6');
    expect(cells[1]).toBe('+7,2');
  });

  it('FIDE-K folgt der Elo, bis man ihn selbst waehlt', async () => {
    await mount({ players: [], meKey: null, loaded: {} });
    const [a] = component.sides;
    component.setInternational(a, 2450);
    expect(a.fideK()).toBe(10);
    component.setFideK(a, 20);
    component.setInternational(a, 1500);
    expect(a.fideK()).toBe(20);
  });

  it('FIDE-Suche fuellt die internationale Wertung von B', async () => {
    await mount({ players, meKey: 'u:1', loaded: { 'u:1': history() } });
    const [, b] = component.sides;
    component.searchName = 'Carlsen, Magnus';
    component.search();
    const req = http.expectOne(r => r.url.endsWith('/player-search'));
    expect(req.request.params.get('lastName')).toBe('Carlsen');
    expect(req.request.params.get('firstName')).toBe('Magnus');
    req.flush({ chessResultsResults: [], fideResults: [
      { name: 'Carlsen, Magnus', fideId: '1503014', chessResultsId: null, elo: 2830, country: 'NOR', title: 'GM' },
    ] });
    component.chooseHit(b, component.searchHits()![0]);
    expect(b.international()).toBe(2830);
    expect(b.national()).toBeNull();
    expect(b.label()).toBe('Carlsen, Magnus');
  });
});
