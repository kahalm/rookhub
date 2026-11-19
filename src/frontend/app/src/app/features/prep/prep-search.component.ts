import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { PrepApiService } from './prep-api.service';
import { PrepHistoryEntry, PrepHit } from './prep.models';

/** Kürzeste Suche, die an den Server geht (so prüft es auch der Server). */
export const PREP_MIN_QUERY = 2;
/** Warten nach dem letzten Tastendruck. */
export const PREP_SEARCH_DELAY_MS = 250;

/**
 * Suchseite der Spielervorbereitung (`/prep`, Recht `prep.view`): Namensanfang („Carlsen", „Carlsen, M",
 * „Magnus Carlsen", Umlaute in beiden Schreibweisen) oder FIDE-ID — meistgespielte zuerst. Die Suche steht in der
 * Adresse (`?q=`), damit „zurück" von der Spielerseite die Treffer wieder zeigt.
 *
 * Unter dem Suchfeld, solange nicht gesucht wird: „Frühere Vorbereitungen" (0.995.0, Wunsch 2026-11-19: „zeig dort liste
 * der alten vorbereitungen an und mit klick hol ich den spieler gleich wieder") — die eigenen „Prep: …"-Repertoires, je
 * Gegner eine Zeile. Ein zugeordneter Gegner führt direkt auf seine Spielerseite; ein nicht eindeutig zugeordneter (ältere
 * Einträge kennen nur den Namen) setzt die Suche auf seinen Namen.
 */
@Component({
  selector: 'app-prep-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, DatePipe, NgTemplateOutlet, MatFormFieldModule, MatInputModule, MatProgressBarModule,
    MatButtonModule, MatIconModule, IconLabelDirective, LoadErrorComponent],
  template: `
    <div class="prep prep-search">
      <h1>{{ 'prep.title' | translate }}</h1>
      <p class="lede">{{ 'prep.intro' | translate }}</p>
      <!-- subscriptSizing dynamic (DESIGN 5.8): der Hinweis bricht am Handy in zwei Zeilen — fest reserviert ist nur eine,
           die zweite lag sonst auf dem, was darunter steht (Überschrift der früheren Vorbereitungen, „Kein Spieler gefunden“). -->
      <mat-form-field appearance="outline" class="q" subscriptSizing="dynamic">
        <mat-label>{{ 'prep.searchLabel' | translate }}</mat-label>
        <input #box matInput type="search" autocomplete="off" spellcheck="false" [value]="q()"
               (input)="onInput($any($event.target).value)" (keydown.enter)="now()" />
        <mat-hint>{{ 'prep.searchHint' | translate }}</mat-hint>
      </mat-form-field>
      @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <p class="err" role="alert">{{ 'prep.searchError' | translate }}</p> }
      @if (hits(); as list) {
        @if (!list.length) { <p class="hint" role="status">{{ 'prep.noHits' | translate }}</p> }
        @else {
          <ul class="hits" role="list">
            @for (h of list; track h.id) {
              <li>
                <a [routerLink]="['/prep', h.id]">
                  <span class="name">{{ h.name }}</span>
                  <span class="meta">{{ 'prep.games' | translate: { games: fmt(h.games) } }}@if (h.firstYear) { · {{ years(h) }} }@if (h.maxElo) { · {{ 'prep.maxElo' | translate: { elo: h.maxElo } }} }@if (h.fide) { · FIDE&nbsp;{{ h.fide }} } @else { · {{ 'prep.noFide' | translate }} }</span>
                </a>
              </li>
            }
          </ul>
        }
      }
      @if (showHistory()) {
        <section class="history" aria-labelledby="prep-history-title">
          <h2 id="prep-history-title">{{ 'prep.history.title' | translate }}</h2>
          @if (historyError(); as failure) {
            <app-load-error [error]="failure" (retry)="loadHistory()" />
          } @else {
            <ul class="history-list" role="list">
              @for (e of history(); track e.repertoireId) {
                <li>
                  @if (e.playerId) {
                    <a class="main" [routerLink]="['/prep', e.playerId]">
                      <ng-container [ngTemplateOutlet]="entry" [ngTemplateOutletContext]="{ $implicit: e }" />
                    </a>
                  } @else {
                    <!-- Nicht zugeordnet: kein Sprung, sondern die Suche nach dem Namen — erkennbar an „Spieler suchen“. -->
                    <button type="button" class="main" (click)="searchFor(e, $event)">
                      <ng-container [ngTemplateOutlet]="entry" [ngTemplateOutletContext]="{ $implicit: e }" />
                    </button>
                  }
                  <a mat-icon-button class="rep" [routerLink]="['/repertoires', e.repertoireId]"
                     [appIconLabel]="'prep.history.openRepertoire' | translate: { name: e.name }">
                    <mat-icon>library_books</mat-icon>
                  </a>
                </li>
              }
            </ul>
          }
        </section>
      }
    </div>

    <ng-template #entry let-e>
      <span class="name">{{ e.name }}</span>
      <span class="meta rh-dots">
        <span>{{ 'prep.history.preparedOn' | translate: { date: (e.preparedAt | date: 'mediumDate') } }}</span>
        @if (e.color) {
          <span>{{ (e.color === 'b' ? 'prep.trainingRepertoire.asBlack' : 'prep.trainingRepertoire.asWhite') | translate }}</span>
        }
        @if (e.playerId) {
          @if (e.games !== null && e.games !== undefined) { <span>{{ 'prep.games' | translate: { games: fmt(e.games) } }}</span> }
          @if (e.firstYear || e.lastYear) { <span>{{ years(e) }}</span> }
          @if (e.maxElo) { <span>{{ 'prep.maxElo' | translate: { elo: e.maxElo } }}</span> }
        } @else {
          <span class="find"><mat-icon>search</mat-icon>{{ 'prep.history.find' | translate }}</span>
        }
      </span>
    </ng-template>
  `,
  styles: [`
    .prep { max-width: 760px; margin: 0 auto; padding: 16px; }
    /* b1-rk-h1-uneinheitlich: Material-Seitentitel wie auf den übrigen Seiten (vorher 26 px/500); Erklärtext mit Luft. */
    .prep h1 { margin: 4px 0 12px; }
    .prep .lede { margin: 0 0 16px; color: var(--mat-sys-on-surface-variant); max-width: 64ch; }
    .q { width: 100%; margin-bottom: 8px; }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .err { color: var(--rh-error); }
    .hits, .history-list { list-style: none; margin: 8px 0 0; padding: 0; }
    .hits li, .history-list li { border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .hits a, .history-list .main { display: grid; gap: 2px; padding: 10px 4px; color: inherit; text-decoration: none; min-width: 0; }
    .hits a:hover .name, .hits a:focus-visible .name,
    .history-list .main:hover .name, .history-list .main:focus-visible .name { text-decoration: underline; }
    .name { font-weight: 500; overflow-wrap: anywhere; }
    .meta { color: var(--mat-sys-on-surface-variant); font-size: 14px; overflow-wrap: anywhere; }
    /* Frühere Vorbereitungen (0.995.0): Zeilen wie die Treffer; rechts daneben — nie in der Zeile selbst — der Weg zum
       Trainings-Repertoire als Symbolknopf. Lange Namen brechen in der Zeile um, der Knopf bleibt stehen. */
    .history { margin-top: 16px; }
    .history h2 { margin: 0; font-size: 1.1rem; }
    .history-list li { display: flex; align-items: center; gap: 4px; }
    .history-list .main { flex: 1 1 auto; }
    .history-list button.main {
      appearance: none; margin: 0; border: 0; background: none; font: inherit; text-align: start; cursor: pointer;
    }
    .history-list .rep { flex: none; color: var(--mat-sys-on-surface-variant); }
    .find { display: inline-flex; align-items: center; gap: 2px; }
    .find mat-icon { width: 16px; height: 16px; font-size: 16px; }
  `],
})
export class PrepSearchComponent {
  private readonly api = inject(PrepApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);
  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  readonly q = signal('');
  /** `null` = noch nichts gesucht (oder zu kurz). */
  readonly hits = signal<PrepHit[] | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  /** Frühere Vorbereitungen — leer heißt: kein Abschnitt (kein Leerzustand-Text). */
  readonly history = signal<PrepHistoryEntry[]>([]);
  /** Der Fehler des Abrufs; dann steht statt der Liste der Ladefehler mit „Wiederholen" (nie wie „es gibt keine"). */
  readonly historyError = signal<unknown>(null);
  /** Nur solange nicht gesucht wird (Feld leer oder zu kurz) — sonst gehört der Platz den Treffern. */
  readonly showHistory = computed(() => this.q().trim().length < PREP_MIN_QUERY && (this.history().length > 0 || !!this.historyError()));
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Zählt die Suchen: eine späte Antwort auf eine überholte Eingabe wird verworfen. */
  private seq = 0;

  constructor() {
    const q = this.route.snapshot.queryParamMap.get('q') ?? this.api.lastQuery();
    if (q) {
      this.q.set(q);
      void this.search(q);
    }
    void this.loadHistory();
    inject(DestroyRef).onDestroy(() => { if (this.timer) clearTimeout(this.timer); });
  }

  onInput(value: string): void {
    this.q.set(value);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.search(value), PREP_SEARCH_DELAY_MS);
  }

  now(): void {
    if (this.timer) clearTimeout(this.timer);
    void this.search(this.q());
  }

  async search(raw: string): Promise<void> {
    const q = raw.trim();
    this.api.lastQuery.set(q);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { q: q || null }, replaceUrl: true });
    const my = ++this.seq;
    this.error.set(false);
    if (q.length < PREP_MIN_QUERY) {
      this.hits.set(null);
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    try {
      const r = await this.api.search(q);
      if (my === this.seq) this.hits.set(r);
    } catch {
      if (my === this.seq) { this.hits.set(null); this.error.set(true); }
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }

  async loadHistory(): Promise<void> {
    this.historyError.set(null);
    try {
      this.history.set(await this.api.history());
    } catch (e) {
      this.history.set([]);
      this.historyError.set(e ?? true);
    }
  }

  /**
   * Eintrag ohne zugeordneten Spieler: sein Name ins Suchfeld, sofort suchen — der Spieler steht dann in den Treffern.
   * Per Tastatur ausgelöst (`detail` 0) geht der Fokus ins Suchfeld: der Knopf verschwindet mit der Liste, der Fokus fiele
   * sonst auf die Seite zurück. Bei Maus und Finger bleibt das Feld ohne Fokus (sonst klappt am Handy die Tastatur auf).
   */
  searchFor(e: PrepHistoryEntry, ev?: Event): void {
    if (this.timer) clearTimeout(this.timer);
    this.q.set(e.query);
    void this.search(e.query);
    if ((ev as UIEvent | undefined)?.detail === 0) this.box()?.nativeElement.focus();
  }

  years(h: { firstYear: number | null; lastYear: number | null }): string {
    return h.firstYear && h.lastYear && h.firstYear !== h.lastYear ? `${h.firstYear}–${h.lastYear}` : String(h.lastYear ?? h.firstYear);
  }

  fmt(n: number): string {
    return n.toLocaleString(this.translate.currentLang() || 'en');
  }
}
