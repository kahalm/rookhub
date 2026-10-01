import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GameSources } from '../core/league.models';
import { GameSourcesComponent } from './game-sources.component';

const S: GameSources = {
  board: [{ key: 'Lumbra', label: 'Lumbra', games: 300 }, { key: 'Mega', label: 'ChessBase-Megabase', games: 100 }], boardTotal: 400,
  online: [{ key: 'lichess', label: 'Lichess', games: 1000 }], onlineTotal: 1000, countedAt: 'x',
};

describe('GameSourcesComponent', () => {
  let fixture: ComponentFixture<GameSourcesComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  function render(s: GameSources, opponent: string | null, league: string | null = null): void {
    fixture = TestBed.createComponent(GameSourcesComponent);
    fixture.componentRef.setInput('sources', s);
    fixture.componentRef.setInput('opponent', opponent);
    fixture.componentRef.setInput('league', league);
    fixture.detectChanges();
  }

  it('ohne Liga und Begegnung zwei Spalten, Balken nach Anteil, größte Quelle rot', () => {
    render(S, null);
    expect(Array.from(el().querySelectorAll('thead th')).map(t => t.textContent!.trim())).toEqual(['Quelle', 'Gesamtalle Ligen']);
    const bars = Array.from(el().querySelectorAll<HTMLElement>('.src-share b'));
    expect(bars.map(b => b.style.width)).toEqual(['75%', '25%', '100%']);
    expect(bars.map(b => b.classList.contains('top'))).toEqual([true, false, true]);
    expect(el().querySelectorAll('.src-group').length).toBe(2);
  });

  it('Liga und Begegnung: Name und Spielerzahl unter der Überschrift (ohne Schrägstrich am Ende), „–“ mit Erklärung', () => {
    render({
      ...S,
      league: { players: 177, board: { Lumbra: 250 }, boardTotal: 250, online: { lichess: { games: 900, accounts: 4 } }, onlineTotal: 900, onlineAccounts: 4 },
      opponent: { players: 8, board: { Lumbra: 7 }, boardTotal: 7, online: {}, onlineTotal: 0, onlineAccounts: 0 },
    }, 'Freibauer Innsbruck/', 'Landesliga');
    expect(Array.from(el().querySelectorAll('thead th')).map(t => t.textContent!.trim()))
      .toEqual(['Quelle', 'Gesamtalle Ligen', 'LigaLandesliga · 177 Spieler', 'BegegnungFreibauer Innsbruck · 8 Spieler']);
    const rows = Array.from(el().querySelectorAll('tbody tr')).map(r => Array.from(r.children).map(c => c.textContent!.trim()));
    expect(rows).toEqual([['Brett', '400', '250', '7'], ['Lumbra', '300', '250', '7'], ['ChessBase-Megabase', '100', '0', '0'],
      ['Online', '1.000', '900', '–'], ['Lichess', '1.000', '900', '–']]);
    const dash = Array.from(el().querySelectorAll<HTMLElement>('td.none'));
    expect(dash.map(d => d.textContent!.trim())).toEqual(['–', '–']);
    expect(dash[1].title).toBe('Für die Spieler von Freibauer Innsbruck ist kein Lichess-Konto eingetragen');
    expect(dash[0].title).toBe('Für die Spieler von Freibauer Innsbruck ist kein Online-Konto eingetragen');
  });
});
