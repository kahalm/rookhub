import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../core/league-api.service';
import { AccountSuggestion } from '../core/league.models';
import { HIDDEN_ACCOUNT, accountErrorText, siteLabel } from '../core/account-format';

/** „zuletzt aktiv 09/2026" — genauer braucht es niemand, um ein verwaistes Konto zu erkennen. */
export function lastActiveText(iso: string | null): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : `zuletzt aktiv ${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
}

/** Was die Seite unter einem Vorschlag verrät: Profilname, Ort, zuletzt aktiv. */
export function suggestionFacts(s: AccountSuggestion): string {
  return [s.profileName ? `Profil: ${s.profileName}` : null, s.location, lastActiveText(s.lastActive)].filter(x => !!x).join(', ');
}

/**
 * Vorschläge der Konto-Suche (0.607.0) — auf der Spielerkarte und in der Übersicht. Je Vorschlag das Konto (Link auf das
 * Profil, zum Nachsehen), die Hinweise und drei Knöpfe: als unsicher übernehmen, als gesichert übernehmen, verwerfen. Die
 * Liste führt die Aktionen selbst aus und meldet danach `decided` — die Karte lädt dann neu, die Übersicht zählt nach.
 */
@Component({
  selector: 'lh-account-suggestions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="sugg-list">
      @for (s of items(); track s.id) {
        <li [class.done]="gone().has(s.id)">
          <div class="acc-row">
            @if (s.hidden) {
              <span class="acc-hidden" title="Konten Minderjähriger zeigt LeagueHub niemandem — entscheide nach den Hinweisen">{{ hiddenLabel }}</span>
            } @else {
              <a [href]="s.url" target="_blank" rel="noopener">{{ label(s.site) }}: {{ s.user }}</a>
            }
            @if (showPlayer() && s.name) {
              <button type="button" class="btn-link sugg-player" (click)="openPlayer.emit(s.fide)">{{ s.name }}</button>
              @if (s.team) { <span class="muted small">{{ s.team }}</span> }
            }
          </div>
          <p class="small sugg-evidence">{{ s.evidence }}</p>
          @if (facts(s); as f) { <p class="small muted sugg-facts">{{ f }}</p> }
          @if (gone().get(s.id); as note) {
            <p class="small muted">{{ note }}</p>
          } @else {
            <div class="sugg-actions">
              <button type="button" class="btn-sec" [disabled]="busy() === s.id" (click)="accept(s, false)">Als unsicher übernehmen</button>
              <button type="button" class="btn-sec" [disabled]="busy() === s.id" (click)="accept(s, true)">Als gesichert übernehmen</button>
              <button type="button" class="btn-link" [disabled]="busy() === s.id" (click)="reject(s)">Verwerfen</button>
            </div>
          }
        </li>
      }
    </ul>
    <span class="update-msg err" role="status">{{ error() ?? '' }}</span>
  `,
})
export class AccountSuggestionsComponent {
  readonly items = input<AccountSuggestion[]>([]);
  /** In der Übersicht: Name und Mannschaft des Spielers dazu. */
  readonly showPlayer = input(false);
  readonly decided = output<{ suggestion: AccountSuggestion; accepted: boolean }>();
  readonly openPlayer = output<string>();

  private readonly api = inject(LeagueApiService);
  readonly label = siteLabel;
  readonly facts = suggestionFacts;
  readonly hiddenLabel = HIDDEN_ACCOUNT;
  readonly busy = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  /** Erledigte bleiben mit einem Satz stehen, bis die Liste neu kommt — sonst springt die Liste unter dem Finger weg. */
  readonly gone = signal(new Map<number, string>());

  async accept(s: AccountSuggestion, sure: boolean): Promise<void> {
    await this.run(s, async () => {
      await this.api.acceptSuggestion(s.id, sure);
      return sure ? 'Als gesichert übernommen.' : 'Als unsicher übernommen.';
    }, true);
  }

  async reject(s: AccountSuggestion): Promise<void> {
    await this.run(s, async () => {
      await this.api.rejectSuggestion(s.id);
      return 'Verworfen — wird nicht wieder vorgeschlagen.';
    }, false);
  }

  private async run(s: AccountSuggestion, work: () => Promise<string>, accepted: boolean): Promise<void> {
    this.busy.set(s.id);
    this.error.set(null);
    try {
      const note = await work();
      this.gone.update(m => new Map(m).set(s.id, note));
      this.decided.emit({ suggestion: s, accepted });
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      if (e?.status === 404) {
        this.gone.update(m => new Map(m).set(s.id, 'Schon erledigt.'));
      } else {
        this.error.set(accountErrorText(e?.error?.reason));
      }
    } finally {
      this.busy.set(null);
    }
  }
}
