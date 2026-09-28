import { computed, signal } from '@angular/core';
import { ClubPreview, ImportGameDecision, PreviewGame, PreviewSide, RosterPerson, SideMatch } from '../../core/club.models';

/** Eine Seite in der Übersicht, so wie der Nutzer sie gerade festgelegt hat. */
export interface ReviewSide {
  /** Name im PGN. */
  raw: string | null;
  /** Wer es ist: Name aus der Meldeliste, sonst der getippte bzw. der aus dem PGN. */
  name: string | null;
  fide: string | null;
  league: boolean;
  ambiguous: boolean;
  /** Spielt für Schwaz (jüngste Saison). */
  club: boolean;
  candidates: RosterPerson[];
  /** Laut Profil der Hochladende. */
  owner: boolean;
  /** Durch „Schwaz" ersetzen. */
  replace: boolean;
  /** Vom Nutzer umgesetzt — dann geht der Name mit, sonst gilt die Kopfzeile. */
  changed: boolean;
}

export interface ReviewGame {
  game: PreviewGame;
  white: ReviewSide;
  black: ReviewSide;
  /** Vom Nutzer abgewählt, obwohl übernehmbar. */
  excluded: boolean;
}

export type SideKey = 'white' | 'black';
export type ReviewFilter = 'all' | 'skipped' | 'unknown';

/** Wird die Partie übernommen — und wenn nicht, warum? DIESELBE Regel wie am Server (`LeagueClubService.Build`). */
export function reviewStatus(r: ReviewGame): { importable: boolean; reason: string | null } {
  if (r.game.error) return { importable: false, reason: r.game.error };
  if (r.game.duplicate) return { importable: false, reason: 'duplicate' };
  const w = r.white, b = r.black;
  if (!w.league && !b.league) return { importable: false, reason: 'noLeaguePlayer' };
  if (!(w.league && !w.replace) && !(b.league && !b.replace)) return { importable: false, reason: 'onlyOwnClub' };
  return { importable: true, reason: null };
}

export const included = (r: ReviewGame): boolean => reviewStatus(r).importable && !r.excluded;

/** Braucht ein Blick: nicht erkannt oder mehrdeutig (ersetzte Seiten zählen nicht). */
export const needsLook = (s: ReviewSide): boolean => !s.replace && (!s.league || s.ambiguous);

function sideOf(p: PreviewSide, replaceClub: boolean): ReviewSide {
  return {
    raw: p.raw, name: p.match.name ?? p.raw, fide: p.match.ambiguous ? null : p.match.fide, league: p.match.league,
    ambiguous: p.match.ambiguous, club: p.match.club, candidates: p.match.candidates ?? [], owner: p.owner,
    replace: replaceClub && p.replace, changed: false,
  };
}

/**
 * Die Übersicht vor dem PGN-Import (Wunsch 2026-09-28: „nach Import Übersicht wer gegen wen, unerkannte/falsche Spieler
 * korrigieren, je Partie markieren, ob sie importiert wird — der Spieler kann eingreifen"). Rein, ohne HTTP: die Seite
 * holt Abgleiche und gibt sie hier hinein. `replaceClub` = das Häkchen „Spieler von Schwaz durch Schwaz ersetzen".
 */
export class ImportReview {
  readonly games = signal<ReviewGame[]>([]);
  readonly filter = signal<ReviewFilter>('all');
  readonly truncated: boolean;

  readonly counts = computed(() => {
    const list = this.games();
    const take = list.filter(included).length;
    return {
      total: list.length, take, skip: list.length - take,
      unknown: list.filter(r => !r.game.error && !r.game.duplicate && (needsLook(r.white) || needsLook(r.black))).length,
    };
  });

  readonly visible = computed(() => {
    const f = this.filter();
    return this.games().filter(r => f === 'all' ? true
      : f === 'skipped' ? !included(r)
      : !r.game.error && !r.game.duplicate && (needsLook(r.white) || needsLook(r.black)));
  });

  constructor(preview: ClubPreview, readonly replaceClub: boolean) {
    this.truncated = preview.truncated;
    this.games.set(preview.games.map(g => ({
      game: g, white: sideOf(g.white, replaceClub), black: sideOf(g.black, replaceClub), excluded: false,
    })));
  }

  private update(index: number, fn: (r: ReviewGame) => ReviewGame): void {
    this.games.update(list => list.map(r => r.game.index === index ? fn(r) : r));
  }

  private withSide(index: number, side: SideKey, fn: (s: ReviewSide) => ReviewSide): void {
    this.update(index, r => ({ ...r, [side]: fn(r[side]) }));
  }

  /** Vorgabe „ersetzen" für einen neu gesetzten Spieler: Schwaz-Spieler und der Hochladende selbst. */
  private defaultReplace(club: boolean, owner: boolean): boolean {
    return this.replaceClub && (club || owner);
  }

  /** Einen Ligaspieler aus der Meldeliste (Vorschlag oder Kandidat) an diese Seite setzen. */
  choosePerson(index: number, side: SideKey, p: RosterPerson): void {
    this.withSide(index, side, s => ({
      ...s, name: p.name, fide: p.fide, league: p.league ?? true, ambiguous: false, club: p.club, candidates: [],
      replace: this.defaultReplace(p.club, s.owner), changed: true,
    }));
  }

  /** Einen getippten Namen samt Abgleich des Servers setzen. */
  setTyped(index: number, side: SideKey, name: string, m: SideMatch): void {
    this.withSide(index, side, s => ({
      ...s, name: m.name ?? name, fide: m.ambiguous ? null : m.fide, league: m.league, ambiguous: m.ambiguous, club: m.club,
      candidates: m.candidates ?? [], replace: this.defaultReplace(m.club, s.owner), changed: true,
    }));
  }

  setReplace(index: number, side: SideKey, replace: boolean): void {
    this.withSide(index, side, s => ({ ...s, replace }));
  }

  toggleInclude(index: number): void {
    this.update(index, r => ({ ...r, excluded: !r.excluded }));
  }

  /** Was an den Server geht: nur übernommene Partien, je Seite der festgelegte Spieler und „ersetzen". */
  decisions(): ImportGameDecision[] {
    // Die FIDE-ID geht mit, sobald der Spieler eindeutig ist — auch ohne Liga (aus dem Megabase-Verzeichnis gewählt).
    const side = (s: ReviewSide) => ({ name: s.changed ? s.name : null, fide: !s.ambiguous ? s.fide : null, replace: s.replace });
    return this.games().filter(included).map(r => ({ index: r.game.index, white: side(r.white), black: side(r.black) }));
  }
}
