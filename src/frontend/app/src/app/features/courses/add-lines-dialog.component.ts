import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { AddCourseLinesResult, CourseService } from './course.service';

export interface AddLinesDialogData {
  bookId: number;
  displayName: string;
  /** Vorbelegtes Kapitel. `undefined` = neues Kapitel anlegen (Name frei eingeben);
   *  `null` = „ohne Kapitel"; String = in dieses bestehende Kapitel einfügen. */
  chapter?: string | null;
  /** true = der Kapitelname ist fest (Einfügen in ein bestehendes Kapitel). */
  chapterLocked: boolean;
}

/**
 * „Kapitel hinzufügen" / „Linien hinzufügen": Kapitelname + Memo-Feld, in das eine Liste von
 * Stellungen hineinkopiert wird (eine je Zeile, optional nummeriert und mit Kommentar). Geparst
 * wird serverseitig (`POST /api/courses/{id}/lines`); verworfene Zeilen kommen mit Zeilennummer
 * und Grund zurück und werden hier aufgelistet, damit man sie korrigieren kann, statt dass der
 * ganze Einfüge-Vorgang scheitert.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-add-lines-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule,
    MatInputModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>
      {{ (data.chapterLocked ? 'courses.detail.addLinesTitle' : 'courses.detail.addChapterTitle') | translate }}
    </h2>
    <mat-dialog-content>
      @if (data.chapterLocked) {
        <p class="hint">{{ 'courses.detail.addLinesHint' | translate:{ chapter: chapterLabel } }}</p>
      } @else {
        <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
          <mat-label>{{ 'courses.detail.chapterName' | translate }}</mat-label>
          <input matInput [(ngModel)]="chapter" maxlength="200"
                 [placeholder]="'courses.detail.chapterNamePlaceholder' | translate" />
        </mat-form-field>
        <p class="hint">{{ 'courses.detail.chapterNameHint' | translate }}</p>
      }

      <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
        <mat-label>{{ 'courses.detail.positions' | translate }}</mat-label>
        <textarea matInput rows="12" [(ngModel)]="text" spellcheck="false"
                  [placeholder]="placeholder"></textarea>
      </mat-form-field>
      <p class="hint format">{{ 'courses.detail.formatHint' | translate }}</p>

      @if (result) {
        <div class="result">
          <p class="added">
            <mat-icon>check_circle</mat-icon>
            {{ 'courses.detail.addedCount' | translate:{ count: result.added } }}
          </p>
          @if (result.issues.length) {
            <p class="issues-head">{{ 'courses.detail.skippedCount' | translate:{ count: result.issues.length } }}</p>
            <ul class="issues">
              @for (issue of result.issues; track $index) {
                <li>
                  <span class="ln">{{ 'courses.detail.line' | translate:{ number: issue.lineNumber } }}</span>
                  <span class="reason">{{ ('courses.detail.reason.' + issue.reason) | translate }}</span>
                  <code>{{ issue.text }}</code>
                </li>
              }
            </ul>
          }
        </div>
      }
      @if (error) { <p class="error">{{ error }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="close()">{{ (result ? 'common.close' : 'common.cancel') | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="saving || !text.trim()" (click)="save()">
        @if (saving) { <mat-spinner diameter="18"></mat-spinner> }
        {{ 'courses.detail.insert' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full { width: 100%; }
    .hint { margin: 6px 0 12px; font-size: .85rem; opacity: .8; }
    .format { white-space: pre-line; font-size: .8rem; }
    textarea { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
    .result { margin-top: 10px; }
    .added { display: flex; align-items: center; gap: 6px; margin: 0 0 6px; font-weight: 500; }
    .added mat-icon { color: #2e7d32; font-size: 18px; width: 18px; height: 18px; }
    .issues-head { margin: 6px 0 4px; font-size: .85rem; color: #e65100; }
    .issues { margin: 0; padding-left: 18px; font-size: .8rem; display: flex; flex-direction: column; gap: 3px; }
    .issues .ln { font-weight: 600; margin-right: 4px; }
    .issues .reason { margin-right: 6px; }
    .issues code { word-break: break-all; opacity: .7; }
    .error { color: #b71c1c; font-size: .85rem; }
    mat-dialog-content { min-width: min(560px, 80vw); }
  `],
})
export class AddLinesDialogComponent {
  chapter = '';
  text = '';
  saving = false;
  error = '';
  result: AddCourseLinesResult | null = null;
  /** Wurde etwas eingefügt? Steuert, ob die Seite nach dem Schließen neu lädt. */
  private changed = false;

  readonly placeholder =
    '1: r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18\n' +
    '2: r2qk2r/p3bppp/Q4n2/4p1N1/3n4/8/PPPP1PPP/RNB2RK1 b kq - 0 12';

  constructor(
    private ref: MatDialogRef<AddLinesDialogComponent, boolean>,
    private courses: CourseService,
    @Inject(MAT_DIALOG_DATA) public data: AddLinesDialogData,
  ) {
    if (data.chapterLocked && typeof data.chapter === 'string') this.chapter = data.chapter;
  }

  get chapterLabel(): string {
    return typeof this.data.chapter === 'string' && this.data.chapter.length > 0 ? this.data.chapter : '—';
  }

  save(): void {
    const text = this.text.trim();
    if (!text || this.saving) return;
    this.saving = true;
    this.error = '';
    const chapter = this.data.chapterLocked ? (this.data.chapter ?? null) : (this.chapter.trim() || null);
    this.courses.addLines(this.data.bookId, chapter, text).subscribe({
      next: res => {
        this.saving = false;
        this.result = res;
        if (res.added > 0) {
          this.changed = true;
          this.text = '';                       // übernommene Zeilen nicht versehentlich doppelt schicken
        }
      },
      error: err => {
        this.saving = false;
        this.error = err?.error?.message || 'Fehler';
      },
    });
  }

  close(): void { this.ref.close(this.changed); }
}
