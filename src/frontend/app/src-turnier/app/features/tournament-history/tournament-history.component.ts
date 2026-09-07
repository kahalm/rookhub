import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription, catchError, of, switchMap, takeWhile, timer } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { OpenTournamentService } from '../../core/open-tournament.service';
import { HISTORY_SPEEDS, HistoryFriend, HistorySpeed, PlayerHistory, PlayerHistoryEntry, SpeedSummary } from './tournament-history.model';
import { TournamentHistoryService } from './tournament-history.service';

/**
 * Ab welcher Zahl eine Performance eine Aussage ist. chess-results traegt eine 0 ein, wenn es sie
 * nicht berechnet — als Wertung gelesen zieht sie jeden Schnitt nach unten.
 */
const MinPlausiblePerformance = 500;

/**
 * Ein Reiter: ein KONTO. Der eigene steht vorn, danach die Freunde.
 *
 * <p>Freunde ohne Namen im Profil bekommen ihren Reiter trotzdem — nur gesperrt: ohne Nachnamen
 * gibt es keine chess-results-Spielersuche und damit keinen Verlauf. Sie ganz wegzulassen war der
 * frühere Zustand und hinterliess eine Auswahl, die ohne Grund leer war.</p>
 */
export interface HistoryTab {
  userId: number;
  label: string;
  /** Ist etwas zu holen? `false` = kein Nachname im Profil. */
  enabled: boolean;
  /** Traegt das Profil eine Kennung? Sonst sind Namensgleiche mit dabei. */
  exact: boolean;
}

/**
 * Der Turnierverlauf: gespielte und kommende Turniere, je mit Platz, Punkten und
 * Performance-Rating. Umschaltbar auf Freunde — alle oder einzeln.
 *
 * <p><b>Warum sich die Seite selbst nachlaedt.</b> Der Server holt die Turnier-LISTE beim Aufruf
 * (ein Abruf) und die ERGEBNISSE im Hintergrund (einer je Turnier). Die Tabelle steht damit
 * sofort — mit Termin und Platz, also dem, was man beim Ueberfliegen sucht — und die Punkte
 * tropfen nach. Die Antwort sagt, wie viele noch fehlen; solange die Zahl groesser als null ist,
 * fragt die Seite nach. Ohne das saehe man eine halbe Tabelle und hielte sie fuer endgueltig.</p>
 *
 * <p><b>Alles aus HTTP liegt in Signalen</b> — dieselbe Regel wie im Turnierkalender: Angulars
 * `fetch`-Pfad traegt die Zone nicht durch den Antwort-Strom, eine Feldzuweisung im Abonnenten
 * loeste also keine Aenderungserkennung aus (siehe TODO.md).</p>
 */
@Component({
  selector: 'app-tournament-history',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatButtonToggleModule, MatCardModule,
    MatIconModule, MatTabsModule, MatTooltipModule, RouterLink, TranslatePipe,
    LoadingSpinnerComponent, HelpHintComponent,
  ],
  templateUrl: './tournament-history.component.html',
  styleUrls: ['./tournament-history.component.scss'],
})
export class TournamentHistoryComponent implements OnInit {
  private readonly history = inject(TournamentHistoryService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly opener = inject(OpenTournamentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /** Welches Turnier gerade geoeffnet wird — der Dienst fuehrt das, die Ansicht zeigt es nur. */
  readonly opening = this.opener.opening;

  /**
   * Die schon geladenen Verlaeufe, nach Konto. Ein einmal geoeffneter Reiter bleibt damit beim
   * Zurueckwechseln sofort da — und jeder Reiter kostet einen eigenen Abruf, der sich so nicht
   * wiederholt.
   */
  readonly loaded = signal<Record<number, PlayerHistory>>({});
  readonly friends = signal<HistoryFriend[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);

  /** Welcher Reiter offen ist — `null`, solange die eigene Kennung nicht feststeht. */
  readonly activeUserId = signal<number | null>(null);

  /** Der Verlauf des offenen Reiters. */
  readonly current = computed(() => {
    const id = this.activeUserId();
    return id === null ? null : this.loaded()[id] ?? null;
  });

  /**
   * Ein Reiter je Konto: ich zuerst, danach die Freunde. Gesperrte (kein Name im Profil) bleiben
   * sichtbar — mit Grund, statt kommentarlos zu fehlen.
   */
  readonly tabs = computed<HistoryTab[]>(() => {
    const me = this.auth.currentUser;
    const mine: HistoryTab[] = me
      ? [{ userId: me.userId, label: this.translate.instant('turnier.history.onlyMe'), enabled: true, exact: true }]
      : [];

    return [
      ...mine,
      ...this.friends().map(f => ({
        userId: f.userId, label: f.displayName, enabled: f.hasName, exact: f.exact,
      })),
    ];
  });

  /** Der Index des offenen Reiters — was `mat-tab-group` braucht. */
  readonly activeIndex = computed(() => {
    const id = this.activeUserId();
    const index = this.tabs().findIndex(t => t.userId === id);
    return index < 0 ? 0 : index;
  });

  /**
   * Auf welche Bedenkzeit die Ansicht eingeschraenkt ist. `null` = alle.
   *
   * <p>Der Filter greift ueberall gleich: Liste, Jahresgruppen UND Summen. Eine Zeile, die eine
   * andere Menge zusammenfasst als die Tabelle darunter, ist schlimmer als kein Filter.</p>
   */
  readonly speedFilter = signal<HistorySpeed | null>(null);

  /** Nur die Klassen anbieten, in denen dieses Konto ueberhaupt gespielt hat. */
  readonly availableSpeeds = computed(() => {
    const history = this.current();
    if (!history) return [];
    const present = new Set(history.entries.map(e => e.speed));
    return HISTORY_SPEEDS.filter(s => present.has(s));
  });

  onSpeedFilter(speed: HistorySpeed | null): void {
    this.speedFilter.set(speed);
  }

  /** Die Eintraege, die der Filter durchlaesst. */
  private filtered(entries: PlayerHistoryEntry[]): PlayerHistoryEntry[] {
    const speed = this.speedFilter();
    return speed === null ? entries : entries.filter(e => e.speed === speed);
  }

  /** Schluessel des gemerkten Reiters — sonst faengt man nach jedem Besuch wieder bei sich an. */
  static readonly ViewKey = 'rh.turnier.historyView';

  ngOnInit(): void {
    const me = this.auth.currentUser?.userId ?? null;
    const remembered = this.restore();

    // Der EIGENE Verlauf laedt sofort — er ist der erste Reiter und der haeufige Fall. Auf die
    // Freundesliste zu warten hiesse, die eigene Tabelle hinter einem zweiten Abruf zu verstecken.
    const start = remembered ?? me;
    if (start !== null) this.select(start);

    this.history.friends().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: friends => {
        this.friends.set(friends);
        // Der gemerkte Reiter kann inzwischen weg sein (Freundschaft aufgeloest) — dann zurueck
        // auf den eigenen, statt auf einen Reiter zu zeigen, den es nicht gibt.
        if (remembered !== null && remembered !== me && !friends.some(f => f.userId === remembered && f.hasName)) {
          if (me !== null) this.select(me);
        }
      },
      error: () => {
        // Ohne Freundesliste bleibt der eigene Verlauf — die Reiter fehlen dann eben.
        this.friends.set([]);
        if (remembered !== null && remembered !== me && me !== null) this.select(me);
      },
    });
  }

  // ----- Reiter -----------------------------------------------------------

  onTabChange(index: number): void {
    const tab = this.tabs()[index];
    if (tab && tab.userId !== this.activeUserId()) this.select(tab.userId);
  }

  /** Reiter oeffnen: gemerkte Fassung sofort zeigen, dann frisch laden. */
  private select(userId: number): void {
    this.activeUserId.set(userId);
    this.store(userId);
    this.load(userId);
  }

  // ----- Laden ------------------------------------------------------------

  private pollSubscription?: Subscription;

  /**
   * Zaehler gegen ueberholte Antworten: wer waehrend eines laufenden Abrufs umschaltet, bekommt
   * sonst die Antwort der alten Auswahl in die Tabelle.
   */
  private generation = 0;

  /**
   * Den Verlauf EINES Kontos laden. Ein Reiter = ein Abruf: „alle Freunde auf einmal" hiesse, fuer
   * jedes Konto eine Trefferliste bei chess-results zu holen, auch fuer die, die niemand ansieht.
   */
  load(userId: number): void {
    // Schon geladen? Dann bleibt die Tabelle stehen und wird nur aufgefrischt — sonst blitzt bei
    // jedem Reiterwechsel ein Ladebalken ueber einer Ansicht auf, die es schon gibt.
    this.loading.set(this.loaded()[userId] === undefined);
    this.failed.set(false);
    this.pollSubscription?.unsubscribe();

    const generation = ++this.generation;
    this.history.get([userId]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: histories => {
        if (generation !== this.generation) return;
        this.remember(histories);
        this.loading.set(false);
        this.schedulePoll(generation, userId);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  /** Antworten in den Zwischenspeicher legen — je Konto eine Zeile. */
  private remember(histories: PlayerHistory[]): void {
    if (histories.length === 0) return;
    this.loaded.update(current => {
      const next = { ...current };
      for (const history of histories) next[history.userId] = history;
      return next;
    });
  }

  /** Wie lange bis zur naechsten Nachfrage, solange Ergebnisse fehlen. */
  private static readonly PollMs = 4000;

  /** Wie oft nachgefragt wird, bevor aufgegeben wird — sonst laeuft die Seite endlos. */
  private static readonly MaxPolls = 15;

  private polls = 0;

  private schedulePoll(generation: number, userId: number): void {
    if (this.pending() === 0) { this.polls = 0; return; }
    if (this.polls >= TournamentHistoryComponent.MaxPolls) return;

    this.polls++;
    this.pollSubscription = timer(TournamentHistoryComponent.PollMs)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (generation !== this.generation) return;
        this.history.get([userId])
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: histories => {
              if (generation !== this.generation) return;
              this.remember(histories);
              this.schedulePoll(generation, userId);
            },
            error: () => { /* still: der naechste Versuch kommt beim naechsten Laden */ },
          });
      });
  }

  /** Wie viele Ergebnisse im OFFENEN Reiter noch fehlen. */
  readonly pending = computed(() => this.current()?.pending ?? 0);

  // ----- Aufteilung -------------------------------------------------------

  /**
   * Kuenftige zuerst, danach die gespielten. Beides in EINER Tabelle waere unlesbar: „Platz 56
   * von 56" und „noch nicht gespielt" sind verschiedene Arten von Zeile.
   */
  upcoming(history: PlayerHistory): PlayerHistoryEntry[] {
    const today = this.today();
    return this.filtered(history.entries)
      .filter(e => e.endDate !== null && e.endDate >= today)
      .sort((a, b) => (a.endDate ?? '').localeCompare(b.endDate ?? ''));
  }

  past(history: PlayerHistory): PlayerHistoryEntry[] {
    const today = this.today();
    return this.filtered(history.entries).filter(e => e.endDate === null || e.endDate < today);
  }

  private today(): string {
    const now = new Date();
    const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
  }

  /**
   * Die gespielten Turniere nach JAHREN, neueste zuerst — und je Jahr dieselbe Auswertung wie
   * oben. Eine ungeteilte Liste ueber Jahre hinweg beantwortet die Frage nicht, um die es hier
   * geht („wie lief die Saison"), und eine Zahl ueber alles erst recht nicht.
   */
  years(history: PlayerHistory): { year: string; entries: PlayerHistoryEntry[]; speeds: SpeedSummary[] }[] {
    const groups = new Map<string, PlayerHistoryEntry[]>();
    for (const entry of this.past(history)) {
      // Ohne Datum gibt es kein Jahr — solche Zeilen kommen ans Ende, in eine eigene Gruppe.
      const year = entry.endDate ? entry.endDate.slice(0, 4) : '';
      (groups.get(year) ?? groups.set(year, []).get(year)!).push(entry);
    }

    return [...groups.entries()]
      .sort((a, b) => b[0].localeCompare(a[0]))
      .map(([year, entries]) => ({ year, entries, speeds: this.speedSummaries(entries) }));
  }

  /**
   * Wie viele Turniere und welche mittlere Performance je Bedenkzeit-Klasse.
   *
   * <p><b>Warum die Gesamtpunkte hier nicht mehr stehen.</b> „57 Punkte" ueber alle Turniere
   * hinweg addiert Blitzpartien zu Turnierpartien und Fuenfrundige zu Elfrundigen — die Zahl
   * wuchs mit der Zeit und sagte sonst nichts. Die Performance je Klasse dagegen ist genau die
   * Auskunft, die man sucht, und sie ist NUR getrennt lesbar: 1900 im Blitz und 1900 im
   * Turnierschach sind nicht dieselbe Leistung.</p>
   *
   * <p>Klassen ohne ein einziges Turnier fallen weg; eine Klasse mit Turnieren, aber ohne
   * gewertete Performance, bleibt mit ihrer Zahl stehen (der Unterschied zwischen „nicht
   * gespielt" und „keine Wertung" gehoert nicht verwischt).</p>
   *
   * <p><b>Turniere ohne bekannte Bedenkzeit zaehlen als eigene Gruppe</b> statt herauszufallen:
   * die Angabe steht auf einer eigenen Seite, die erst der naechtliche Durchgang holt, und manche
   * Turniere nennen gar keine. Ohne diese Gruppe waere die Uebersicht direkt nach einem Deploy
   * leer — die Performance verschwaende, bloss weil die Einordnung noch fehlt.</p>
   */
  speedSummaries(entries: PlayerHistoryEntry[]): SpeedSummary[] {
    const played = entries.filter(e => e.hasResult);

    return HISTORY_SPEEDS.map(speed => {
      const mine = played.filter(e => e.speed === speed);
      // Eine Performance unter 500 gibt es nicht — chess-results schreibt dort eine 0, wenn es
      // sie NICHT berechnet hat (Gegner ohne Wertung, sehr wenige Partien, 0 % oder 100 %). Der
      // Server raeumt solche Werte inzwischen weg; die Ansicht rechnet sie zusaetzlich nicht mit,
      // damit ein alter Bestand keinen Schnitt verdirbt.
      const rated = mine.filter(e => (e.performanceRating ?? 0) >= MinPlausiblePerformance);
      // Partien nur summieren, wo eine Karte sie kennt — sonst zaehlte ein Turnier ohne Angabe
      // als null Partien und die Summe waere stillschweigend zu klein.
      const counted = mine.filter(e => e.gamesPlayed !== null);
      return {
        speed,
        played: mine.length,
        games: counted.length === 0 ? null : counted.reduce((sum, e) => sum + (e.gamesPlayed ?? 0), 0),
        performance: rated.length === 0
          ? null
          : Math.round(rated.reduce((sum, e) => sum + (e.performanceRating ?? 0), 0) / rated.length),
      };
    }).filter(s => s.played > 0);
  }

  /**
   * Die PARTIEN in einer Klammer — mehr nicht.
   *
   * <p>Hier stand zusaetzlich die Zahl der Turniere je Klasse, und die Zeile las sich damit als
   * „12 Turniere · 67 Partien" hinter jeder einzelnen Bedenkzeit. Das ist dreimal dieselbe
   * Buchhaltung nebeneinander; die Gesamtzahl der Turniere steht ohnehin am Anfang der Zeile, und
   * innerhalb einer Klasse ist die PARTIENzahl die aussagekraeftigere Groesse (fuenf
   * Wochenend-Opens sind fuenf Turniere und rund 25 Partien, eine Ligasaison ein Turnier und drei
   * Partien).</p>
   *
   * <p>Kennt noch keine Karte die Partienzahl, bleibt die Klammer WEG statt eine erfundene Null
   * zu zeigen.</p>
   */
  counts(summary: SpeedSummary): string {
    if (summary.games === null) return '';
    return `(${this.translate.instant('turnier.history.countGames', { count: summary.games })})`;
  }

  /**
   * Die Summe der gespielten Turniere je Konto: Anzahl und Performance JE KLASSE.
   */
  summary(history: PlayerHistory): { played: number; speeds: SpeedSummary[] } {
    const past = this.past(history);
    return {
      played: past.filter(e => e.hasResult).length,
      speeds: this.speedSummaries(past),
    };
  }

  /**
   * Ein Klick fuehrt auf das TURNIER, nicht ins Verzeichnis.
   *
   * <p>Vorher ging er auf `/tournaments/calendar/{id}` — und landete bei der Mehrheit der
   * Verlaufs-Eintraege auf „steht (noch) nicht im Verzeichnis". Das ist kein Zufall und heilt
   * auch nicht von selbst: der naechtliche Sweep liest nur das Fenster von 30 Tagen zurueck bis
   * 18 Monate voraus. Ein Turnier, das man 2024 gespielt hat, wird dort NIE stehen.</p>
   *
   * <p>Der Ablauf (nachsehen, einreihen, nachfragen, Deckel) liegt im
   * <see cref="OpenTournamentService"/> — die Merkliste braucht denselben, und zweimal getippt
   * waere es derselbe Poll-Mechanismus an zwei Stellen.</p>
   */
  open(entry: PlayerHistoryEntry): void {
    this.opener.open(entry.chessResultsId);
  }

  trackById = (_: number, entry: PlayerHistoryEntry) => entry.chessResultsId;
  trackByTab = (_: number, tab: HistoryTab) => tab.userId;

  // ----- Gemerkte Auswahl -------------------------------------------------

  private store(userId: number): void {
    try {
      localStorage.setItem(TournamentHistoryComponent.ViewKey, JSON.stringify({ userId }));
    } catch {
      // Gesperrter oder voller Speicher (Privatmodus) ist kein Grund, die Seite scheitern zu
      // lassen — dann faengt man eben wieder beim eigenen Reiter an.
    }
  }

  /** Der zuletzt geoeffnete Reiter, oder `null`. */
  private restore(): number | null {
    try {
      const raw = localStorage.getItem(TournamentHistoryComponent.ViewKey);
      const stored = raw ? JSON.parse(raw) : null;
      return stored && typeof stored.userId === 'number' ? stored.userId : null;
    } catch {
      // unlesbar/kaputt: der eigene Reiter bleibt die Vorgabe
      return null;
    }
  }
}
