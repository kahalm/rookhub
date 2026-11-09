import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { IsoDateFieldComponent } from '../../shared/date-field/iso-date-field.component';
import { GameDetails, PGN_RESULTS, PgnResult } from './analysis-game-pgn';

export type GameActionKind = 'download' | 'save' | 'club';

export interface ClubChoice { id: number; name: string; }

export interface GameDialogData {
  kind: GameActionKind;
  details: GameDetails;
  /** Nur bei `club`: die Vereine des Kontos; mehr als einer → Auswahl. */
  clubs?: ClubChoice[];
  clubId?: number | null;
}

export interface GameDialogResult { details: GameDetails; clubId: number | null; }

/** Kopfdaten für „PGN herunterladen", „In Meine Partien" und „In die Vereinsdatenbank" (Wunsch 2026-11-09). */
@Component({
  selector: 'app-analysis-game-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe,
    IsoDateFieldComponent],
  template: `
    <h2 mat-dialog-title>{{ title() | translate }}</h2>
    <mat-dialog-content>
      @if (data.kind === 'club' && clubs.length > 1) {
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'analysis.gameActions.club' | translate }}</mat-label>
          <mat-select [ngModel]="clubId()" (ngModelChange)="clubId.set($event)" name="club">
            @for (c of clubs; track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }
          </mat-select>
        </mat-form-field>
      }
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'analysis.gameActions.white' | translate }}</mat-label>
        <input matInput name="white" [ngModel]="white()" (ngModelChange)="white.set($event)" />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'analysis.gameActions.black' | translate }}</mat-label>
        <input matInput name="black" [ngModel]="black()" (ngModelChange)="black.set($event)" />
      </mat-form-field>
      <div class="pair">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'analysis.gameActions.result' | translate }}</mat-label>
          <mat-select name="result" [ngModel]="result()" (ngModelChange)="result.set($event)">
            @for (r of results; track r) {
              <mat-option [value]="r">{{ r === '*' ? ('analysis.gameActions.resultOpen' | translate) : r }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <app-iso-date-field [label]="'analysis.gameActions.date' | translate" [value]="date()" (valueChange)="date.set($event)" subscriptSizing="fixed" />
      </div>
      <div class="pair">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'analysis.gameActions.event' | translate }}</mat-label>
          <input matInput name="event" [ngModel]="event()" (ngModelChange)="event.set($event)" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'analysis.gameActions.round' | translate }}</mat-label>
          <input matInput name="round" [ngModel]="round()" (ngModelChange)="round.set($event)" />
        </mat-form-field>
      </div>
      @if (data.kind !== 'download') {
        <p class="hint">{{ 'analysis.gameActions.mainlineHint' | translate }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (reason(); as why) { <span class="dialog-disabled-reason">{{ why | translate }}</span> }
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" type="button" [disabled]="!!reason()" (click)="submit()">{{ confirm() | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full { width: 100%; }
    .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin: 0; }
    .dialog-disabled-reason { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin-right: auto; }
  `],
})
export class AnalysisGameDialogComponent {
  readonly data = inject<GameDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<AnalysisGameDialogComponent, GameDialogResult>);

  readonly results = PGN_RESULTS;
  readonly clubs = this.data.clubs ?? [];
  readonly clubId = signal<number | null>(this.data.clubId ?? (this.clubs.length === 1 ? this.clubs[0].id : null));
  readonly white = signal(this.data.details.white);
  readonly black = signal(this.data.details.black);
  readonly result = signal<PgnResult>(this.data.details.result);
  readonly date = signal(this.data.details.date);
  readonly event = signal(this.data.details.event);
  readonly round = signal(this.data.details.round);

  /** Warum „Übernehmen" gesperrt ist (DESIGN 5.6): der Verein braucht beide Spieler und einen Verein. */
  readonly reason = computed<string | null>(() => {
    if (this.data.kind !== 'club') return null;
    if (this.clubId() == null) return 'analysis.gameActions.needClub';
    return this.white().trim() && this.black().trim() ? null : 'analysis.gameActions.needNames';
  });

  title(): string {
    switch (this.data.kind) {
      case 'download': return 'common.downloadPgn';
      case 'save': return 'analysis.gameActions.saveTitle';
      default: return 'analysis.gameActions.clubTitle';
    }
  }

  confirm(): string {
    switch (this.data.kind) {
      case 'download': return 'analysis.gameActions.downloadConfirm';
      case 'save': return 'common.save';
      default: return 'analysis.gameActions.clubConfirm';
    }
  }

  submit(): void {
    if (this.reason()) return;
    this.ref.close({
      details: { ...this.data.details, white: this.white(), black: this.black(), result: this.result(), date: this.date(),
        event: this.event(), round: this.round() },
      clubId: this.clubId(),
    });
  }
}
