import { Directive, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable, forkJoin } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FriendsService } from '../../core/friends.service';
import { Friend } from '../../core/models';
import { SnackbarService } from '../../core/snackbar.service';

/** Nutzer, mit dem das Objekt bereits geteilt ist (identisch für Kurs- und Repertoire-Freigaben). */
export interface ShareRecipient {
  userId: number;
  username: string;
  displayName: string | null;
  sharedAt: string;
}

/** Antwort eines Batch-Teilens: wie viele gingen durch, wie viele wurden übersprungen. */
export interface ShareBatchResult {
  shared: number;
  skipped?: unknown[];
}

/**
 * Bausteine des Teilen-Dialogs (Kurs/Repertoire/…): Multi-Select über die Freundesliste plus
 * Liste der bereits belieferten Nutzer mit Zurücknehmen-Knopf.
 *
 * FALLE: Template UND Styles liegen bewusst als exportierte Konstanten hier — jede konkrete
 * Dialog-Klasse (Kurs, Repertoire) referenziert sie in ihrem @Component. Wer stattdessen das
 * Markup kopiert, pflegt jede UI-Änderung doppelt; genau so sind die beiden Dialoge früher
 * auseinandergedriftet. Die i18n-Keys unterscheiden sich nur im Namensraum (`i18nPrefix`),
 * die Unterschlüssel (title/hint/sharedWith/…) heißen in allen Namensräumen gleich.
 */
export const SHARE_DIALOG_IMPORTS = [
  CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatCheckboxModule,
  MatProgressSpinnerModule, MatTooltipModule, TranslatePipe,
];

export const SHARE_DIALOG_TEMPLATE = `
    <h2 mat-dialog-title>{{ tk('title') | translate:{ name: targetName } }}</h2>
    <mat-dialog-content>
      @if (loading) {
        <div class="center"><mat-spinner diameter="32"></mat-spinner></div>
      } @else {
        <p class="hint">{{ tk('hint') | translate }}</p>

        @if (recipients.length > 0) {
          <div class="section">
            <h3>{{ tk('sharedWith') | translate }}</h3>
            <ul class="recipient-list">
              @for (r of recipients; track r.userId) {
                <li class="recipient-row">
                  <mat-icon class="ok-icon">check_circle</mat-icon>
                  <span class="rname">{{ r.displayName || r.username }}</span>
                  <button mat-icon-button class="remove-btn" [disabled]="busy"
                          [matTooltip]="tk('unshareTooltip') | translate"
                          (click)="unshare(r)">
                    <mat-icon>close</mat-icon>
                  </button>
                </li>
              }
            </ul>
          </div>
        }

        <div class="section">
          <h3>{{ tk('pickFriends') | translate }}</h3>
          @if (selectableFriends.length === 0) {
            <p class="empty">{{ tk('noFriends') | translate }}</p>
          } @else {
            <div class="friend-list">
              @for (f of selectableFriends; track f.userId) {
                <mat-checkbox [checked]="selected.has(f.userId)" (change)="toggle(f.userId, $event.checked)">
                  {{ f.displayName || f.username }}
                </mat-checkbox>
              }
            </div>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.close' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="selected.size === 0 || busy" (click)="share()">
        {{ tk('shareButton') | translate:{ count: selected.size } }}
      </button>
    </mat-dialog-actions>
  `;

export const SHARE_DIALOG_STYLES = `
    .center { display: flex; justify-content: center; padding: 24px; }
    .hint { margin: 0 0 12px; font-size: 0.88rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .section { margin-bottom: 14px; min-width: min(360px, 80vw); }
    .section h3 { font-size: 0.85rem; font-weight: 600; margin: 0 0 6px; opacity: 0.75; }
    .recipient-list { list-style: none; margin: 0; padding: 0; }
    .recipient-row { display: flex; align-items: center; gap: 8px; padding: 2px 0; }
    .ok-icon { color: #4caf50; font-size: 18px; width: 18px; height: 18px; }
    .rname { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .remove-btn { width: 30px; height: 30px; padding: 3px; }
    .remove-btn mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .friend-list { display: flex; flex-direction: column; gap: 4px; max-height: 260px; overflow-y: auto; }
    .empty { font-style: italic; font-size: 0.85rem; color: color-mix(in srgb, currentColor 55%, transparent); margin: 0; }
  `;

/**
 * Ablauf-Logik des Teilen-Dialogs. Die Ableitung liefert nur noch den i18n-Namensraum, den
 * Anzeigenamen und die drei Service-Aufrufe (laden/teilen/zurücknehmen).
 *
 * FALLE: `@Directive()` ohne Selektor ist Pflicht — eine Basisklasse, die Angular-Features
 * (inject/ngOnInit) nutzt, muss dekoriert sein, sonst bricht der AOT-Build mit NG2007.
 */
@Directive()
export abstract class ShareWithFriendsDialogBase implements OnInit {
  loading = true;
  busy = false;
  friends: Friend[] = [];
  recipients: ShareRecipient[] = [];
  selected = new Set<number>();

  readonly dialogRef = inject<MatDialogRef<unknown>>(MatDialogRef);
  private friendsService = inject(FriendsService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  /** i18n-Namensraum der Dialog-Texte, z. B. „courses.share". */
  protected abstract readonly i18nPrefix: string;
  /** Name des geteilten Objekts (steht im Dialogtitel). */
  abstract get targetName(): string;

  protected abstract loadRecipients(): Observable<ShareRecipient[]>;
  protected abstract shareWith(userIds: number[]): Observable<ShareBatchResult>;
  protected abstract unshareFrom(userId: number): Observable<void>;

  /** Voller i18n-Key im Namensraum des konkreten Dialogs. */
  tk(key: string): string {
    return `${this.i18nPrefix}.${key}`;
  }

  ngOnInit(): void {
    forkJoin({
      friends: this.friendsService.getFriends(),
      recipients: this.loadRecipients(),
    }).subscribe({
      next: ({ friends, recipients }) => {
        this.friends = friends;
        this.recipients = recipients;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant(this.tk('loadFailed')), { action: 'common.ok', duration: 3000 });
        this.dialogRef.close();
      },
    });
  }

  /** Freunde, die noch nicht Empfänger sind (bereits geteilte stehen in der oberen Liste). */
  get selectableFriends(): Friend[] {
    const shared = new Set(this.recipients.map(r => r.userId));
    return this.friends.filter(f => !shared.has(f.userId));
  }

  toggle(userId: number, checked: boolean): void {
    if (checked) this.selected.add(userId); else this.selected.delete(userId);
  }

  share(): void {
    if (this.selected.size === 0 || this.busy) return;
    this.busy = true;
    const ids = [...this.selected];
    this.shareWith(ids).subscribe({
      next: res => {
        this.busy = false;
        this.selected.clear();
        // Frisch geteilte Empfänger oben einsortieren (aus der geladenen Freundesliste).
        const known = new Set(this.recipients.map(r => r.userId));
        for (const f of this.friends) {
          if (ids.includes(f.userId) && !known.has(f.userId)) {
            this.recipients = [...this.recipients, {
              userId: f.userId, username: f.username, displayName: f.displayName, sharedAt: new Date().toISOString(),
            }];
          }
        }
        const skipped = res.skipped?.length ?? 0;
        if (res.shared > 0 && skipped === 0) {
          this.snackbar.success(this.translate.instant(this.tk('shared'), { count: res.shared }));
        } else if (res.shared > 0) {
          this.snackbar.info(this.translate.instant(this.tk('sharedPartial'), { shared: res.shared, skipped }));
        } else {
          this.snackbar.info(this.translate.instant(this.tk('nothingShared')), { action: 'common.ok', duration: 3000 });
        }
      },
      error: err => {
        this.busy = false;
        this.snackbar.info(err?.error?.message || this.translate.instant(this.tk('failed')), { action: 'common.ok', duration: 3000 });
      },
    });
  }

  unshare(r: ShareRecipient): void {
    if (this.busy) return;
    this.busy = true;
    this.unshareFrom(r.userId).subscribe({
      next: () => {
        this.busy = false;
        this.recipients = this.recipients.filter(x => x.userId !== r.userId);
        this.snackbar.info(this.translate.instant(this.tk('unshared'), { name: r.displayName || r.username }), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.busy = false;
        this.snackbar.info(this.translate.instant(this.tk('failed')), { action: 'common.ok', duration: 3000 });
      },
    });
  }
}
