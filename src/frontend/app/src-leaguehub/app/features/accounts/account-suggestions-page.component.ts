import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { AccountSuggestion } from '../../core/league.models';
import { AccountSuggestionsComponent } from '../../shared/account-suggestions.component';
import { PlayerCardComponent } from '../../shared/player-card.component';

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

/**
 * Konto-Vorschläge (0.607.0, Wunsch 2026-09-30): alle offenen Vorschläge der Konto-Suche an einem Ort, je Spieler. Die
 * Suche läuft im Hintergrund über die Spieler der laufenden Saison; hier entscheidet ein Verwalter. Ein Klick auf den Namen
 * öffnet die Spielerkarte (dort stehen die schon eingetragenen Konten und seine Partien).
 */
@Component({
  selector: 'lh-account-suggestions-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountSuggestionsComponent, PlayerCardComponent],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Die Konto-Vorschläge sehen nur Liga-Verwalter.</p>
      </section>
    } @else {
      <section class="sugg-page">
        <h2>Konto-Vorschläge</h2>
        <p class="muted">LeagueHub sucht auf Lichess und chess.com nach Konten, deren Name zum Spieler passt — nur bei
          Erwachsenen. Ob ein Konto wirklich ihm gehört, entscheidest du: übernehmen (unsicher oder gesichert) oder verwerfen;
          Verworfenes kommt nicht wieder.</p>
        @if (progress(); as p) {
          <p class="small muted" role="status">{{ p }}</p>
        }
        @if (error()) { <p class="err">{{ error() }}</p> }
        @if (loading() && !groups().length) { <p class="muted">Lade …</p> }
        @for (g of groups(); track g.fide) {
          <article class="sugg-group">
            <h3><button type="button" class="btn-link sugg-name" (click)="openCard(g.fide)">{{ g.name }}</button>
              @if (g.team) { <span class="muted small">{{ g.team }}</span> }</h3>
            <lh-account-suggestions [items]="g.items" (decided)="onDecided()" />
          </article>
        } @empty {
          @if (!loading() && !error()) { <p class="muted">Keine offenen Vorschläge.</p> }
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

  onDecided(): void {
    this.decided.update(n => n + 1);
  }

  openCard(fide: string): void {
    void this.card()?.open(fide, null, null, null);
  }
}
