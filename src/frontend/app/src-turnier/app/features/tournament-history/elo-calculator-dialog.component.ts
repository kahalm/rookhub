import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of } from 'rxjs';
import { ProfileService } from '@rh/core/profile.service';
import { PlayerSearchItem, PlayerSearchResult } from '@rh/shared/profile-identity-form/profile-identity-form.component';
import {
  FIDE_K_FACTORS, GAME_RESULTS, GameResult, fideChange, fideKFactor, formatChange, oesbChange, oesbKFactor,
} from './elo-calc';
import { PlayerHistory } from './tournament-history.model';
import { TournamentHistoryService } from './tournament-history.service';

/** Ein Spieler, den die Seite schon kennt (ich, Freunde, Verfolgte). */
export interface EloCalcPlayer {
  key: string;
  kind: 'account' | 'tracked';
  id: number;
  label: string;
}

export interface EloCalcDialogData {
  players: EloCalcPlayer[];
  /** Vorauswahl fuer Spieler A — der angemeldete Spieler. */
  meKey: string | null;
  /** Schon geladene Verlaeufe nach Reiter-Schluessel — spart den zweiten Abruf. */
  loaded: Record<string, PlayerHistory>;
}

/** Was ein Verlauf fuer den Rechner hergibt. */
export interface DerivedRatings {
  international: number | null;
  national: number | null;
  birthYear: number | null;
  /** Woher die Zahl stammt: das Turnier, an dessen Karte sie steht. */
  source: { name: string; endDate: string | null } | null;
}

/**
 * Die Wertungen eines Spielers aus seinem Verlauf, so gut es geht: die juengste Spielerkarte
 * mit Wertung. International = Wertung zu Beginn + Aenderung in diesem Turnier (das kommt der
 * aktuellen Elo am naechsten); national = Wertung zu Beginn (eine nationale Aenderung fuehrt die
 * Karte nicht). Zahlen <= 0 sind „ohne Wertung".
 */
export function deriveRatings(history: PlayerHistory | null | undefined): DerivedRatings {
  const none: DerivedRatings = { international: null, national: null, birthYear: null, source: null };
  if (!history) return none;
  const byDate = [...history.entries].sort((a, b) => (b.endDate ?? '').localeCompare(a.endDate ?? ''));
  const intl = byDate.find(e => (e.ratingBefore ?? 0) > 0);
  const natl = byDate.find(e => (e.ratingNational ?? 0) > 0);
  const source = intl ?? natl ?? null;
  return {
    international: intl ? Math.round(intl.ratingBefore! + (intl.ratingChange ?? 0)) : null,
    national: natl ? natl.ratingNational! : null,
    birthYear: history.birthYear ?? null,
    source: source ? { name: source.name, endDate: source.endDate } : null,
  };
}

type SideMode = 'player' | 'manual';

/** Eine Seite des Rechners. Alle Felder bleiben editierbar; die Auswahl fuellt sie nur vor. */
class Side {
  readonly mode = signal<SideMode>('player');
  readonly playerKey = signal<string | null>(null);
  readonly label = signal('');
  readonly loading = signal(false);
  readonly source = signal<DerivedRatings['source']>(null);
  readonly birthYear = signal<number | null>(null);

  readonly international = signal<number | null>(null);
  readonly national = signal<number | null>(null);
  readonly fideK = signal<number>(20);
  /** `null` = aus der nationalen Elo nach OeSB-Formel. */
  readonly nationalKOverride = signal<number | null>(null);
  /** Hat jemand den FIDE-K selbst gewaehlt? Dann folgt er der Elo nicht mehr. */
  fideKManual = false;

  readonly nationalK = computed(() => {
    const own = this.nationalKOverride();
    if (own !== null) return own;
    const r = this.national();
    return r === null ? null : Math.round(oesbKFactor(r) * 10) / 10;
  });

  fill(r: DerivedRatings, label: string): void {
    this.label.set(label);
    this.international.set(r.international);
    this.national.set(r.national);
    this.birthYear.set(r.birthYear);
    this.source.set(r.source);
    this.nationalKOverride.set(null);
    this.fideKManual = false;
    this.syncFideK();
  }

  clear(): void {
    this.fill({ international: null, national: null, birthYear: null, source: null }, '');
    this.playerKey.set(null);
  }

  syncFideK(): void {
    if (this.fideKManual) return;
    const r = this.international();
    this.fideK.set(r === null ? 20 : fideKFactor(r, this.birthYear()));
  }
}

const MIRROR: Record<GameResult, GameResult> = { win: 'loss', draw: 'draw', loss: 'win' };

/** Zahl aus einem Eingabefeld: leer/ungueltig → `null`. */
function parseRating(value: unknown, min = 1, max = 3500): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) && n >= min && n <= max ? n : null;
}

/**
 * Elo-Rechner: was eine Partie zwischen zwei Spielern international (FIDE) und national (OeSB)
 * theoretisch bringt — fuer Sieg, Remis und Niederlage.
 *
 * <p>Je Seite entweder einen Spieler waehlen (Spieler A = ich, vorausgewaehlt; Spieler B aus
 * Freunden und Verfolgten oder ueber die FIDE-Suche) oder Elo und K-Faktor selbst eintragen. Die
 * Auswahl fuellt nur vor — jedes Feld bleibt aenderbar, denn die Zahl aus dem Verlauf ist der
 * Stand der juengsten Spielerkarte, nicht unbedingt die heutige Liste. Formeln und Quellen:
 * `elo-calc.ts`.</p>
 */
@Component({
  selector: 'app-elo-calculator-dialog',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatButtonToggleModule, MatDialogModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressSpinnerModule, MatSelectModule, TranslatePipe,
  ],
  templateUrl: './elo-calculator-dialog.component.html',
  styleUrls: ['./elo-calculator-dialog.component.scss'],
})
export class EloCalculatorDialogComponent {
  readonly data = inject<EloCalcDialogData>(MAT_DIALOG_DATA);
  private readonly history = inject(TournamentHistoryService);
  private readonly profiles = inject(ProfileService);
  private readonly translate = inject(TranslateService);

  readonly sides = [new Side(), new Side()] as const;
  readonly results = GAME_RESULTS;
  readonly fideKFactors = FIDE_K_FACTORS;
  /** Die vier Ergebnisspalten: A FIDE, A national, B FIDE, B national. */
  readonly cells: { side: 0 | 1; system: 'fide' | 'national' }[] = [
    { side: 0, system: 'fide' }, { side: 0, system: 'national' },
    { side: 1, system: 'fide' }, { side: 1, system: 'national' },
  ];

  /** Zwischenspeicher der hier nachgeladenen Verlaeufe. */
  private readonly cache: Record<string, PlayerHistory> = { ...this.data.loaded };

  // ----- FIDE-Suche fuer Spieler B -----
  searchName = '';
  readonly searching = signal(false);
  readonly searchHits = signal<PlayerSearchItem[] | null>(null);
  readonly searchFailed = signal(false);

  constructor() {
    const [a, b] = this.sides;
    if (this.data.meKey && this.data.players.some(p => p.key === this.data.meKey)) {
      this.choosePlayer(a, this.data.meKey);
    } else {
      a.mode.set('manual');
    }
    // B: ein zweiter bekannter Spieler waere geraten — B bleibt leer, bis jemand waehlt.
    if (this.data.players.filter(p => p.key !== this.data.meKey).length === 0) b.mode.set('manual');
  }

  playersFor(index: number): EloCalcPlayer[] {
    // A darf auch jemand anderes sein; B soll nicht A sein.
    const other = this.sides[index === 0 ? 1 : 0].playerKey();
    return this.data.players.filter(p => p.key !== other || other === null);
  }

  setMode(side: Side, mode: SideMode): void {
    side.mode.set(mode);
    if (mode === 'manual') {
      side.playerKey.set(null);
      side.source.set(null);
    }
  }

  choosePlayer(side: Side, key: string | null): void {
    side.playerKey.set(key);
    const player = this.data.players.find(p => p.key === key);
    if (!player) { side.clear(); return; }

    const cached = this.cache[key!];
    if (cached) { side.fill(deriveRatings(cached), player.label); return; }

    side.loading.set(true);
    this.loadHistory(player).subscribe(history => {
      side.loading.set(false);
      if (side.playerKey() !== key) return;   // inzwischen anders gewaehlt
      if (history) this.cache[key!] = history;
      side.fill(deriveRatings(history), player.label);
    });
  }

  private loadHistory(player: EloCalcPlayer): Observable<PlayerHistory | null> {
    const source = player.kind === 'tracked'
      ? this.history.trackedHistory(player.id)
      : this.history.get([player.id]).pipe(map(list => list.find(h => h.userId === player.id) ?? null));
    return source.pipe(catchError(() => of(null)));
  }

  search(): void {
    const name = this.searchName.trim();
    if (name.length < 2 || this.searching()) return;
    const comma = name.indexOf(',');
    const last = (comma >= 0 ? name.slice(0, comma) : name).trim();
    const first = comma >= 0 ? name.slice(comma + 1).trim() : '';
    this.searching.set(true);
    this.searchFailed.set(false);
    this.searchHits.set(null);
    this.profiles.searchPlayer<PlayerSearchResult>(last, first || undefined).subscribe({
      next: found => {
        this.searchHits.set(found.fideResults ?? []);
        this.searching.set(false);
      },
      error: () => {
        this.searchFailed.set(true);
        this.searching.set(false);
      },
    });
  }

  /** Ein FIDE-Treffer fuellt die internationale Wertung; die nationale bleibt einzutragen. */
  chooseHit(side: Side, hit: PlayerSearchItem): void {
    side.playerKey.set(null);
    side.fill({
      international: hit.elo && hit.elo > 0 ? hit.elo : null, national: null, birthYear: null, source: null,
    }, hit.name);
    this.searchHits.set(null);
  }

  // ----- Eingaben -----
  setInternational(side: Side, value: unknown): void {
    side.international.set(parseRating(value));
    side.syncFideK();
  }

  setNational(side: Side, value: unknown): void {
    side.national.set(parseRating(value));
  }

  setFideK(side: Side, k: number): void {
    side.fideKManual = true;
    side.fideK.set(k);
  }

  setNationalK(side: Side, value: unknown): void {
    side.nationalKOverride.set(parseRating(value, 0.1, 100));
  }

  // ----- Ergebnis -----
  private locale(): string {
    return this.translate.currentLang() || 'en';
  }

  /** Aenderung fuer Seite `index` bei Ergebnis `result` AUS SICHT VON A; `null` ohne beide Wertungen. */
  change(index: 0 | 1, system: 'fide' | 'national', result: GameResult): string | null {
    const own = this.sides[index];
    const opp = this.sides[index === 0 ? 1 : 0];
    const mine = index === 0 ? result : MIRROR[result];
    if (system === 'fide') {
      const r = own.international(), o = opp.international();
      return r === null || o === null ? null : formatChange(fideChange(r, o, own.fideK(), mine), this.locale());
    }
    const r = own.national(), o = opp.national(), k = own.nationalK();
    return r === null || o === null || k === null
      ? null : formatChange(oesbChange(r, o, k, mine), this.locale());
  }

  sideLabel(index: 0 | 1): string {
    return this.sides[index].label()
      || this.translate.instant(index === 0 ? 'turnier.history.elo.playerA' : 'turnier.history.elo.playerB');
  }
}
