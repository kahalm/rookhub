import { computed, signal } from '@angular/core';
import { ClubPairing, ClubPreview, ImportGameDecision, PreviewGame, PreviewSide, RosterPerson, SideMatch } from '../../core/club.models';

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
  /** Nicht erkannt: ähnlich geschriebene Ligaspieler zum Anklicken, ohne die Seite zu öffnen (0.596.0). */
  similar: RosterPerson[];
}

export interface ReviewGame {
  game: PreviewGame;
  white: ReviewSide;
  black: ReviewSide;
  /** Vom Nutzer abgewählt, obwohl übernehmbar. */
  excluded: boolean;
  /** Das Häkchen hat der Nutzer selbst angefasst — dann ändert eine übernommene Korrektur es nicht mehr. */
  touched?: boolean;
  /** Die gewählte Brettpaarung (0.678.0): `null` = keine. Vorgabe = die eindeutig erkannte des Servers. */
  pairingId: number | null;
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
    alias: !!p.match.alias, similar: p.match.similar ?? [],
  };
}

/** Die Schnellauswahl einer Seite: nur solange sie unerkannt und unangefasst ist (und nicht durch „Schwaz" ersetzt). */
export const quickPicks = (s: ReviewSide): RosterPerson[] =>
  !s.changed && !s.replace && !known(s) && !s.ambiguous ? s.similar ?? [] : [];

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
      const r: ReviewGame = { game: g, white: sideOf(g.white, replaceClub), black: sideOf(g.black, replaceClub), excluded: false,
        pairingId: g.pairingId ?? null };
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
      similar: [],
    });
    this.withSide(index, side, make, true);
    return this.propagate(index, side, make);
  }

  /** Einen getippten Namen samt Abgleich des Servers setzen (und wie bei {@link choosePerson} weitergeben). */
  setTyped(index: number, side: SideKey, name: string, m: SideMatch): number {
    const make = (s: ReviewSide): ReviewSide => ({
      ...s, name: m.name ?? name, fide: m.ambiguous ? null : m.fide, league: m.league, ambiguous: m.ambiguous, club: m.club,
      candidates: m.candidates ?? [], replace: this.defaultReplace(m.club, s.owner), changed: true,
      lastNameOnly: !!m.lastNameOnly, mega: !!m.mega, alias: !!m.alias, similar: m.similar ?? [],
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

  /**
   * Die Partie einer Brettpaarung zuordnen (0.678.0, Wunsch 2026-10-05: „einer Ligarunde zuweisen"). `null` = keine. Die
   * Paarung legt die Spieler fest: jede Seite, die der Nutzer nicht selbst gesetzt hat, bekommt Name und FIDE-ID aus der
   * Paarung — eine Seite von Schwaz wird dabei wie sonst ersetzt (Vorgabe). Die Partie wird damit importiert.
   */
  setPairing(index: number, id: number | null): void {
    this.update(index, r => {
      const p = id == null ? undefined : r.game.pairings?.find(x => x.id === id);
      if (!p) return { ...r, pairingId: null };
      if (p.open) return { ...r, pairingId: p.id, excluded: false };   // leeres Brett: Spieler bleiben
      const from = (s: ReviewSide, name: string, fide: string | null, own: boolean): ReviewSide => s.changed ? s : {
        ...s, name, fide, league: true, ambiguous: false, club: own, candidates: [], replace: this.defaultReplace(own, s.owner),
        changed: true, lastNameOnly: false, mega: false, alias: false, similar: [],
      };
      return {
        ...r, pairingId: p.id, excluded: false,
        white: from(r.white, p.white, p.whiteFide, p.whiteOwnClub),
        black: from(r.black, p.black, p.blackFide, p.blackOwnClub),
      };
    });
  }

  setReplace(index: number, side: SideKey, replace: boolean): void {
    this.withSide(index, side, s => ({ ...s, replace }));
  }

  toggleInclude(index: number): void {
    this.update(index, r => ({ ...r, excluded: !r.excluded, touched: true }));
  }

  /** Der Stand zum Speichern im Entwurf (0.595.0): je Partie die festgelegten Seiten und das Häkchen, dazu die Vorgabe
   *  „ersetzen" — mit {@link ImportReview.restore} auf einer frischen Übersicht derselben Liste wiederhergestellt. */
  snapshot(): string {
    const snap: ReviewSnapshot = {
      v: 1, replaceClub: this.replaceClub,
      games: this.games().map(r => ({ i: r.game.index, raw: [r.white.raw, r.black.raw], white: r.white, black: r.black,
        excluded: r.excluded, touched: r.touched, p: r.pairingId })),
    };
    return JSON.stringify(snap);
  }

  /** Eine Übersicht mit dem gespeicherten Stand — Partien, deren PGN-Namen nicht mehr passen, behalten ihre Vorgabe. */
  static restore(preview: ClubPreview, state: string | null, replaceClub: boolean): ImportReview {
    let snap: ReviewSnapshot | null = null;
    try { snap = state ? JSON.parse(state) as ReviewSnapshot : null; } catch { snap = null; }
    const review = new ImportReview(preview, snap?.v === 1 ? snap.replaceClub : replaceClub);
    if (snap?.v !== 1) return review;
    const byIndex = new Map(snap.games.map(g => [g.i, g]));
    review.games.update(list => list.map(r => {
      const s = byIndex.get(r.game.index);
      if (!s || s.raw[0] !== r.white.raw || s.raw[1] !== r.black.raw) return r;
      // Eine gewählte Paarung kommt nur zurück, wenn es sie noch gibt (ein Stand von vor 0.678.0 kennt keine).
      const pairingId = s.p === undefined ? r.pairingId
        : s.p === null || r.game.pairings?.some(x => x.id === s.p) ? s.p : r.pairingId;
      const next = { ...r, white: restoreSide(r.white, s.white), black: restoreSide(r.black, s.black), touched: s.touched, pairingId };
      return { ...next, excluded: s.touched ? s.excluded : optionalGame(next) };
    }));
    return review;
  }

  /** Was an den Server geht: nur übernommene Partien, je Seite der festgelegte Spieler und „ersetzen". */
  decisions(): ImportGameDecision[] {
    // Die FIDE-ID geht mit, sobald der Spieler eindeutig ist — auch ohne Liga (aus dem Megabase-Verzeichnis gewählt).
    const side = (s: ReviewSide) => ({ name: s.changed ? s.name : null, fide: !s.ambiguous ? s.fide : null, replace: s.replace });
    return this.games().filter(included).map(r => ({ index: r.game.index, white: side(r.white), black: side(r.black),
      leagueGameId: r.pairingId ?? 0 }));
  }
}

/** Eine Seite aus dem Entwurf (0.597.0): was der Nutzer selbst gesetzt hat, bleibt; eine NICHT angefasste Seite nimmt den
 * frischen Abgleich — sonst käme eine Seite, die der Server inzwischen erkennt (neue Regel, gemerkte Zuordnung), als
 * „mehrdeutig" zurück (gemeldet 2026-09-29 an „Forster, Stephan"). „Ersetzen" bleibt, solange die Seite gleich erkannt
 * wird (Schwaz ja/nein, ich ja/nein) — dann war es die Wahl des Nutzers; sonst gilt die frische Vorgabe. */
function restoreSide(fresh: ReviewSide, saved: ReviewSide): ReviewSide {
  // Ein Stand von vor 0.596.0 kennt die Schnellauswahl nicht — dann gilt die der frischen Übersicht.
  if (saved.changed) return { ...saved, similar: saved.similar ?? fresh.similar };
  const same = saved.club === fresh.club && saved.owner === fresh.owner;
  return { ...fresh, replace: same ? saved.replace : fresh.replace };
}

interface ReviewSnapshot {
  v: 1;
  replaceClub: boolean;
  games: { i: number; raw: [string | null, string | null]; white: ReviewSide; black: ReviewSide; excluded: boolean; touched?: boolean;
    /** Gewählte Brettpaarung (0.678.0); fehlt in älteren Ständen. */
    p?: number | null }[];
}

/** Wie eine Paarung in der Auswahl steht: „2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026) — Hengl – Muster". Die
 *  Namen sind die des öffentlichen Spielplans; „(?)" = Spieler oder Tag passen nicht ganz. */
export function pairingText(p: ClubPairing): string {
  if (p.open) return `${p.label} — ${p.white} – ${p.black}, noch nicht besetzt${p.exact ? '' : ' (?)'}`;
  return `${p.label} — ${p.white} – ${p.black}${p.exact ? '' : ' (?)'}`;
}
