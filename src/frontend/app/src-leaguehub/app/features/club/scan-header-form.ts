import { computed, signal } from '@angular/core';
import { ClubClient } from '../../core/club-api.service';
import { ClubPairing, LeagueScanState, RosterPerson, SideMatch } from '../../core/club.models';
import { normalizeResult, presetYear } from '../../core/club-format';
import { pairingText } from './import-review';

export type Side = 'white' | 'black';
export const SIDES: Side[] = ['white', 'black'];
const MATCH_DEBOUNCE_MS = 400;

export interface ScanHeaderFormOptions {
  api: Pick<ClubClient, 'match' | 'pairings'>;
  /** Name anonymisierter Spieler (der des angemeldeten Vereins bzw. des Teilen-Links). */
  anon: () => string;
  clubName: () => string;
  /** Vorschläge für die Ligapaarung holen — nicht beim Korrigieren einer schon übernommenen Vereinspartie. */
  pairingsEnabled: boolean;
}

/**
 * Die Kopfdaten eines eingelesenen Partieformulars vor dem Übernehmen (`ClubScanPageComponent`): Namen mit
 * Ligaspieler-Prüfung und „durch Schwaz ersetzen" je Seite (Vorgabe: Spieler von Schwaz und die eigene Seite), Elo,
 * Jahr, Ergebnis, Veranstaltung und die erkannte Brettpaarung einer Ligarunde (0.678.0).
 */
export class ScanHeaderForm {
  readonly sides = SIDES;
  private readonly sideState = {
    white: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
    black: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
  };
  readonly name = (k: Side) => this.sideState[k].name;
  readonly fide = (k: Side) => this.sideState[k].fide;
  readonly elo = (k: Side) => this.sideState[k].elo;
  readonly match = (k: Side) => this.sideState[k].match;
  readonly replace = (k: Side) => this.sideState[k].replace;
  readonly year = signal<number | null>(null);
  /** Die Erkennung las ein älteres Jahr, vorbelegt ist heuer (0.655.0) — Hinweis unter dem Feld, bis man es ändert. */
  readonly yearRead = signal<number | null>(null);
  readonly result = signal('*');
  readonly event = signal('');
  readonly ownerSide = signal<Side | null>(null);
  readonly anyReplaced = computed(() => this.replace('white')() || this.replace('black')());
  /** Der Tag laut Formular — nur für die Erkennung der Ligapaarung (gespeichert wird nur das Jahr). */
  sheetDate: string | null = null;
  /** Brettpaarungen, die diese Partie sein könnten (0.678.0); `pairingId` = die gewählte. */
  readonly pairings = signal<ClubPairing[]>([]);
  readonly pairingId = signal<number | null>(null);
  readonly pairingText = pairingText;
  /** Der Nutzer hat selbst gewählt — dann ändert ein neuer Vorschlag die Wahl nicht mehr. */
  private pairingTouched = false;
  /** Vorschläge wurden geholt — erst dann heißt „keine" auch keine (sonst entscheidet der Server). */
  pairingsLoaded = false;
  private pairingTimer: ReturnType<typeof setTimeout> | null = null;
  private pairingSeq = 0;
  private matchTimer: ReturnType<typeof setTimeout> | null = null;
  private matchSeq = 0;
  private disposed = false;

  constructor(private readonly opts: ScanHeaderFormOptions) {}

  /** Wie die Partie in der Datenbank steht (Jahr · Weiß – Schwarz · Ergebnis). */
  readonly preview = computed(() => {
    const shown = (k: Side) => this.replace(k)() ? this.opts.anon() : this.match(k)()?.name || this.name(k)().trim() || '?';
    return [this.year() ?? 'ohne Jahr', `${shown('white')} – ${shown('black')}`, this.result() === '*' ? 'Ergebnis offen' : this.result()]
      .join(' · ');
  });

  /** Was die Partie unübernehmbar macht — dieselbe Regel wie am Server (`ClubSideResolver.Build`): bekannt ist, wer in
   * der Liga ODER im Megabase-Verzeichnis steht. */
  readonly problem = computed(() => {
    const w = this.match('white')(), b = this.match('black')();
    if (!w || !b) return null;
    const known = (m: SideMatch) => m.league || !!m.mega;
    if (!known(w) && !known(b))
      return 'Kein Spieler erkannt (weder in der Liga noch in der Megabase) — so wird die Partie nicht angenommen. Namen prüfen (Vorschläge beim Tippen).';
    if (!(known(w) && !this.replace('white')()) && !(known(b) && !this.replace('black')()))
      return 'Nach dem Ersetzen bleibt kein bekannter Gegner übrig — so wird die Partie nicht angenommen.';
    return null;
  });

  /** Die Kopfdaten der fertigen Lesung übernehmen und die Namen abgleichen. */
  fill(st: LeagueScanState): void {
    this.name('white').set(st.white ?? '');
    this.name('black').set(st.black ?? '');
    const preset = presetYear(st.date);
    this.sheetDate = st.date;
    this.year.set(preset.year);
    this.yearRead.set(preset.read);
    this.result.set(normalizeResult(st.result));
    this.event.set(st.event ?? '');
    this.ownerSide.set(st.ownerSide);
    void this.runMatch();
  }

  /** Wartende Abgleiche abbrechen (Seite verlassen). */
  dispose(): void {
    this.disposed = true;
    for (const t of [this.matchTimer, this.pairingTimer]) if (t) clearTimeout(t);
  }

  /** Vorgabe „ersetzen": Spieler von Schwaz und die eigene Seite — bis der Nutzer das Häkchen selbst anfasst. */
  private applyDefault(k: Side): void {
    const st = this.sideState[k];
    if (st.touched) return;
    st.replace.set(!!st.match()?.club || this.ownerSide() === k);
  }

  setOwner(k: Side): void {
    this.ownerSide.set(k);
    for (const x of this.sides) this.applyDefault(x);
  }

  setReplace(k: Side, on: boolean): void {
    this.sideState[k].touched = true;
    this.sideState[k].replace.set(on);
  }

  setName(k: Side, value: string): void {
    const st = this.sideState[k];
    st.name.set(value);
    st.fide.set(null);
    if (this.matchTimer) clearTimeout(this.matchTimer);
    this.matchTimer = setTimeout(() => void this.runMatch(), MATCH_DEBOUNCE_MS);
  }

  /** Ein Treffer der Suche (Liga oder Megabase) bringt Namen und FIDE-ID mit — so ist der Spieler eindeutig. */
  pickPerson(k: Side, p: RosterPerson): void {
    const st = this.sideState[k];
    if (this.matchTimer) clearTimeout(this.matchTimer);
    st.name.set(p.name);
    st.fide.set(p.fide);
    // Die Elo des gewählten Spielers gleich mit (Wunsch 2026-10-05) — aus der jüngsten Meldeliste, sonst bleibt sie.
    if (p.elo) st.elo.set(p.elo);
    const league = p.league ?? true;
    st.match.set({ league, ambiguous: false, name: p.name, fide: p.fide, club: p.club, candidates: [], mega: !league });
    this.applyDefault(k);
    this.schedulePairings();
  }

  /** Vorschläge für die Ligapaarung neu holen (gedrosselt) — nach jedem Abgleich der Namen und jedem Jahr. */
  schedulePairings(): void {
    if (!this.opts.pairingsEnabled) return;
    if (this.pairingTimer) clearTimeout(this.pairingTimer);
    this.pairingTimer = setTimeout(() => void this.loadPairings(), MATCH_DEBOUNCE_MS);
  }

  private async loadPairings(): Promise<void> {
    const my = ++this.pairingSeq;
    const fide = (k: Side) => this.sideState[k].fide() ?? (this.match(k)()?.ambiguous ? null : this.match(k)()?.fide ?? null);
    try {
      const list = await this.opts.api.pairings({
        white: this.name('white')().trim() || null, whiteFide: fide('white'),
        black: this.name('black')().trim() || null, blackFide: fide('black'),
        date: this.sheetDate, year: this.year(),
      });
      if (my !== this.pairingSeq || this.disposed) return;
      // Eine schon gewählte Paarung bleibt in der Auswahl, auch wenn sie nach einer Namensänderung nicht mehr vorkäme.
      const chosen = this.pairings().find(p => p.id === this.pairingId());
      this.pairings.set(chosen && !list.some(p => p.id === chosen.id) ? [chosen, ...list] : list);
      this.pairingsLoaded = true;
      if (!this.pairingTouched) {
        // genau ein genauer Treffer, sonst die vorläufige Zuordnung zur laufenden Runde (0.832.0) — wie der Server
        const exact = list.filter(p => p.exact);
        this.pairingId.set(exact.length === 1 ? exact[0].id : list.find(p => p.provisional)?.id ?? null);
      }
    } catch { /* ohne Vorschläge entscheidet der Server beim Übernehmen selbst */ }
  }

  /** Eine Paarung gewählt: die Spieler kommen aus dem Spielplan (eine Seite von Schwaz wird wie sonst ersetzt). */
  choosePairing(value: string): void {
    this.pairingTouched = true;
    const p = value ? this.pairings().find(x => x.id === Number(value)) : undefined;
    this.pairingId.set(p?.id ?? null);
    if (!p || p.open) return;   // leeres Brett der laufenden Runde: die Namen bleiben, wie sie sind
    const person = (name: string, fide: string | null, club: boolean): RosterPerson => ({ name, fide, teams: [], club, league: true });
    this.pickPerson('white', person(p.white, p.whiteFide, p.whiteOwnClub));
    this.pickPerson('black', person(p.black, p.blackFide, p.blackOwnClub));
    const year = Number(p.label.match(/\((\d\d)\.(\d\d)\.(\d{4})\)$/)?.[3]);
    if (year) { this.year.set(year); this.yearRead.set(null); }
  }

  private async runMatch(): Promise<void> {
    const my = ++this.matchSeq;
    try {
      const m = await this.opts.api.match(this.name('white')(), this.name('black')());
      if (my !== this.matchSeq) return;
      for (const k of this.sides) {
        if (this.sideState[k].fide()) continue;             // aus der Meldeliste gewählt: bleibt
        this.sideState[k].match.set(m[k]);
        this.applyDefault(k);
      }
      this.schedulePairings();
    } catch { /* die Prüfung macht der Server beim Übernehmen ohnehin */ }
  }

  /** Nicht erkannt: ähnlich geschriebene Ligaspieler zum Anklicken (0.596.0, derselbe Abgleich wie in der PGN-Übersicht). */
  similarOf(k: Side): RosterPerson[] {
    const m = this.match(k)();
    return m && !this.replace(k)() && !m.league && !m.mega && !m.ambiguous ? m.similar ?? [] : [];
  }

  matchText(k: Side): string {
    const m = this.match(k)();
    const anon = this.opts.anon();
    if (this.replace(k)()) return m?.club ? `Spieler von ${this.opts.clubName()} — wird „${anon}“` : `wird „${anon}“`;
    if (!m) return '';
    if (m.ambiguous) return 'Ligaspieler (mehrere dieses Namens — bitte aus den Vorschlägen wählen)';
    if (m.league && m.lastNameOnly) return `nur über den Nachnamen: ${m.name} — bitte prüfen`;
    const kept = m.alias ? ' (gemerkt)' : '';
    if (m.league) return `Ligaspieler: ${m.name}${m.fide ? '' : ' (ohne FIDE-ID)'}${m.club ? ` — ${anon}, nicht ersetzt` : ''}${kept}`;
    if (m.mega) return `nicht in Liga — aus der Megabase: ${m.name}${m.fide ? ` (FIDE ${m.fide})` : ''}${kept}`;
    return 'nicht erkannt';
  }
}
