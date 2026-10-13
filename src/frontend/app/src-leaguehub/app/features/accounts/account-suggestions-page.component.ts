import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { AccountSuggestion } from '../../core/league.models';
import { AccountSuggestionsComponent } from '@rh/shared/player-card/account-suggestions.component';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';

/** Vorschläge nach Spieler, in der Reihenfolge des stärksten Vorschlags je Spieler. */
export function groupByPlayer(items: AccountSuggestion[]): { fide: string; name: string; team: string | null; items: AccountSuggestion[] }[] {
  const groups = new Map<string, { fide: string; name: string; team: string | null; items: AccountSuggestion[] }>();
  for (const s of items) {
    let g = groups.get(s.fide);
    if (!g) groups.set(s.fide, g = { fide: s.fide, name: s.name ?? s.fide, team: s.team ?? null, items: [] });
    g.items.push(s);
  }
  return [...groups.values()];
}

/** Gruppen, deren Spieler- oder Mannschaftsname den Suchbegriff enthält (ohne Groß/klein und Akzente). */
export function filterGroups<T extends { name: string; team: string | null }>(groups: T[], query: string): T[] {
  const norm = (x: string) => x.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const q = norm(query.trim());
  return q ? groups.filter(g => norm(g.name).includes(q) || norm(g.team ?? '').includes(q)) : groups;
}

/**
 * Konto-Vorschläge (0.607.0, Wunsch 2026-09-30): alle offenen Vorschläge der Konto-Suche an einem Ort, je Spieler. Die
 * Suche läuft im Hintergrund über die Spieler der laufenden Saison; hier entscheidet ein Verwalter. Ein Klick auf den Namen
 * öffnet die Spielerkarte (dort stehen die schon eingetragenen Konten und seine Partien).
 */
@Component({
  selector: 'lh-account-suggestions-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, AccountSuggestionsComponent, PlayerCardComponent],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <!-- UI-Sweep 2026-10-11 (r2-l-gate-text): Klartext statt Rechte-Schlüssel und ein Weg zurück zu den Prognosen -->
        <p>Die Konto-Vorschläge sehen nur Liga-Verwalter.</p>
        <p><a class="btn-sec" routerLink="/">← Zu den Prognosen</a></p>
      </section>
    } @else {
      <section class="sugg-page">
        <!-- UI-Sweep 2026-10-11 (d2-l-konten-vorlauf): Zähler neben der Überschrift, ein Satz + (i) statt drei Absätzen. -->
        <div class="sugg-top">
          <h2>Konto-Vorschläge</h2>
          @if (progress(); as p) { <span class="small muted" role="status">{{ p }}</span> }
        </div>
        <p class="muted info-line">Gehört das Konto dem Spieler? Übernehmen oder verwerfen — Verworfenes kommt nicht wieder.
          <button type="button" class="chk-btn" [attr.aria-expanded]="infoOpen()" aria-label="Wie gesucht wird"
                  title="Wie gesucht wird" (click)="infoOpen.set(!infoOpen())">i</button></p>
        @if (infoOpen()) {
          <div class="info-panel sugg-info">
            <p class="note">LeagueHub sucht auf Lichess und chess.com nach Konten, deren Name zum Spieler passt. Ob ein Konto
              wirklich ihm gehört, entscheidest du: übernehmen (unsicher oder gesichert) oder verwerfen; Verworfenes kommt nicht wieder.</p>
            <!-- UI-Sweep 2026-10-11 (r2-l-minor-intro): seit 0.610.0 werden auch Minderjährige gesucht (nur für Admins sichtbar) -->
            <p class="note">Konten Minderjähriger werden ebenfalls gesucht, sind aber nur für Admins sichtbar (Etikett
              „minderjährig") und erscheinen nirgends sonst.</p>
          </div>
        }
        @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
        @if (loading() && !groups().length) { <p class="muted">Lade …</p> }
        <!-- d2-l-konten-endlos: Suche nach Spieler oder Verein, die Liste in Seiten zu je 20 Spielern (vorher 96 Bildschirme). -->
        @if (groups().length) {
          <div class="club-search sugg-filter">
            <input type="search" placeholder="Spieler oder Verein" aria-label="Spieler oder Verein suchen" [value]="query()"
                   (input)="setQuery($any($event.target).value)" />
            @if (query()) { <span class="small muted">{{ filtered().length }} von {{ groups().length }} Spielern</span> }
          </div>
        }
        <!-- z2-l-konten-spalten: ab 1280 px fließen die Spieler in zwei Spalten; wer mehrere Vorschläge hat, nimmt die volle Breite. -->
        <div class="sugg-groups">
          @for (g of visible(); track g.fide) {
            <article class="sugg-group" [class.multi]="g.items.length > 1">
              <h3><button type="button" class="btn-link sugg-name" (click)="openCard(g.fide)">{{ g.name }}</button>
                @if (g.team) { <span class="muted small">{{ g.team }}</span> }
                <!-- a1-l-konten-mehrfachgruppe: ein Spieler mit mehreren Konten ist eine umrahmte Fläche mit der Zahl im Kopf. -->
                @if (g.items.length > 1) { <span class="muted small sugg-count">{{ g.items.length }} Konten</span> }</h3>
              <lh-account-suggestions [items]="g.items" (decided)="onDecided($event)" />
            </article>
          } @empty {
            @if (!loading() && !error()) { <p class="muted">{{ query() && groups().length ? 'Kein Spieler passt zur Suche.' : 'Keine offenen Vorschläge.' }}</p> }
          }
        </div>
        @if (rest(); as r) {
          <p class="sugg-more"><button type="button" class="btn-sec" (click)="shown.set(shown() + pageSize)">Weitere {{ nextCount() }} von {{ r }}</button></p>
        }
      </section>
      <lh-player-card />
    }
  `,
})
export class AccountSuggestionsPageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly auth = inject(AuthService);
  private readonly card = viewChild(PlayerCardComponent);

  readonly allowed = this.auth.has('league.manage');
  readonly items = signal<AccountSuggestion[]>([]);
  readonly scanned = signal<number | null>(null);
  readonly total = signal<number | null>(null);
  readonly decided = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly groups = computed(() => groupByPlayer(this.items()));
  /** Erklärtext hinter (i) (d2-l-konten-vorlauf). */
  readonly infoOpen = signal(false);
  /** Suche und Seiten (d2-l-konten-endlos): erst 20 Spieler, „Weitere" holt die nächsten 20. */
  readonly pageSize = 20;
  readonly query = signal('');
  readonly shown = signal(this.pageSize);
  readonly filtered = computed(() => filterGroups(this.groups(), this.query()));
  readonly visible = computed(() => this.filtered().slice(0, this.shown()));
  readonly rest = computed(() => Math.max(0, this.filtered().length - this.shown()));
  readonly nextCount = computed(() => Math.min(this.pageSize, this.rest()));
  readonly progress = computed(() => {
    const s = this.scanned(), t = this.total();
    if (s === null || !t) return null;
    const open = this.items().length - this.decided();
    const head = `${open} offen${this.decided() ? `, ${this.decided()} erledigt` : ''}.`;
    return s >= t ? `${head} Alle ${t} Spieler der Saison sind abgesucht.`
      : `${head} ${s} von ${t} Spielern der Saison abgesucht — die Suche läuft im Hintergrund weiter.`;
  });

  ngOnInit(): void {
    if (this.allowed) void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const r = await this.api.suggestions();
      this.items.set(r.items);
      this.scanned.set(r.scanned ?? null);
      this.total.set(r.total ?? null);
      this.decided.set(0);
    } catch {
      this.error.set('Die Vorschläge konnten nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  onDecided(e?: { suggestion: AccountSuggestion; accepted: boolean }): void {
    this.decided.update(n => n + 1);
    // UI-Sweep 2026-10-11 (r2-l-same-account): übernommen → die Vorschläge desselben Kontos bei anderen Spielern hat der
    // Server verworfen; sie verschwinden aus der Übersicht (auch aus den Gruppen der anderen Spieler).
    const twins = e?.accepted ? new Set((e.suggestion.alsoFor ?? []).map(t => t.id)) : null;
    if (twins?.size) this.items.update(list => list.filter(x => !twins.has(x.id)));
  }

  setQuery(q: string): void {
    this.query.set(q);
    this.shown.set(this.pageSize);
  }

  openCard(fide: string): void {
    void this.card()?.open(fide, null, null, null);
  }
}
