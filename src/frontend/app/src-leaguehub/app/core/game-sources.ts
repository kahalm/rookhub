import { GameSources, PlayerSources } from './league.models';

/** „1.100" — fest mit Punkt (wie `chessbase-upload.ts`): `toLocaleString('de-AT')` setzt je nach Umgebung ein schmales Leerzeichen. */
export const thousands = (n: number): string => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, '.');

/** Eine Zeile der Quellen-Tabelle (0.628.0, Fassung B des Entwurfs vom 01.10.2026: Quelle | Gesamt | Liga | Begegnung). */
export interface SourceRow {
  key: string;
  label: string;
  games: string;
  /** Anteil an „Gesamt" der Gruppe in Prozent (Breite des Balkens). */
  share: number;
  /** Größte Quelle der Gruppe — Balken in Rot. */
  top: boolean;
  /** Spalten „Liga" und „Begegnung": Zahl, „–" = keiner dieser Spieler hat dort ein Konto, „" = Spalte fehlt. */
  league: string;
  opp: string;
}
export interface SourceGroup { title: 'Brett' | 'Online'; games: string; league: string; opp: string; rows: SourceRow[] }

/** Zelle einer Brett-Quelle bzw. der Brett-Summe für eine Spielergruppe. */
const boardCell = (p: PlayerSources | undefined, key?: string): string =>
  !p ? '' : thousands(key ? (p.board[key] ?? 0) : p.boardTotal);

/** Zelle einer Online-Seite bzw. der Online-Summe — „–", wenn keiner ein Konto (auf dieser Seite) hat. */
const onlineCell = (p: PlayerSources | undefined, key?: string): string => {
  if (!p) return '';
  if (key) return p.online[key] ? thousands(p.online[key].games) : '–';
  return p.onlineAccounts > 0 ? thousands(p.onlineTotal) : '–';
};

/** Brett und Online als Gruppen, je Quelle mit Anteil und — soweit der Server sie mitgeschickt hat — Liga und Begegnung. */
export function sourceGroups(s: GameSources): SourceGroup[] {
  const share = (n: number, total: number) => (total > 0 ? Math.round((n / total) * 1000) / 10 : 0);
  const topOf = (rows: { games: number }[]) => Math.max(0, ...rows.map(r => r.games));
  const groups: SourceGroup[] = [];
  if (s.boardTotal > 0) {
    const top = topOf(s.board);
    groups.push({
      title: 'Brett', games: thousands(s.boardTotal), league: boardCell(s.league), opp: boardCell(s.opponent),
      rows: s.board.map(r => ({
        key: r.key, label: r.label, games: thousands(r.games), share: share(r.games, s.boardTotal), top: r.games === top,
        league: boardCell(s.league, r.key), opp: boardCell(s.opponent, r.key),
      })),
    });
  }
  if (s.onlineTotal > 0) {
    const top = topOf(s.online);
    groups.push({
      title: 'Online', games: thousands(s.onlineTotal), league: onlineCell(s.league), opp: onlineCell(s.opponent),
      rows: s.online.map(r => ({
        key: r.key, label: r.label, games: thousands(r.games), share: share(r.games, s.onlineTotal), top: r.games === top,
        league: onlineCell(s.league, r.key), opp: onlineCell(s.opponent, r.key),
      })),
    });
  }
  return groups;
}
