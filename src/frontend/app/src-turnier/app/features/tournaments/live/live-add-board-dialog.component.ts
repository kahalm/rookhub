import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '@rh/core/api-error';
import { ExternalRoundView } from './live-boards.models';
import { LiveBoardsService } from './live-boards.service';
import { LIVE_MAX_ADDED, LIVE_MAX_EXT, LIVE_MAX_EXT_ROUNDS, parseBroadcastLink, samePlayer } from './live-boards.util';

/** Ein Brett der eigenen Runde zur Auswahl: `key` wie in `?add=`. */
export interface AddBoardRow { key: string; board: number; white: string; black: string; match: string | null; }

export interface AddBoardDialogData {
  /** Bretter derselben Runde, die gerade NICHT gezeigt werden (leer ohne Mannschafts-/Spielerfilter — dann sieht man ohnehin alle). */
  rows: AddBoardRow[];
  added: string[];
  ext: string[];
}

export interface AddBoardDialogResult { added: string[]; ext: string[]; }

/**
 * „Brett hinzufügen“ der Live-Bretter (1.025.0, Wunsch 2026-11-26): oben die übrigen Partien der Runde mit Suche (nur mit
 * Mannschafts-/Spielerfilter), darunter ein Feld für einen Lichess-Übertragungs-Link (Partie → genau diese, Runde/Turnier →
 * Auswahl aus deren Partien). Antippen schaltet um (✓); „Fertig“ übernimmt die Auswahl in die Adresse. DESIGN 5.6:
 * Auswahl-Dialog 560 px, `autoFocus: 'dialog'`.
 */
@Component({
  selector: 'app-live-add-board-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'tournaments.live.addTitle' | translate }}</h2>
    <mat-dialog-content>
      @if (data.rows.length > 0) {
        <h3 class="ab-head">{{ 'tournaments.live.addSameRound' | translate }}</h3>
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ab-field">
          <mat-label>{{ 'tournaments.live.addSearch' | translate }}</mat-label>
          <mat-icon matPrefix aria-hidden="true">search</mat-icon>
          <input matInput type="search" [value]="query()" (input)="query.set($any($event.target).value)" autocomplete="off" />
        </mat-form-field>
        <ul class="ab-list" role="list">
          @for (r of shownRows(); track r.key) {
            <li>
              <button type="button" class="ab-row" [class.on]="added().includes(r.key)" [attr.aria-pressed]="added().includes(r.key)"
                      [disabled]="!added().includes(r.key) && added().length >= maxAdded" (click)="toggleAdd(r.key)">
                <mat-icon class="ab-check" aria-hidden="true">{{ added().includes(r.key) ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
                <span class="ab-text">
                  <span class="ab-players">{{ r.white }} – {{ r.black }}</span>
                  <span class="ab-meta">{{ 'tournaments.live.boardN' | translate:{ board: r.board } }}@if (r.match) { · {{ r.match }} }</span>
                </span>
              </button>
            </li>
          } @empty {
            <li class="ab-muted">{{ 'tournaments.live.addNoMatch' | translate }}</li>
          }
        </ul>
      }

      <h3 class="ab-head">{{ 'tournaments.live.addOther' | translate }}</h3>
      <form class="ab-link" (submit)="$event.preventDefault(); lookup()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ab-field">
          <mat-label>{{ 'tournaments.live.addLinkLabel' | translate }}</mat-label>
          <input matInput type="url" [value]="link()" (input)="link.set($any($event.target).value); linkError.set(null)"
                 placeholder="https://lichess.org/broadcast/…" autocomplete="off" />
          <mat-hint>{{ 'tournaments.live.addLinkHint' | translate }}</mat-hint>
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="loading() || !link().trim()">{{ 'tournaments.live.addLoad' | translate }}</button>
      </form>
      @if (linkError(); as e) { <p class="ab-error" role="alert">{{ e }}</p> }
      @if (round(); as rv) {
        <p class="ab-round">{{ rv.tourName }} · {{ rv.roundName }}</p>
        <ul class="ab-list" role="list">
          @for (g of rv.games; track g.id) {
            <li>
              <button type="button" class="ab-row" [class.on]="ext().includes(rv.roundId + '.' + g.id)"
                      [attr.aria-pressed]="ext().includes(rv.roundId + '.' + g.id)"
                      [disabled]="!ext().includes(rv.roundId + '.' + g.id) && !canAddExt(rv.roundId)" (click)="toggleExt(rv.roundId + '.' + g.id)">
                <mat-icon class="ab-check" aria-hidden="true">{{ ext().includes(rv.roundId + '.' + g.id) ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
                <span class="ab-text">
                  <span class="ab-players">{{ g.white }} – {{ g.black }}</span>
                  @if (g.result !== '*') { <span class="ab-meta">{{ g.result }}</span> }
                </span>
              </button>
            </li>
          } @empty {
            <li class="ab-muted">{{ 'tournaments.live.noGames' | translate }}</li>
          }
        </ul>
      }
      @if (ext().length >= maxExt) { <p class="ab-muted">{{ 'tournaments.live.addLimit' | translate:{ max: maxExt } }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button type="button" (click)="done()">{{ 'tournaments.live.addDone' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ab-head { margin: 8px 0; font-size: 1rem; font-weight: 500; }
    .ab-head:first-child { margin-top: 0; }
    .ab-field { width: 100%; }
    .ab-list { list-style: none; margin: 4px 0 12px; padding: 0; max-height: 40vh; overflow-y: auto; }
    .ab-row { display: flex; align-items: center; gap: 8px; width: 100%; min-height: 44px; padding: 4px 8px; border: 0; border-radius: 8px;
      background: transparent; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .ab-row:hover:not([disabled]) { background: color-mix(in srgb, currentColor 6%, transparent); }
    .ab-row[disabled] { cursor: default; opacity: 0.5; }
    .ab-row.on .ab-check { color: var(--rh-accent); }
    .ab-text { display: flex; flex-direction: column; min-width: 0; }
    .ab-players { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ab-meta, .ab-muted { font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .ab-muted { padding: 4px 8px; }
    .ab-link { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-start; }
    .ab-link .ab-field { flex: 1 1 280px; width: auto; min-width: 0; }
    .ab-link button { margin-top: 8px; }
    .ab-round { margin: 12px 0 0; font-weight: 500; overflow-wrap: anywhere; }
    .ab-error { margin: 4px 0 0; color: var(--rh-error); font-size: 0.875rem; }
  `],
})
export class LiveAddBoardDialogComponent {
  readonly data = inject<AddBoardDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<LiveAddBoardDialogComponent, AddBoardDialogResult>);
  private readonly api = inject(LiveBoardsService);
  private readonly translate = inject(TranslateService);

  readonly maxAdded = LIVE_MAX_ADDED;
  readonly maxExt = LIVE_MAX_EXT;
  readonly query = signal('');
  readonly added = signal<string[]>([...this.data.added]);
  readonly ext = signal<string[]>([...this.data.ext]);
  readonly link = signal('');
  readonly linkError = signal<string | null>(null);
  readonly loading = signal(false);
  readonly round = signal<ExternalRoundView | null>(null);

  /** Suche: Spielername (Reihenfolge/Komma egal) oder Begegnung. */
  readonly shownRows = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.data.rows;
    return this.data.rows.filter(r => [r.white, r.black, r.match ?? ''].some(t => t.toLowerCase().includes(q))
      || samePlayer(r.white, q) || samePlayer(r.black, q));
  });

  toggleAdd(key: string): void {
    this.added.update(l => l.includes(key) ? l.filter(k => k !== key) : l.length >= LIVE_MAX_ADDED ? l : [...l, key]);
  }

  toggleExt(key: string): void {
    this.ext.update(l => l.includes(key) ? l.filter(k => k !== key) : this.canAddExt(key.slice(0, 8)) ? [...l, key] : l);
  }

  /** Noch Platz: höchstens 12 fremde Bretter aus höchstens 4 Runden (wie der Server). */
  canAddExt(roundId: string): boolean {
    const l = this.ext();
    if (l.length >= LIVE_MAX_EXT) return false;
    const rounds = new Set(l.map(k => k.slice(0, 8)));
    return rounds.has(roundId) || rounds.size < LIVE_MAX_EXT_ROUNDS;
  }

  /** Link prüfen (im Browser und im Server), Runde laden; ein Partie-Link wählt seine Partie gleich aus. */
  lookup(): void {
    const url = this.link().trim();
    if (!parseBroadcastLink(url)) { this.linkError.set(this.translate.instant('apiErrors.live_invalid_url')); return; }
    this.loading.set(true);
    this.linkError.set(null);
    this.api.external(url).subscribe({
      next: v => {
        this.loading.set(false);
        this.round.set(v);
        const key = v.gameId ? `${v.roundId}.${v.gameId}` : null;
        if (key && !this.ext().includes(key)) this.toggleExt(key);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.round.set(null);
        this.linkError.set(apiErrorText(err, this.translate, 'tournaments.live.addLoadFailed'));
      },
    });
  }

  done(): void { this.ref.close({ added: this.added(), ext: this.ext() }); }
}
