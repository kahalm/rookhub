import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../core/league-api.service';
import { Account, AccountInput, AccountSuggestion, SuggestionList } from '../core/league.models';
import { AccountSuggestionsComponent } from './account-suggestions.component';
import { AccountChecksComponent } from './account-checks.component';
import { ACCOUNT_SITES, HIDDEN_ACCOUNT, MINOR_ACCOUNT, MINOR_ACCOUNT_TITLE, accountErrorText, siteLabel } from '../core/account-format';

export { ACCOUNT_SITES, accountErrorText, siteLabel } from '../core/account-format';

/** Ergebnis von „Jetzt suchen" in Worten. */
export function scanNoteText(r: SuggestionList): string {
  if (r.skipped) return `Nicht gesucht: ${r.skipped}.`;
  const n = r.found ?? 0;
  return n === 0 ? 'Nichts Neues gefunden.' : n === 1 ? '1 neuer Vorschlag.' : `${n} neue Vorschläge.`;
}

/** Stand des Abrufs in Worten — `null` ohne Abruf-Angaben (Teilen-Link). */
export function accountStatus(a: Account): string | null {
  if (a.id === undefined) return null;
  if (a.error) return `Partien nicht geholt: ${a.error}.`;
  if (!a.syncedAt) return 'Partien werden geholt …';
  const d = new Date(a.syncedAt);
  const stand = `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.`;
  const n = a.games ?? 0;
  return `${String(n).replace(/\B(?=(\d{3})+(?!\d))/g, '.')} ${n === 1 ? 'Online-Partie' : 'Online-Partien'} geholt (Stand ${stand}).`;
}

/**
 * Online-Konten eines Spielers (0.605.0, Wunsch 2026-09-30: „für einen User kann es eine Liste von Onlinekonten geben —
 * Name + Seite, gesichert oder unsicher, dazu Kommentare"). Zeigt die Konten mit Stand des Abrufs; Verwalter
 * (`league.manage`, nie über einen Teilen-Link) legen an, ändern und entfernen. Nach jeder Änderung meldet die Liste
 * `changed` — die Karte lädt sich neu (Partienzahl, Baum).
 */
@Component({
  selector: 'lh-online-accounts',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (accounts().length) {
      <ul class="acc-list">
        @for (a of accounts(); track a.id ?? a.url ?? $index) {
          <li>
            @if (editing() === a.id) {
              <ng-container *ngTemplateOutlet="form" />
            } @else {
              <div class="acc-row">
                @if (a.hidden) {
                  <span class="acc-hidden" title="Konten Minderjähriger zeigt LeagueHub niemandem — ihre Partien zählen nur im Eröffnungsbaum">{{ hiddenLabel }}</span>
                } @else {
                  <a [href]="a.url" target="_blank" rel="noopener">{{ label(a.site) }}: {{ a.user }}</a>
                }
                <span class="tag" [class.tag-sure]="a.conf === 'sicher'">{{ a.conf === 'sicher' ? 'gesichert' : 'unsicher' }}</span>
                @if (a.minor) { <span class="tag tag-minor" [attr.title]="minorTitle">{{ minorLabel }}</span> }
                @if (a.id !== undefined && !a.hidden) {
                  <button type="button" class="chk-btn" [attr.aria-expanded]="checksOpen().has(a.id)" aria-label="Was geprüft wurde"
                          title="Was geprüft wurde" (click)="toggleChecks(a.id)">i</button>
                }
                @if (canEdit() && a.id !== undefined) {
                  @if (!a.hidden) { <button type="button" class="btn-link" [disabled]="busy()" (click)="startEdit(a)">Bearbeiten</button> }
                  <button type="button" class="btn-link" [disabled]="busy()" (click)="remove(a)">Entfernen</button>
                }
              </div>
              @if (a.id !== undefined && checksOpen().has(a.id)) { <lh-account-checks kind="account" [id]="a.id" /> }
              @if (a.comment) { <p class="acc-comment small">{{ a.comment }}</p> }
              @if (status(a); as st) {
                <p class="small muted acc-status">{{ st }}
                  @if (canEdit() && a.error) { <button type="button" class="btn-link" [disabled]="busy()" (click)="resync(a)">Nochmal holen</button> }</p>
              }
            }
          </li>
        }
      </ul>
    } @else {
      <p class="muted small">Noch kein Online-Konto eingetragen.</p>
    }
    @if (canEdit() && editing() === null) {
      @if (adding()) { <ng-container *ngTemplateOutlet="form" /> }
      @else { <button type="button" class="btn-sec acc-add" (click)="startAdd()">Konto hinzufügen</button> }
    }
    <span class="update-msg err" role="status">{{ error() ?? '' }}</span>
    @if (canEdit()) {
      <div class="sugg">
        <div class="sugg-head">
          <h4>Vorschläge der Konto-Suche</h4>
          <button type="button" class="btn-link" [disabled]="scanning()" (click)="scan()">{{ scanning() ? 'Sucht …' : 'Jetzt suchen' }}</button>
        </div>
        @if (suggestions().length) {
          <lh-account-suggestions [items]="suggestions()" (decided)="onDecided($event.accepted)" />
        } @else if (!scanning() && !scanNote()) {
          <p class="muted small">Keine offenen Vorschläge. LeagueHub sucht im Hintergrund auf Lichess und chess.com nach Konten,
            deren Name zum Spieler passt.</p>
        }
        @if (scanNote(); as n) { <p class="small muted" role="status">{{ n }}</p> }
      </div>
    }

    <ng-template #form>
      <form class="acc-form" (submit)="$event.preventDefault(); save()">
        <div class="acc-form-row">
          <label class="field">Seite
            <select [value]="formSite()" (change)="formSite.set($any($event.target).value)">
              @for (s of sites; track s.key) { <option [value]="s.key" [selected]="s.key === formSite()">{{ s.label }}</option> }
            </select>
          </label>
          <label class="field acc-user">Name oder Profiladresse
            <input type="text" autocomplete="off" spellcheck="false" [value]="formUser()" (input)="formUser.set($any($event.target).value)" />
          </label>
        </div>
        <div class="seg" role="group" aria-label="Zuordnung">
          <button type="button" [attr.aria-pressed]="formSure()" (click)="formSure.set(true)">gesichert</button>
          <button type="button" [attr.aria-pressed]="!formSure()" (click)="formSure.set(false)">unsicher</button>
        </div>
        <label class="field">Kommentar
          <textarea rows="2" maxlength="1000" [value]="formComment()" (input)="formComment.set($any($event.target).value)"
                    placeholder="z. B. woher die Zuordnung kommt"></textarea>
        </label>
        <div class="actions">
          <button type="submit" class="btn-pri" [disabled]="busy() || !formUser().trim()">{{ busy() ? 'Speichere …' : 'Speichern' }}</button>
          <button type="button" class="btn-link" [disabled]="busy()" (click)="cancel()">Abbrechen</button>
        </div>
      </form>
    </ng-template>
  `,
  imports: [NgTemplateOutlet, AccountSuggestionsComponent, AccountChecksComponent],
})
export class OnlineAccountsComponent {
  readonly fide = input.required<string>();
  readonly accounts = input<Account[]>([]);
  readonly canEdit = input(false);
  readonly changed = output<void>();

  private readonly api = inject(LeagueApiService);
  readonly sites = ACCOUNT_SITES;
  readonly label = siteLabel;
  readonly hiddenLabel = HIDDEN_ACCOUNT;
  readonly minorLabel = MINOR_ACCOUNT;
  readonly minorTitle = MINOR_ACCOUNT_TITLE;
  readonly status = accountStatus;

  readonly adding = signal(false);
  /** Kennung des Kontos, das gerade bearbeitet wird. */
  readonly editing = signal<number | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly formSite = signal('lichess');
  readonly formUser = signal('');
  readonly formSure = signal(false);
  readonly formComment = signal('');
  /** Offene Vorschläge der Konto-Suche für diesen Spieler (0.607.0, nur Verwalter). */
  readonly suggestions = signal<AccountSuggestion[]>([]);
  readonly scanning = signal(false);
  readonly scanNote = signal<string | null>(null);
  /** Konten, deren Prüfung (i) aufgeklappt ist (0.619.0). */
  readonly checksOpen = signal(new Set<number>());

  constructor() {
    effect(() => {
      const fide = this.fide();
      if (!this.canEdit()) return;
      untracked(() => void this.loadSuggestions(fide));
    });
  }

  private async loadSuggestions(fide: string): Promise<void> {
    this.scanNote.set(null);
    try {
      const r = await this.api.playerSuggestions(fide);
      if (fide === this.fide()) this.suggestions.set(r.items);
    } catch { /* Vorschläge sind eine Zugabe — ohne sie bleibt die Liste, wie sie ist */ }
  }

  /** Für diesen Spieler jetzt suchen (einige Sekunden: Lichess und chess.com werden einzeln gefragt). */
  async scan(): Promise<void> {
    const fide = this.fide();
    this.scanning.set(true);
    this.scanNote.set(null);
    try {
      const r = await this.api.scanSuggestions(fide);
      if (fide !== this.fide()) return;
      this.suggestions.set(r.items);
      this.scanNote.set(scanNoteText(r));
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason : undefined;
      this.scanNote.set(reason === 'rateLimited' ? 'Lichess oder chess.com bremst gerade — bitte in ein paar Minuten nochmal.'
        : 'Lichess oder chess.com ist gerade nicht erreichbar.');
    } finally {
      this.scanning.set(false);
    }
  }

  toggleChecks(id: number): void {
    this.checksOpen.update(s => { const n = new Set(s); if (!n.delete(id)) n.add(id); return n; });
  }

  onDecided(accepted: boolean): void {
    if (accepted) this.changed.emit();                                     // das neue Konto steht dann in der Liste
  }

  startAdd(): void {
    this.fill('lichess', '', false, '');
    this.adding.set(true);
  }

  startEdit(a: Account): void {
    this.fill(a.site ?? 'lichess', a.user ?? '', a.conf === 'sicher', a.comment ?? '');
    this.adding.set(false);
    this.editing.set(a.id ?? null);
  }

  cancel(): void {
    this.adding.set(false);
    this.editing.set(null);
    this.error.set(null);
  }

  async save(): Promise<void> {
    const input: AccountInput = { site: this.formSite(), user: this.formUser().trim(), sure: this.formSure(), comment: this.formComment() };
    await this.run(async () => {
      const id = this.editing();
      if (id !== null) await this.api.updateAccount(id, input);
      else await this.api.addAccount(this.fide(), input);
      this.cancel();
    });
  }

  async remove(a: Account): Promise<void> {
    const what = a.hidden ? 'Das verborgene Online-Konto' : `${siteLabel(a.site)}-Konto „${a.user}“`;
    if (a.id === undefined || !confirm(`${what} entfernen? Die geholten Partien gehen mit.`)) return;
    await this.run(() => this.api.deleteAccount(a.id!));
  }

  async resync(a: Account): Promise<void> {
    if (a.id !== undefined) await this.run(() => this.api.syncAccount(a.id!));
  }

  private async run(work: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
      this.changed.emit();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.error.set(accountErrorText(e?.error?.reason));
    } finally {
      this.busy.set(false);
    }
  }

  private fill(site: string, user: string, sure: boolean, comment: string): void {
    this.formSite.set(site);
    this.formUser.set(user);
    this.formSure.set(sure);
    this.formComment.set(comment);
    this.error.set(null);
  }
}
