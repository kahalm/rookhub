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
  /** Nur über den Nachnamen zugeordnet („Kostic") — braucht einen Blick. */
  lastNameOnly: boolean;
  /** Kein Ligaspieler, aber im Megabase-Verzeichnis gefunden oder dort ausgewählt — „nicht in Liga". */
  mega: boolean;
  /** Über eine gemerkte Zuordnung zugeordnet (Server). */
  alias: boolean;
}

export interface ReviewGame {
  game: PreviewGame;
  white: ReviewSide;
  black: ReviewSide;
  /** Vom Nutzer abgewählt, obwohl übernehmbar. */
  excluded: boolean;
  /** Das Häkchen hat der Nutzer selbst angefasst — dann ändert eine übernommene Korrektur es nicht mehr. */
  touched?: boolean;
}

export type SideKey = 'white' | 'black';
export type ReviewFilter = 'all' | 'skipped' | 'new' | 'unknown';

/** Bekannt = Ligaspieler oder im Megabase-Verzeichnis (Wunsch 2026-09-28: „wenn in Tirol kein Treffer"). */
export const known = (s: ReviewSide): boolean => s.league || s.mega;

/** Wird die Partie übernommen — und wenn nicht, warum? DIESELBE Regel wie am Server (`LeagueClubService.Build`). */
export function reviewStatus(r: ReviewGame): { importable: boolean; reason: string | null } {
  if (r.game.error) return { importable: false, reason: r.game.error };
  if (r.game.duplicate) return { importable: false, reason: 'duplicate' };
  const w = r.white, b = r.black;
  if (!known(w) && !known(b)) return { importable: false, reason: 'noLeaguePlayer' };
  if (!(known(w) && !w.replace) && !(known(b) && !b.replace)) return { importable: false, reason: 'onlyOwnClub' };
  return { importable: true, reason: null };
}

/** Übernehmbar, aber ohne Gegner aus der Liga (nur die Megabase kennt ihn): nicht vorgewählt, lässt sich anhaken. */
export const optionalGame = (r: ReviewGame): boolean =>
  reviewStatus(r).importable && !(r.white.league && !r.white.replace) && !(r.black.league && !r.black.replace);

export const included = (r: ReviewGame): boolean => reviewStatus(r).importable && !r.excluded;

/** Braucht ein Blick: nicht erkannt oder mehrdeutig (ersetzte Seiten zählen nicht; „nicht in Liga" ist erkannt). */
export const needsLook = (s: ReviewSide): boolean => !s.replace && (!known(s) || s.ambiguous || s.lastNameOnly);

function sideOf(p: PreviewSide, replaceClub: boolean): ReviewSide {
  return {
    raw: p.raw, name: p.match.name ?? p.raw, fide: p.match.ambiguous ? null : p.match.fide, league: p.match.league,
    ambiguous: p.match.ambiguous, club: p.match.club, candidates: p.match.candidates ?? [], owner: p.owner,
    replace: replaceClub && p.replace, changed: false, lastNameOnly: !!p.match.lastNameOnly, mega: !!p.match.mega,
    alias: !!p.match.alias,
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

  /** Welche Partien ein Filter zeigt: „nicht importiert", „noch nicht vorhanden" (nicht schon in der Vereins-Datenbank
   * oder weiter oben in der Datei), „nicht erkannt" (ein Spieler braucht einen Blick). */
  static matches(f: ReviewFilter, r: ReviewGame): boolean {
    switch (f) {
      case 'skipped': return !included(r);
      case 'new': return !r.game.duplicate;
      case 'unknown': return !r.game.error && !r.game.duplicate && (needsLook(r.white) || needsLook(r.black));
      default: return true;
    }
  }

  /** Wie viele Partien jeder Filter zeigt — steht an den Knöpfen. */
  readonly filterCounts = computed(() => {
    const list = this.games();
    const n = (f: ReviewFilter) => list.filter(r => ImportReview.matches(f, r)).length;
    return { all: list.length, skipped: n('skipped'), new: n('new'), unknown: n('unknown') } as Record<ReviewFilter, number>;
  });

  readonly counts = computed(() => {
    const list = this.games();
    const take = list.filter(included).length;
    return {
      total: list.length, take, skip: list.length - take,
      unknown: list.filter(r => !r.game.error && !r.game.duplicate && (needsLook(r.white) || needsLook(r.black))).length,
      optional: list.filter(r => optionalGame(r) && r.excluded).length,
    };
  });

  readonly visible = computed(() => {
    const f = this.filter();
    return this.games().filter(r => ImportReview.matches(f, r));
  });

  constructor(preview: ClubPreview, readonly replaceClub: boolean) {
    this.truncated = preview.truncated;
    this.games.set(preview.games.map(g => {
      const r: ReviewGame = { game: g, white: sideOf(g.white, replaceClub), black: sideOf(g.black, replaceClub), excluded: false };
      return { ...r, excluded: optionalGame(r) };
    }));
  }

  private update(index: number, fn: (r: ReviewGame) => ReviewGame): void {
    this.games.update(list => list.map(r => r.game.index === index ? fn(r) : r));
  }

  /** Eine Seite umsetzen. `pick` = der Nutzer hat an dieser Partie einen Spieler festgelegt — dann wird sie auch
   * importiert (Wunsch 2026-09-28: „wenn ich einen Spieler aus der Megabase auswähle, soll er auch importieren wählen"). */
  private withSide(index: number, side: SideKey, fn: (s: ReviewSide) => ReviewSide, pick = false): void {
    this.update(index, r => ({ ...r, [side]: fn(r[side]), excluded: pick ? false : r.excluded }));
  }

  /** Vorgabe „ersetzen" für einen neu gesetzten Spieler: Schwaz-Spieler und der Hochladende selbst. */
  private defaultReplace(club: boolean, owner: boolean): boolean {
    return this.replaceClub && (club || owner);
  }

  /** Einen Spieler (Meldeliste, Kandidat oder Megabase) an diese Seite setzen — die Partie wird damit importiert. Gilt
   * auch für jede andere, noch nicht angefasste Seite mit demselben Namen im PGN; liefert, wie viele das waren. */
  choosePerson(index: number, side: SideKey, p: RosterPerson): number {
    const league = p.league ?? true;
    const make = (s: ReviewSide): ReviewSide => ({
      ...s, name: p.name, fide: p.fide, league, ambiguous: false, club: p.club, candidates: [],
      replace: this.defaultReplace(p.club, s.owner), changed: true, lastNameOnly: false, mega: !league, alias: false,
    });
    this.withSide(index, side, make, true);
    return this.propagate(index, side, make);
  }

  /** Einen getippten Namen samt Abgleich des Servers setzen (und wie bei {@link choosePerson} weitergeben). */
  setTyped(index: number, side: SideKey, name: string, m: SideMatch): number {
    const make = (s: ReviewSide): ReviewSide => ({
      ...s, name: m.name ?? name, fide: m.ambiguous ? null : m.fide, league: m.league, ambiguous: m.ambiguous, club: m.club,
      candidates: m.candidates ?? [], replace: this.defaultReplace(m.club, s.owner), changed: true,
      lastNameOnly: !!m.lastNameOnly, mega: !!m.mega, alias: !!m.alias,
    });
    this.withSide(index, side, make, true);
    return this.propagate(index, side, make);
  }

  /** Derselbe Name im PGN (ohne Groß/klein, Leerzeichen) — Wunsch 2026-09-28: „merk dir das zum Original und matche das
   * bei allen selbst". */
  static rawKey(raw: string | null): string {
    return (raw ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
  }

  /** Dieselbe Korrektur an jeder anderen Seite mit demselben PGN-Namen, die der Nutzer noch nicht selbst gesetzt hat.
   * Ob die Partie übernommen wird, folgt dann der Vorgabe (außer der Nutzer hat das Häkchen selbst angefasst). */
  private propagate(index: number, side: SideKey, make: (s: ReviewSide) => ReviewSide): number {
    const src = this.games().find(r => r.game.index === index)?.[side];
    const key = ImportReview.rawKey(src?.raw ?? null);
    if (!key) return 0;
    let n = 0;
    this.games.update(list => list.map(r => {
      let next = r;
      for (const k of ['white', 'black'] as SideKey[]) {
        const s = r[k];
        if ((r.game.index === index && k === side) || s.changed || ImportReview.rawKey(s.raw) !== key) continue;
        next = { ...next, [k]: make(s) };
        n++;
      }
      return next === r ? r : { ...next, excluded: r.touched ? r.excluded : optionalGame(next) };
    }));
    return n;
  }

  setReplace(index: number, side: SideKey, replace: boolean): void {
    this.withSide(index, side, s => ({ ...s, replace }));
  }

  toggleInclude(index: number): void {
    this.update(index, r => ({ ...r, excluded: !r.excluded, touched: true }));
  }

  /** Was an den Server geht: nur übernommene Partien, je Seite der festgelegte Spieler und „ersetzen". */
  decisions(): ImportGameDecision[] {
    // Die FIDE-ID geht mit, sobald der Spieler eindeutig ist — auch ohne Liga (aus dem Megabase-Verzeichnis gewählt).
    const side = (s: ReviewSide) => ({ name: s.changed ? s.name : null, fide: !s.ambiguous ? s.fide : null, replace: s.replace });
    return this.games().filter(included).map(r => ({ index: r.game.index, white: side(r.white), black: side(r.black) }));
  }
}
