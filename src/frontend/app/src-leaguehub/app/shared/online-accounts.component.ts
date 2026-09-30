import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../core/league-api.service';
import { Account, AccountInput } from '../core/league.models';

/** Die Seiten, die LeagueHub kennt — SPIEGEL von `LeagueOnlineSites.All` (Kürzel + Anzeige). */
export const ACCOUNT_SITES: { key: string; label: string }[] = [
  { key: 'lichess', label: 'Lichess' },
  { key: 'chess.com', label: 'chess.com' },
];

export const siteLabel = (site: string): string => ACCOUNT_SITES.find(s => s.key === site)?.label ?? site;

/** Absage des Servers beim Anlegen/Ändern als Satz. */
export function accountErrorText(reason: string | undefined): string {
  switch (reason) {
    case 'invalidSite': return 'Unbekannte Seite — Lichess oder chess.com.';
    case 'invalidUser': return 'Das ist kein gültiger Kontoname (und keine Profiladresse).';
    case 'duplicate': return 'Dieses Konto steht schon da.';
    case 'tooMany': return 'Mehr als 20 Konten je Spieler gehen nicht.';
    case 'unknownPlayer': return 'Diesen Spieler kennt LeagueHub nicht.';
    default: return 'Speichern hat nicht geklappt.';
  }
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
        @for (a of accounts(); track a.id ?? a.url) {
          <li>
            @if (editing() === a.id) {
              <ng-container *ngTemplateOutlet="form" />
            } @else {
              <div class="acc-row">
                <a [href]="a.url" target="_blank" rel="noopener">{{ label(a.site) }}: {{ a.user }}</a>
                <span class="tag" [class.tag-sure]="a.conf === 'sicher'">{{ a.conf === 'sicher' ? 'gesichert' : 'unsicher' }}</span>
                @if (canEdit() && a.id !== undefined) {
                  <button type="button" class="btn-link" [disabled]="busy()" (click)="startEdit(a)">Bearbeiten</button>
                  <button type="button" class="btn-link" [disabled]="busy()" (click)="remove(a)">Entfernen</button>
                }
              </div>
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
  imports: [NgTemplateOutlet],
})
export class OnlineAccountsComponent {
  readonly fide = input.required<string>();
  readonly accounts = input<Account[]>([]);
  readonly canEdit = input(false);
  readonly changed = output<void>();

  private readonly api = inject(LeagueApiService);
  readonly sites = ACCOUNT_SITES;
  readonly label = siteLabel;
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

  startAdd(): void {
    this.fill('lichess', '', false, '');
    this.adding.set(true);
  }

  startEdit(a: Account): void {
    this.fill(a.site, a.user, a.conf === 'sicher', a.comment ?? '');
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
    if (a.id === undefined || !confirm(`${siteLabel(a.site)}-Konto „${a.user}" entfernen? Die geholten Partien gehen mit.`)) return;
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
