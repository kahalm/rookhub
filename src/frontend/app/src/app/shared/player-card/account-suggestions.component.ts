import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '@lh/core/league-api.service';
import { AccountSuggestion, AccountSuggestionTwin } from '@lh/core/league.models';
import { HIDDEN_ACCOUNT, MINOR_ACCOUNT, MINOR_ACCOUNT_TITLE, accountErrorText, siteLabel } from './account-format';
import { AccountChecksComponent } from './account-checks.component';
import { injectCardTx } from './card-tx';

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

/** UI-Sweep 2026-10-11 (r2-l-same-account): der Verein aus dem Hinweis eines Team-Vorschlags („spielte für „…““ bzw.
 *  „Mitglied im Lichess-Team „…““), sonst `null` — dieselbe Regel wie `LeagueTeamScout.OriginClub` am Server. */
export function originClub(evidence: string | null | undefined): string | null {
  const m = /(?:spielte für|Mitglied im Lichess-Team) „([^“]+)“/.exec(evidence ?? '');
  return m ? m[1] : null;
}

/** „Schmidt, Hans (SC Garching 4)" — Name und Mannschaft eines anderen Spielers desselben Kontos. */
export function twinText(t: AccountSuggestionTwin): string {
  return `${t.name ?? t.fide}${t.team ? ` (${t.team})` : ''}`;
}

/** UI-Sweep 2026-10-11 (r2-l-same-account): passt der Verein des Kontos zu einem ANDEREN Spieler, aber nicht zu diesem —
 *  „spielte für „SC Garching 1980 e.V.“ – passt eher zu Schmidt, Hans (SC Garching 4)". Sonst `null`. */
export function betterFitText(s: AccountSuggestion): string | null {
  if (s.clubFits || !s.alsoFor?.length) return null;
  const fits = s.alsoFor.filter(t => t.clubFits);
  if (!fits.length) return null;
  const club = originClub(s.evidence);
  return `${club ? `spielte für „${club}“ – ` : ''}passt eher zu ${fits.map(twinText).join(', ')}`;
}

/**
 * Vorschläge der Konto-Suche (0.607.0) — auf der Spielerkarte und in der Übersicht. Je Vorschlag das Konto (Link auf das
 * Profil, zum Nachsehen), die Hinweise und drei Knöpfe: als unsicher übernehmen, als gesichert übernehmen, verwerfen. Die
 * Liste führt die Aktionen selbst aus und meldet danach `decided` — die Karte lädt dann neu, die Übersicht zählt nach.
 */
@Component({
  selector: 'lh-account-suggestions',
  standalone: true,
  imports: [AccountChecksComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="sugg-list">
      @for (s of items(); track s.id) {
        <li [class.done]="gone().has(s.id)">
          <div class="acc-row">
            @if (s.hidden) {
              <span class="acc-hidden" [title]="tx('playerCard.accounts.hiddenSuggTitle', 'Konten Minderjähriger zeigt LeagueHub niemandem — entscheide nach den Hinweisen')">{{ tx('playerCard.accounts.hidden', hiddenLabel) }}</span>
            } @else {
              <a [href]="s.url" target="_blank" rel="noopener">{{ label(s.site) }}: {{ s.user }}</a>
              <button type="button" class="chk-btn" [attr.aria-expanded]="checksOpen().has(s.id)" [attr.aria-label]="tx('playerCard.accounts.checksTitle', 'Was geprüft wurde')"
                      [title]="tx('playerCard.accounts.checksTitle', 'Was geprüft wurde')" (click)="toggleChecks(s.id)">i</button>
              @if (s.minor) { <span class="tag tag-minor" [attr.title]="tx('playerCard.accounts.minorTitle', minorTitle)">{{ tx('playerCard.accounts.minor', minorLabel) }}</span> }
            }
            @if (showPlayer() && s.name) {
              <button type="button" class="btn-link sugg-player" (click)="openPlayer.emit(s.fide)">{{ s.name }}</button>
              @if (s.team) { <span class="muted small">{{ s.team }}</span> }
            }
          </div>
          @if (s.alsoFor?.length) {
            <!-- UI-Sweep 2026-10-11 (r2-l-same-account): dasselbe Konto steht auch bei anderen Spielern — wer es hier
                 übernimmt, verwirft die anderen (ein Konto gehört einem Menschen). -->
            <p class="also-for" [class.fits]="s.clubFits">{{ tx('playerCard.accounts.alsoFor', 'auch vorgeschlagen für: ' + twins(s), { names: twins(s) }) }}</p>
            @if (betterFit(s); as b) { <p class="small muted sugg-better">{{ b }}</p> }
          }
          <p class="small sugg-evidence">{{ s.evidence }}</p>
          @if (checksOpen().has(s.id)) { <lh-account-checks kind="suggestion" [id]="s.id" /> }
          @if (facts(s); as f) { <p class="small muted sugg-facts">{{ f }}</p> }
          @if (gone().get(s.id); as note) {
            <p class="small muted">{{ note }}</p>
          } @else {
            <div class="sugg-actions">
              <!-- UI-Sweep 2026-10-10 (l-account-btns): drei gleich breite Knöpfe ≥ 40 px, die langen Texte als Tooltip -->
              <button type="button" class="btn-sec sa-btn" [disabled]="busy() === s.id" (click)="accept(s, false)"
                      [title]="tx('playerCard.accounts.acceptUnsureTitle', 'Als unsicher übernehmen')">{{ tx('playerCard.accounts.acceptUnsure', 'Unsicher') }}</button>
              <button type="button" class="btn-pri sa-btn" [disabled]="busy() === s.id" (click)="accept(s, true)"
                      [title]="tx('playerCard.accounts.acceptSureTitle', 'Als gesichert übernehmen')">{{ tx('playerCard.accounts.acceptSure', 'Gesichert') }}</button>
              <button type="button" class="btn-sec sa-btn sa-reject" [disabled]="busy() === s.id" (click)="reject(s)"
                      [title]="tx('playerCard.accounts.rejectTitle', 'Vorschlag verwerfen — er kommt nicht wieder')">{{ tx('playerCard.accounts.reject', 'Verwerfen') }}</button>
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
  /** Sprache wie die Karte (Codereview F3-011: in RookHubs Spielervorbereitung stand der Baustein fest deutsch). */
  readonly tx = injectCardTx();
  readonly label = siteLabel;
  readonly facts = suggestionFacts;
  readonly betterFit = betterFitText;
  twins(s: AccountSuggestion): string { return (s.alsoFor ?? []).map(twinText).join(', '); }
  readonly hiddenLabel = HIDDEN_ACCOUNT;
  readonly minorLabel = MINOR_ACCOUNT;
  readonly minorTitle = MINOR_ACCOUNT_TITLE;
  readonly busy = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  /** Erledigte bleiben mit einem Satz stehen, bis die Liste neu kommt — sonst springt die Liste unter dem Finger weg. */
  readonly gone = signal(new Map<number, string>());
  /** Vorschläge, deren Prüfung (i) aufgeklappt ist (0.619.0). */
  readonly checksOpen = signal(new Set<number>());

  toggleChecks(id: number): void {
    this.checksOpen.update(s => { const n = new Set(s); if (!n.delete(id)) n.add(id); return n; });
  }

  async accept(s: AccountSuggestion, sure: boolean): Promise<void> {
    await this.run(s, async () => {
      await this.api.acceptSuggestion(s.id, sure);
      const note = sure ? this.tx('playerCard.accounts.acceptedSure', 'Als gesichert übernommen.')
        : this.tx('playerCard.accounts.acceptedUnsure', 'Als unsicher übernommen.');
      // UI-Sweep 2026-10-11 (r2-l-same-account): der Server verwirft die Vorschläge desselben Kontos bei anderen Spielern.
      const names = this.twins(s);
      return s.alsoFor?.length
        ? `${note} ${this.tx('playerCard.accounts.twinsRejected', `Vorschlag für ${names} verworfen — gehört jemand anderem.`, { names })}`
        : note;
    }, true);
  }

  async reject(s: AccountSuggestion): Promise<void> {
    await this.run(s, async () => {
      await this.api.rejectSuggestion(s.id);
      return this.tx('playerCard.accounts.rejected', 'Verworfen — wird nicht wieder vorgeschlagen.');
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
        this.gone.update(m => new Map(m).set(s.id, this.tx('playerCard.accounts.alreadyDone', 'Schon erledigt.')));
      } else {
        this.error.set(accountErrorText(e?.error?.reason));
      }
    } finally {
      this.busy.set(null);
    }
  }
}
