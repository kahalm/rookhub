import { ChangeDetectionStrategy, Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CalcEditionsService, CalcSeriesMember } from './calc-editions.service';
import { SnackbarService } from '../../core/snackbar.service';

export interface CalcMembersDialogData { bookId: number; }

/**
 * Verwaltung des privaten Serien-Verteilers (Phase 2b): Mitglieder per Benutzername hinzufügen,
 * das Tester-Häkchen setzen und Mitglieder entfernen. Der Dialog hält seinen Zustand selbst und
 * spricht direkt mit {@link CalcEditionsService} — der Aufrufer (Kurs-Detailseite) muss nichts
 * nachladen. Zugriff ist serverseitig auf Besitzer/Admin beschränkt.
 */
@Component({
  selector: 'app-calc-members-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatSlideToggleModule, MatProgressBarModule, MatTooltipModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'calc.series.members' | translate }}</h2>
    <mat-dialog-content class="cmd">
      <p class="hint">{{ 'calc.series.membersHint' | translate }}</p>

      <div class="add-row">
        <mat-form-field appearance="outline" class="grow">
          <mat-label>{{ 'calc.series.username' | translate }}</mat-label>
          <input matInput [(ngModel)]="newUsername" (keydown.enter)="add()" [disabled]="busy">
        </mat-form-field>
        <mat-slide-toggle [(ngModel)]="newIsTester" [disabled]="busy">{{ 'calc.series.tester' | translate }}</mat-slide-toggle>
        <button mat-flat-button color="primary" [disabled]="busy || !newUsername.trim()" (click)="add()">
          <mat-icon>person_add</mat-icon> {{ 'calc.series.addMember' | translate }}
        </button>
      </div>

      @if (busy) { <mat-progress-bar mode="indeterminate"></mat-progress-bar> }

      @if (loaded && members.length === 0) {
        <p class="empty">{{ 'calc.series.noMembers' | translate }}</p>
      }
      <ul class="members">
        @for (m of members; track m.userId) {
          <li class="member">
            <span class="name">{{ m.username }}</span>
            <mat-slide-toggle [checked]="m.isTester" [disabled]="busy"
                              (change)="setTester(m, $event.checked)"
                              [matTooltip]="'calc.series.testerHint' | translate">{{ 'calc.series.tester' | translate }}</mat-slide-toggle>
            <button mat-icon-button color="warn" [disabled]="busy" (click)="remove(m)"
                    [attr.aria-label]="'calc.series.removeMember' | translate">
              <mat-icon>delete</mat-icon>
            </button>
          </li>
        }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .cmd { min-width: 360px; }
    .hint { color: #9aa4b2; font-size: 12px; margin: 0 0 8px; }
    .add-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .add-row .grow { flex: 1 1 180px; }
    .empty { color: #9aa4b2; font-style: italic; }
    .members { list-style: none; margin: 8px 0 0; padding: 0; }
    .member { display: flex; align-items: center; gap: 12px; padding: 6px 0; border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .member .name { flex: 1 1 auto; font-weight: 500; }
  `],
})
export class CalcMembersDialogComponent {
  members: CalcSeriesMember[] = [];
  loaded = false;
  busy = false;
  newUsername = '';
  newIsTester = false;

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: CalcMembersDialogData,
    private service: CalcEditionsService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {
    this.reload();
  }

  private reload(): void {
    this.busy = true;
    this.service.members(this.data.bookId).subscribe({
      next: list => { this.members = list; this.loaded = true; this.busy = false; },
      error: () => { this.busy = false; this.snackbar.warn(this.translate.instant('calc.series.membersLoadFailed')); },
    });
  }

  add(): void {
    const username = this.newUsername.trim();
    if (!username || this.busy) return;
    this.busy = true;
    this.service.upsertMember(this.data.bookId, { username, isTester: this.newIsTester }).subscribe({
      next: () => {
        this.newUsername = '';
        this.newIsTester = false;
        this.snackbar.quick(this.translate.instant('calc.series.memberAdded'));
        this.reload();
      },
      error: (err: HttpErrorResponse) => {
        this.busy = false;
        // 404 = es gibt keinen Nutzer mit diesem Namen (kein stiller Fehlschlag: der Nutzer hat gerade getippt).
        const key = err?.status === 404 ? 'calc.series.userNotFound' : 'calc.series.saveFailed';
        this.snackbar.warn(this.translate.instant(key));
      },
    });
  }

  setTester(m: CalcSeriesMember, isTester: boolean): void {
    this.busy = true;
    this.service.upsertMember(this.data.bookId, { username: m.username, isTester }).subscribe({
      next: () => { m.isTester = isTester; this.busy = false; },
      error: () => { this.busy = false; this.snackbar.warn(this.translate.instant('calc.series.saveFailed')); this.reload(); },
    });
  }

  remove(m: CalcSeriesMember): void {
    this.busy = true;
    this.service.removeMember(this.data.bookId, m.userId).subscribe({
      next: () => { this.snackbar.quick(this.translate.instant('calc.series.memberRemoved')); this.reload(); },
      error: () => { this.busy = false; this.snackbar.warn(this.translate.instant('calc.series.saveFailed')); },
    });
  }
}
