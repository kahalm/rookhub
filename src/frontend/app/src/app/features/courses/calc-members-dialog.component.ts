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
import { forkJoin } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CalcEditionsService, CalcSeriesMember } from './calc-editions.service';
import { SnackbarService } from '../../core/snackbar.service';

export interface CalcMembersDialogData { bookId: number; }

/**
 * Verwaltung des privaten Serien-Verteilers: Mitglieder per Benutzername hinzufügen, das Tester-Häkchen
 * setzen und Mitglieder entfernen (Phase 2b). Zusätzlich zeigt der Dialog je Mitglied, WIE VIELE der
 * bereits freigegebenen Wochen es schon geöffnet hat (Phase 3c, „Gesehen"; Quelle: `GET .../views`).
 * Der Dialog hält seinen Zustand selbst und spricht direkt mit {@link CalcEditionsService} — der
 * Aufrufer (Kurs-Detailseite) muss nichts nachladen. Zugriff ist serverseitig auf Besitzer/Admin beschränkt.
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
            <!-- „Gesehen": N von M freigegebenen Wochen geöffnet (nur wenn schon etwas freigegeben ist). -->
            @if (releasedCount > 0) {
              <span class="seen" [class.seen--none]="seenCount(m) === 0"
                    [matTooltip]="seenTooltip(m)">
                <mat-icon inline>visibility</mat-icon>{{ 'calc.series.seenCount' | translate:{ seen: seenCount(m), total: releasedCount } }}
              </span>
            }
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
    .cmd { min-width: 380px; }
    .hint { color: #9aa4b2; font-size: 12px; margin: 0 0 8px; }
    .add-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .add-row .grow { flex: 1 1 180px; }
    .empty { color: #9aa4b2; font-style: italic; }
    .members { list-style: none; margin: 8px 0 0; padding: 0; }
    .member { display: flex; align-items: center; gap: 12px; padding: 6px 0; border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .member .name { flex: 1 1 auto; font-weight: 500; }
    .seen { display: inline-flex; align-items: center; gap: 2px; font-size: 12px; white-space: nowrap; color: #2e7d32; cursor: default; }
    .seen mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .seen--none { color: #9aa4b2; }
  `],
})
export class CalcMembersDialogComponent {
  members: CalcSeriesMember[] = [];
  loaded = false;
  busy = false;
  newUsername = '';
  newIsTester = false;

  /** Anzahl bereits FREIGEGEBENER Wochen (= Nenner der „Gesehen"-Anzeige). */
  releasedCount = 0;
  /** Je Nutzer die Namen der freigegebenen Wochen, die er geöffnet hat. */
  private seenByUser: Record<number, string[]> = {};

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
    // KRITISCH: die Mitgliederliste — das ist der Zweck des Dialogs. Schlägt sie fehl, melden.
    this.service.members(this.data.bookId).subscribe({
      next: members => { this.members = members; this.loaded = true; this.busy = false; },
      error: () => { this.busy = false; this.snackbar.warn(this.translate.instant('calc.series.membersLoadFailed')); },
    });
    // BEST-EFFORT: die „Gesehen"-Anzeige (N/M). Fällt sie aus, bleibt die Verwaltung voll nutzbar —
    // dann nur ohne die Zähler (kein eigener Fehler, um die Mitglieder-Verwaltung nicht zu stören).
    forkJoin({
      editions: this.service.manage(this.data.bookId),
      views: this.service.views(this.data.bookId),
    }).subscribe({
      next: ({ editions, views }) => {
        const releasedChapters = new Set(editions.filter(e => e.released).map(e => e.chapter));
        this.releasedCount = releasedChapters.size;
        // Nur Sichten auf inzwischen freigegebene Wochen zählen (eine später wieder verborgene Woche
        // soll den Zähler nicht über den Nenner heben).
        const map: Record<number, Set<string>> = {};
        for (const v of views) {
          if (!releasedChapters.has(v.chapter)) continue;
          (map[v.userId] ??= new Set()).add(v.chapter);
        }
        this.seenByUser = Object.fromEntries(Object.entries(map).map(([k, set]) => [k, [...set].sort()]));
      },
      error: () => { this.releasedCount = 0; this.seenByUser = {}; },
    });
  }

  /** Wie viele freigegebene Wochen dieses Mitglied geöffnet hat. */
  seenCount(m: CalcSeriesMember): number {
    return this.seenByUser[m.userId]?.length ?? 0;
  }

  /** Tooltip: die Namen der gesehenen Wochen (oder „noch nicht gesehen"). */
  seenTooltip(m: CalcSeriesMember): string {
    const chapters = this.seenByUser[m.userId];
    return chapters?.length ? chapters.join(', ') : this.translate.instant('calc.series.seenNone');
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
