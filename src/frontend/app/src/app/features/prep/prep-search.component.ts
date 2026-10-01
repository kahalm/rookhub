import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PrepApiService } from './prep-api.service';
import { PrepHit } from './prep.models';

/** Kürzeste Suche, die an den Server geht (so prüft es auch der Server). */
export const PREP_MIN_QUERY = 2;
/** Warten nach dem letzten Tastendruck. */
export const PREP_SEARCH_DELAY_MS = 250;

/**
 * Suchseite der Spielervorbereitung (`/prep`, Recht `prep.view`): Namensanfang („Carlsen", „Carlsen, M",
 * „Magnus Carlsen", Umlaute in beiden Schreibweisen) oder FIDE-ID — meistgespielte zuerst. Die Suche steht in der
 * Adresse (`?q=`), damit „zurück" von der Spielerseite die Treffer wieder zeigt.
 */
@Component({
  selector: 'app-prep-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  template: `
    <div class="prep prep-search">
      <h1>{{ 'prep.title' | translate }}</h1>
      <p class="lede">{{ 'prep.intro' | translate }}</p>
      <mat-form-field appearance="outline" class="q">
        <mat-label>{{ 'prep.searchLabel' | translate }}</mat-label>
        <input matInput type="search" autocomplete="off" spellcheck="false" [value]="q()"
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
    </div>
  `,
  styles: [`
    .prep { max-width: 760px; margin: 0 auto; padding: 16px; }
    .prep h1 { margin: 4px 0 6px; font: 500 26px/1.2 Roboto, "Helvetica Neue", sans-serif; }
    .prep .lede { margin: 0 0 16px; color: var(--mat-sys-on-surface-variant); max-width: 64ch; }
    .q { width: 100%; }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .err { color: var(--rh-error); }
    .hits { list-style: none; margin: 8px 0 0; padding: 0; }
    .hits li { border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .hits a { display: grid; gap: 2px; padding: 10px 4px; color: inherit; text-decoration: none; min-width: 0; }
    .hits a:hover .name, .hits a:focus-visible .name { text-decoration: underline; }
    .name { font-weight: 500; overflow-wrap: anywhere; }
    .meta { color: var(--mat-sys-on-surface-variant); font-size: 14px; overflow-wrap: anywhere; }
  `],
})
export class PrepSearchComponent {
  private readonly api = inject(PrepApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);

  readonly q = signal('');
  /** `null` = noch nichts gesucht (oder zu kurz). */
  readonly hits = signal<PrepHit[] | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Zählt die Suchen: eine späte Antwort auf eine überholte Eingabe wird verworfen. */
  private seq = 0;

  constructor() {
    const q = this.route.snapshot.queryParamMap.get('q') ?? this.api.lastQuery();
    if (q) {
      this.q.set(q);
      void this.search(q);
    }
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

  years(h: PrepHit): string {
    return h.firstYear && h.lastYear && h.firstYear !== h.lastYear ? `${h.firstYear}–${h.lastYear}` : String(h.lastYear ?? h.firstYear);
  }

  fmt(n: number): string {
    return n.toLocaleString(this.translate.currentLang() || 'en');
  }
}
