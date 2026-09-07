import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription, catchError, of, switchMap, takeWhile, timer } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentListService } from '../../core/tournament-list.service';
import { HistoryFriend, PlayerHistory, PlayerHistoryEntry } from './tournament-history.model';
import { TournamentHistoryService } from './tournament-history.service';

/** Wessen Verlauf gezeigt wird. */
type Whose = 'me' | 'all' | 'pick';

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
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatChipsModule,
    MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule, RouterLink,
    TranslatePipe,
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
  private readonly tournaments = inject(TournamentListService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /**
   * Welches Turnier gerade geoeffnet wird (chess-results-Nummer) — sperrt weitere Klicks und
   * traegt die Wartezeit-Anzeige.
   */
  readonly opening = signal<string | null>(null);

  /** Wie lange auf einen Holen-Auftrag gewartet wird: 4 s x 30 = rund zwei Minuten. */
  private static readonly MaxImportPolls = 30;

  readonly histories = signal<PlayerHistory[]>([]);
  readonly friends = signal<HistoryFriend[]>([]);

  /**
   * Die Freunde, mit denen sich wirklich vergleichen laesst. Die Liste enthaelt bewusst AUCH die
   * ohne Namen im Profil (sonst stand dort nichts und niemand wusste warum), aber auswaehlbar
   * sind nur diese hier — und an ihnen haengt, ob die Umschaltung ueberhaupt etwas anbietet.
   */
  readonly selectableFriends = computed(() => this.friends().filter(f => f.hasName));
  readonly loading = signal(true);
  readonly failed = signal(false);

  whose: Whose = 'me';
  /** Bei `pick`: die ausgewaehlten Freunde. */
  picked: number[] = [];

  /** Schluessel der gemerkten Auswahl — sonst stellt man sie nach jedem Besuch neu ein. */
  static readonly ViewKey = 'rh.turnier.historyView';

  ngOnInit(): void {
    this.restore();

    // „Alle Freunde" braucht die LISTE, um zu wissen, wen es meint. Ist sie die gemerkte
    // Auswahl, muss deshalb erst sie da sein — sonst laedt die Seite beim Wiederkommen nur den
    // eigenen Verlauf und die gemerkte Auswahl waere wirkungslos.
    const needsFriends = this.whose === 'all';

    this.history.friends().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: friends => {
        this.friends.set(friends);
        if (needsFriends) this.load();
      },
      error: () => {
        // Ohne Freundesliste bleibt der eigene Verlauf — die Umschaltung fehlt dann eben.
        this.friends.set([]);
        if (needsFriends) this.load();
      },
    });

    if (!needsFriends) this.load();
  }

  // ----- Auswahl ----------------------------------------------------------

  onWhoseChange(whose: Whose): void {
    this.whose = whose;
    this.store();
    this.load();
  }

  onPickedChange(picked: number[]): void {
    this.picked = picked ?? [];
    this.store();
    this.load();
  }

  /**
   * Der eigene Verlauf ist IMMER dabei. „Nur Freunde" waere eine Ansicht, in der man sich selbst
   * sucht — und der Vergleich ist der Zweck der Umschaltung.
   */
  private selectedUserIds(): number[] {
    const me = this.auth.currentUser?.userId;
    const mine = me ? [me] : [];

    if (this.whose === 'all') return [...mine, ...this.selectableFriends().map(f => f.userId)];
    if (this.whose === 'pick') return [...mine, ...this.picked];
    return mine;
  }

  // ----- Laden ------------------------------------------------------------

  private pollSubscription?: Subscription;

  /**
   * Zaehler gegen ueberholte Antworten: wer waehrend eines laufenden Abrufs umschaltet, bekommt
   * sonst die Antwort der alten Auswahl in die Tabelle.
   */
  private generation = 0;

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.pollSubscription?.unsubscribe();

    const generation = ++this.generation;
    this.history.get(this.selectedUserIds()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: histories => {
        if (generation !== this.generation) return;
        this.histories.set(histories);
        this.loading.set(false);
        this.schedulePoll(generation);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  /** Wie lange bis zur naechsten Nachfrage, solange Ergebnisse fehlen. */
  private static readonly PollMs = 4000;

  /** Wie oft nachgefragt wird, bevor aufgegeben wird — sonst laeuft die Seite endlos. */
  private static readonly MaxPolls = 15;

  private polls = 0;

  private schedulePoll(generation: number): void {
    if (this.pending() === 0) { this.polls = 0; return; }
    if (this.polls >= TournamentHistoryComponent.MaxPolls) return;

    this.polls++;
    this.pollSubscription = timer(TournamentHistoryComponent.PollMs)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (generation !== this.generation) return;
        this.history.get(this.selectedUserIds())
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: histories => {
              if (generation !== this.generation) return;
              this.histories.set(histories);
              this.schedulePoll(generation);
            },
            error: () => { /* still: der naechste Versuch kommt beim naechsten Laden */ },
          });
      });
  }

  readonly pending = computed(() => this.histories().reduce((sum, h) => sum + h.pending, 0));

  // ----- Aufteilung -------------------------------------------------------

  /**
   * Kuenftige zuerst, danach die gespielten. Beides in EINER Tabelle waere unlesbar: „Platz 56
   * von 56" und „noch nicht gespielt" sind verschiedene Arten von Zeile.
   */
  upcoming(history: PlayerHistory): PlayerHistoryEntry[] {
    const today = this.today();
    return history.entries
      .filter(e => e.endDate !== null && e.endDate >= today)
      .sort((a, b) => (a.endDate ?? '').localeCompare(b.endDate ?? ''));
  }

  past(history: PlayerHistory): PlayerHistoryEntry[] {
    const today = this.today();
    return history.entries.filter(e => e.endDate === null || e.endDate < today);
  }

  private today(): string {
    const now = new Date();
    const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
  }

  /**
   * Die Summe der gespielten Turniere — Punkte und Schnitt der Performance. Ein Verlauf ohne
   * Summe laesst einen selbst zusammenzaehlen, und genau darum sieht man ihn an.
   */
  summary(history: PlayerHistory): { played: number; points: number; performance: number | null } {
    const played = this.past(history).filter(e => e.hasResult);
    const rated = played.filter(e => e.performanceRating !== null);

    return {
      played: played.length,
      points: played.reduce((sum, e) => sum + (e.points ?? 0), 0),
      performance: rated.length === 0
        ? null
        : Math.round(rated.reduce((sum, e) => sum + (e.performanceRating ?? 0), 0) / rated.length),
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
   * <p>Ist es schon geholt, fuehrt der Klick direkt zu Teilnehmern, Paarungen und den eigenen
   * Ergebnissen. Ist es das nicht, wird der Holen-Auftrag eingereiht und danach dorthin
   * gewechselt — der Weg, auf dem ein vergangenes Turnier hier ueberhaupt ansehbar wird.</p>
   */
  open(entry: PlayerHistoryEntry): void {
    if (this.opening()) return;
    this.opening.set(entry.chessResultsId);

    this.tournaments.getTournament(entry.chessResultsId).pipe(
      catchError(() => of(null)),
    ).subscribe(tournament => {
      if (tournament) {
        this.opening.set(null);
        void this.router.navigate(['/tournaments', tournament.id]);
        return;
      }
      this.fetchThenOpen(entry);
    });
  }

  /**
   * Turnier holen und danach hinwechseln. Der Auftrag laeuft serverseitig weiter, auch wenn hier
   * nicht mehr gewartet wird — deshalb ein DECKEL auf das Nachfragen (rund zwei Minuten) und eine
   * Meldung statt eines endlosen Wartens.
   */
  private fetchThenOpen(entry: PlayerHistoryEntry): void {
    this.snackbar.info(this.translate.instant('turnier.history.fetching'));

    this.tournaments.startCrawl(entry.chessResultsId).pipe(
      catchError(() => of(null)),
    ).subscribe(job => {
      if (!job) {
        this.opening.set(null);
        this.snackbar.warn(this.translate.instant('turnier.history.fetchFailed'));
        return;
      }
      this.pollImport(entry);
    });
  }

  private pollImport(entry: PlayerHistoryEntry): void {
    timer(0, TournamentHistoryComponent.PollMs).pipe(
      switchMap(() => this.tournaments.getTournament(entry.chessResultsId)
        .pipe(catchError(() => of(null)))),
      // Solange nichts da ist, weiterfragen — hoechstens `MaxImportPolls` mal.
      takeWhile((t, i) => t === null && i < TournamentHistoryComponent.MaxImportPolls, true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(tournament => {
      if (tournament) {
        this.opening.set(null);
        void this.router.navigate(['/tournaments', tournament.id]);
      } else if (this.opening() === entry.chessResultsId) {
        // Deckel erreicht: der Auftrag laeuft weiter, aber hier wird nicht weiter gewartet.
        this.opening.set(null);
        this.snackbar.info(this.translate.instant('turnier.history.fetchSlow'));
      }
    });
  }

  trackById = (_: number, entry: PlayerHistoryEntry) => entry.chessResultsId;
  trackByUser = (_: number, history: PlayerHistory) => history.userId;

  // ----- Gemerkte Auswahl -------------------------------------------------

  private store(): void {
    try {
      localStorage.setItem(TournamentHistoryComponent.ViewKey,
        JSON.stringify({ whose: this.whose, picked: this.picked }));
    } catch {
      // Gesperrter oder voller Speicher (Privatmodus) ist kein Grund, die Seite scheitern zu
      // lassen — dann faengt man eben wieder bei „nur ich" an.
    }
  }

  private restore(): void {
    try {
      const raw = localStorage.getItem(TournamentHistoryComponent.ViewKey);
      const stored = raw ? JSON.parse(raw) : null;
      if (!stored || typeof stored !== 'object') return;

      if (stored.whose === 'me' || stored.whose === 'all' || stored.whose === 'pick') {
        this.whose = stored.whose;
      }
      if (Array.isArray(stored.picked)) {
        this.picked = stored.picked.filter((v: unknown) => typeof v === 'number');
      }
    } catch {
      // unlesbar/kaputt: Vorgabe bleibt stehen
    }
  }
}
