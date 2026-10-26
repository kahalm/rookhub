import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { defaultLockDay, lockEndFromDate, todayIso } from './lock-user.util';

export interface LockUserDialogData {
  username: string;
}

/** Ergebnis „Sperren“: Ende (ISO-UTC; `null` = unbefristet) und optionaler Grund. Abbrechen = `undefined`. */
export interface LockUserDialogResult {
  until: string | null;
  reason: string;
}

/**
 * „Sperren …“ in der Admin-Nutzerliste (Codereview P-4): bis einschließlich eines Tages oder unbefristet, optional mit
 * Grund (nur fürs Server-Protokoll). Der Dialog ist zugleich die Rückfrage. Native Felder, Zustand in Signalen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-lock-user-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'admin.users.lock.title' | translate: { username: data.username } }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'admin.users.lock.hint' | translate }}</p>
      <fieldset class="mode">
        <legend class="sr-only">{{ 'admin.users.lock.title' | translate: { username: data.username } }}</legend>
        <label class="opt">
          <input type="radio" name="lock-mode" value="until" [checked]="mode() === 'until'" (change)="mode.set('until')">
          {{ 'admin.users.lock.modeUntil' | translate }}
        </label>
        @if (mode() === 'until') {
          <label class="field">
            <span>{{ 'admin.users.lock.dateLabel' | translate }}</span>
            <input type="date" class="date" [min]="min" [value]="day()" (input)="day.set($any($event.target).value)">
          </label>
        }
        <label class="opt">
          <input type="radio" name="lock-mode" value="indefinite" [checked]="mode() === 'indefinite'" (change)="mode.set('indefinite')">
          {{ 'admin.users.lock.modeIndefinite' | translate }}
        </label>
      </fieldset>
      <label class="field">
        <span>{{ 'admin.users.lock.reasonLabel' | translate }}</span>
        <textarea class="reason" rows="2" maxlength="200" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
        <small class="muted">{{ 'admin.users.lock.reasonHint' | translate }}</small>
      </label>
      @if (invalid()) { <p class="error" role="alert">{{ 'admin.users.lock.invalidDate' | translate }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="warn" class="confirm" (click)="submit()">{{ 'admin.users.lock.confirm' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; max-width: 460px; }
    .hint { margin: 0 0 0.75rem; font-size: 0.875rem; }
    .mode { border: 0; padding: 0; margin: 0 0 0.75rem; display: flex; flex-direction: column; gap: 0.25rem; }
    .opt { display: flex; align-items: center; gap: 0.5rem; min-height: 40px; cursor: pointer; }
    .field { display: flex; flex-direction: column; gap: 0.25rem; margin: 0 0 0.5rem 1.75rem; }
    .mode + .field { margin-left: 0; }
    .date, .reason { font: inherit; color: inherit; padding: 0.5rem; border-radius: 4px; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent); background: var(--mat-sys-surface-container, transparent); }
    .reason { width: 100%; resize: vertical; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--rh-error, var(--mat-sys-error)); margin: 0.25rem 0 0; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
  `],
})
export class LockUserDialogComponent {
  readonly data = inject<LockUserDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<LockUserDialogComponent, LockUserDialogResult>>(MatDialogRef);

  readonly min = todayIso();
  readonly mode = signal<'until' | 'indefinite'>('until');
  readonly day = signal(defaultLockDay());
  readonly reason = signal('');
  readonly invalid = signal(false);

  submit(): void {
    let until: string | null = null;
    if (this.mode() === 'until') {
      until = this.day() >= this.min ? lockEndFromDate(this.day()) : null;
      if (!until) { this.invalid.set(true); return; }
    }
    this.ref.close({ until, reason: this.reason().trim() });
  }
}
