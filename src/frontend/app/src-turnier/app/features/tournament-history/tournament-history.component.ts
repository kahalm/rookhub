import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable, Subscription, catchError, map, of, switchMap, takeWhile, timer } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { OpenTournamentService } from '../../core/open-tournament.service';
import { HISTORY_SPEEDS, HistoryFriend, HistorySpeed, PlayerHistory, PlayerHistoryEntry, SpeedSummary, TrackedPlayer } from './tournament-history.model';
import { TournamentHistoryService } from './tournament-history.service';
import { TrackPlayerDialogComponent } from './track-player-dialog.component';

/**
 * Ab welcher Zahl eine Performance eine Aussage ist. chess-results traegt eine 0 ein, wenn es sie
 * nicht berechnet — als Wertung gelesen zieht sie jeden Schnitt nach unten.
 */
const MinPlausiblePerformance = 500;

/**
 * Ein Reiter: ein KONTO oder ein verfolgter SPIELER. Der eigene steht vorn, danach die Freunde,
 * danach die verfolgten.
 *
 * <p>Freunde ohne Namen im Profil bekommen ihren Reiter trotzdem — nur gesperrt: ohne Nachnamen
 * gibt es keine chess-results-Spielersuche und damit keinen Verlauf. Sie ganz wegzulassen war der
 * frühere Zustand und hinterliess eine Auswahl, die ohne Grund leer war.</p>
 *
 * <p><b>Der Schluessel ist zusammengesetzt</b> (`u:12` / `t:3`), nicht die blosse Zahl: eine
 * Konto-Kennung und die Kennung eines Verfolgt-Eintrags kommen aus verschiedenen Toepfen und
 * kollidieren zwangslaeufig — mit der Zahl allein zeigten zwei Reiter auf denselben Zustand.</p>
 */
export interface HistoryTab {
  /** Eindeutig ueber beide Arten: `u:<Konto>` bzw. `t:<verfolgter Eintrag>`. */
  key: string;
  kind: 'account' | 'tracked';
  /** Konto-Kennung bzw. Kennung des Verfolgt-Eintrags. */
  id: number;
  label: string;
  /** Ist etwas zu holen? `false` = kein Nachname im Profil. */
  enabled: boolean;
  /** Traegt der Eintrag eine Kennung? Sonst sind Namensgleiche mit dabei. */
  exact: boolean;
}

/** Der Reiter-Schluessel eines Kontos bzw. eines verfolgten Spielers. */
export const accountKey = (userId: number): string => `u:${userId}`;
export const trackedKey = (id: number): string => `t:${id}`;

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
    MatDialogModule, MatIconModule, MatTabsModule, MatTooltipModule, RouterLink, TranslatePipe,
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
  private readonly dialog = inject(MatDialog);

  /** Welches Turnier gerade geoeffnet wird — der Dienst fuehrt das, die Ansicht zeigt es nur. */
  readonly opening = this.opener.opening;

  /**
   * Die schon geladenen Verlaeufe, nach Reiter-SCHLUESSEL. Ein einmal geoeffneter Reiter bleibt
   * damit beim Zurueckwechseln sofort da — und jeder Reiter kostet einen eigenen Abruf, der sich
   * so nicht wiederholt.
   */
  readonly loaded = signal<Record<string, PlayerHistory>>({});
  readonly friends = signal<HistoryFriend[]>([]);

  /** Die verfolgten Spieler — Leute ohne Konto hier, deren Verlauf man mitliest. */
  readonly tracked = signal<TrackedPlayer[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);

  /** Welcher Reiter offen ist — `null`, solange die eigene Kennung nicht feststeht. */
  readonly activeKey = signal<string | null>(null);

  /** Der Verlauf des offenen Reiters. */
  readonly current = computed(() => {
    const key = this.activeKey();
    return key === null ? null : this.loaded()[key] ?? null;
  });

  /**
   * Ein Reiter je Konto und je verfolgtem Spieler: ich zuerst, danach die Freunde, danach die
   * Verfolgten. Gesperrte (kein Name im Profil) bleiben sichtbar — mit Grund, statt kommentarlos
   * zu fehlen.
   */
  readonly tabs = computed<HistoryTab[]>(() => {
    const me = this.auth.currentUser;
    const mine: HistoryTab[] = me
      ? [{
          key: accountKey(me.userId), kind: 'account', id: me.userId,
          label: this.translate.instant('turnier.history.onlyMe'), enabled: true, exact: true,
        }]
      : [];

    return [
      ...mine,
      ...this.friends().map(f => ({
        key: accountKey(f.userId), kind: 'account' as const, id: f.userId,
        label: f.displayName, enabled: f.hasName, exact: f.exact,
      })),
      ...this.tracked().map(t => ({
        key: trackedKey(t.id), kind: 'tracked' as const, id: t.id,
        label: t.displayName, enabled: true, exact: t.exact,
      })),
    ];
  });

  /** Der Index des offenen Reiters — was `mat-tab-group` braucht. */
  readonly activeIndex = computed(() => {
    const key = this.activeKey();
    const index = this.tabs().findIndex(t => t.key === key);
    return index < 0 ? 0 : index;
  });

  /** Der offene Reiter — die Ansicht braucht ihn fuer „nicht mehr verfolgen". */
  readonly activeTab = computed(() => this.tabs().find(t => t.key === this.activeKey()) ?? null);

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
    const mineKey = me === null ? null : accountKey(me);
    const remembered = this.restore();

    // Der EIGENE Verlauf laedt sofort — er ist der erste Reiter und der haeufige Fall. Auf die
    // Freundesliste zu warten hiesse, die eigene Tabelle hinter einem zweiten Abruf zu verstecken.
    const start = remembered ?? mineKey;
    if (start !== null) this.select(start);

    this.history.friends().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: friends => {
        this.friends.set(friends);
        this.fallBackIfGone(remembered, mineKey);
      },
      error: () => {
        // Ohne Freundesliste bleibt der eigene Verlauf — die Reiter fehlen dann eben.
        this.friends.set([]);
        this.fallBackIfGone(remembered, mineKey);
      },
    });

    this.history.tracked().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: tracked => {
        this.tracked.set(tracked);
        this.fallBackIfGone(remembered, mineKey);
      },
      error: () => {
        this.tracked.set([]);
        this.fallBackIfGone(remembered, mineKey);
      },
    });
  }

  /**
   * Der gemerkte Reiter kann inzwischen weg sein — Freundschaft aufgeloest, Spieler nicht mehr
   * verfolgt. Dann zurueck auf den eigenen, statt auf einen Reiter zu zeigen, den es nicht gibt.
   *
   * <p>Geprueft wird erst, wenn BEIDE Listen da sind: die Antworten kommen in beliebiger
   * Reihenfolge, und wer nach der ersten urteilt, wirft einen gemerkten Reiter weg, den die
   * zweite gerade mitbringt.</p>
   */
  private listsSeen = 0;

  private fallBackIfGone(remembered: string | null, mineKey: string | null): void {
    if (++this.listsSeen < 2) return;
    if (remembered === null || remembered === mineKey || mineKey === null) return;

    const stillThere = this.tabs().some(t => t.key === remembered && t.enabled);
    if (!stillThere) this.select(mineKey);
  }

  // ----- Reiter -----------------------------------------------------------

  onTabChange(index: number): void {
    const tab = this.tabs()[index];
    if (tab && tab.key !== this.activeKey()) this.select(tab.key);
  }

  /** Reiter oeffnen: gemerkte Fassung sofort zeigen, dann frisch laden. */
  private select(key: string): void {
    this.activeKey.set(key);
    this.store(key);
    this.load(key);
  }

  // ----- Verfolgte Spieler ------------------------------------------------

  /**
   * „+": einen beliebigen Spieler verfolgen.
   *
   * <p>Der Verlauf hing bis hierher an KONTEN — dem eigenen und denen angenommener Freunde. Die
   * Leute, deren Ergebnisse man wirklich verfolgt, haben aber meist gar kein Konto hier: das
   * eigene Kind, ein Vereinskamerad, der Gegner der naechsten Runde. Sie einzuladen, damit man
   * ihre oeffentlich auf chess-results stehenden Turniere sehen kann, ist keine Loesung.</p>
   */
  addTracked(): void {
    this.dialog.open(TrackPlayerDialogComponent, { width: '560px', maxWidth: '96vw' })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((player?: TrackedPlayer) => {
        if (!player) return;
        // Schon verfolgt? Der Server gibt denselben Eintrag zurueck — dann nur hinspringen.
        this.tracked.update(list =>
          list.some(t => t.id === player.id) ? list : [...list, player]);
        this.select(trackedKey(player.id));
      });
  }

  /**
   * Nicht mehr verfolgen — mit Rueckgaengig, weil der Knopf direkt neben dem Namen sitzt und ein
   * Fehlgriff sonst eine neue Suche kostet. Der geholte Verlauf bleibt ohnehin liegen; entfernt
   * wird der Reiter.
   */
  removeTracked(tab: HistoryTab): void {
    const player = this.tracked().find(t => t.id === tab.id);
    if (!player) return;

    this.history.untrack(player.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.tracked.update(list => list.filter(t => t.id !== player.id));
        this.loaded.update(current => {
          const next = { ...current };
          delete next[trackedKey(player.id)];
          return next;
        });

        const me = this.auth.currentUser?.userId ?? null;
        if (me !== null) this.select(accountKey(me));

        this.snackbar
          .show(this.translate.instant('turnier.history.track.removed', { name: player.displayName }),
                { action: 'common.undo', duration: 6000 })
          .onAction()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe(() => this.restoreTracked(player));
      },
      error: () => this.snackbar.warn(this.translate.instant('turnier.history.track.removeError')),
    });
  }

  /** Rueckgaengig: denselben Spieler noch einmal anlegen — die Kennung macht daraus denselben Reiter. */
  private restoreTracked(player: TrackedPlayer): void {
    this.history.track({
      lastName: player.lastName,
      firstName: player.firstName,
      fideId: player.fideId,
      chessResultsId: player.chessResultsId,
      displayName: player.displayName,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: restored => {
        this.tracked.update(list =>
          list.some(t => t.id === restored.id) ? list : [...list, restored]);
        this.select(trackedKey(restored.id));
      },
      error: () => this.snackbar.warn(this.translate.instant('turnier.history.track.addError')),
    });
  }

  // ----- Laden ------------------------------------------------------------

  private pollSubscription?: Subscription;

  /**
   * Zaehler gegen ueberholte Antworten: wer waehrend eines laufenden Abrufs umschaltet, bekommt
   * sonst die Antwort der alten Auswahl in die Tabelle.
   */
  private generation = 0;

  /**
   * Den Verlauf EINES Reiters laden. Ein Reiter = ein Abruf: „alle Freunde auf einmal" hiesse, fuer
   * jedes Konto eine Trefferliste bei chess-results zu holen, auch fuer die, die niemand ansieht.
   */
  load(key: string): void {
    // Schon geladen? Dann bleibt die Tabelle stehen und wird nur aufgefrischt — sonst blitzt bei
    // jedem Reiterwechsel ein Ladebalken ueber einer Ansicht auf, die es schon gibt.
    this.loading.set(this.loaded()[key] === undefined);
    this.failed.set(false);
    this.pollSubscription?.unsubscribe();

    const generation = ++this.generation;
    this.fetch(key).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: history => {
        if (generation !== this.generation) return;
        this.remember(key, history);
        this.loading.set(false);
        this.schedulePoll(generation, key);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  /**
   * Woher der Verlauf dieses Reiters kommt. Ein KONTO geht ueber die Konten-Abfrage (dieselbe, die
   * Freunde prueft), ein verfolgter Spieler ueber seinen eigenen Weg — dort ist die Zahl ein
   * Eintrag der eigenen Liste und kein Konto.
   */
  private fetch(key: string): Observable<PlayerHistory | null> {
    const [kind, raw] = key.split(':');
    const id = Number(raw);
    if (!Number.isFinite(id)) return of(null);

    return kind === 't'
      ? this.history.trackedHistory(id)
      : this.history.get([id]).pipe(map(histories => histories[0] ?? null));
  }

  /** Antwort in den Zwischenspeicher legen — je Reiter eine Zeile. */
  private remember(key: string, history: PlayerHistory | null): void {
    if (!history) return;
    this.loaded.update(current => ({ ...current, [key]: history }));
  }

  /** Wie lange bis zur naechsten Nachfrage, solange Ergebnisse fehlen. */
  private static readonly PollMs = 4000;

  /** Wie oft nachgefragt wird, bevor aufgegeben wird — sonst laeuft die Seite endlos. */
  private static readonly MaxPolls = 15;

  private polls = 0;

  private schedulePoll(generation: number, key: string): void {
    if (this.pending() === 0) { this.polls = 0; return; }
    if (this.polls >= TournamentHistoryComponent.MaxPolls) return;

    this.polls++;
    this.pollSubscription = timer(TournamentHistoryComponent.PollMs)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (generation !== this.generation) return;
        this.fetch(key)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: history => {
              if (generation !== this.generation) return;
              this.remember(key, history);
              this.schedulePoll(generation, key);
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
  trackByTab = (_: number, tab: HistoryTab) => tab.key;

  // ----- Gemerkte Auswahl -------------------------------------------------

  private store(key: string): void {
    try {
      localStorage.setItem(TournamentHistoryComponent.ViewKey, JSON.stringify({ tab: key }));
    } catch {
      // Gesperrter oder voller Speicher (Privatmodus) ist kein Grund, die Seite scheitern zu
      // lassen — dann faengt man eben wieder beim eigenen Reiter an.
    }
  }

  /**
   * Der zuletzt geoeffnete Reiter, oder `null`.
   *
   * <p>Ein alter Eintrag traegt noch `{ userId }` — er wird gelesen und gilt als Konto-Reiter.
   * Ihn zu verwerfen hiesse, jeden Nutzer nach dem Deploy einmal grundlos auf den eigenen Reiter
   * zurueckzusetzen.</p>
   */
  private restore(): string | null {
    try {
      const raw = localStorage.getItem(TournamentHistoryComponent.ViewKey);
      const stored = raw ? JSON.parse(raw) : null;
      if (stored && typeof stored.tab === 'string') return stored.tab;
      if (stored && typeof stored.userId === 'number') return accountKey(stored.userId);
      return null;
    } catch {
      // unlesbar/kaputt: der eigene Reiter bleibt die Vorgabe
      return null;
    }
  }
}
