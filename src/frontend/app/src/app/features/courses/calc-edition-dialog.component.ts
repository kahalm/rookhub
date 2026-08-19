import { ChangeDetectionStrategy, Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { CalcEdition, CalcEditionInput } from './calc-editions.service';

export interface CalcEditionDialogData { chapter: string; edition?: CalcEdition; }
export type CalcEditionDialogResult = { save: CalcEditionInput } | { delete: true };

/** ISO-UTC → lokaler `datetime-local`-Wert („YYYY-MM-DDTHH:mm"). */
function isoToLocalInput(iso?: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}
/** Lokaler `datetime-local`-Wert → ISO-UTC. */
function localInputToIso(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);          // interpretiert die naive Angabe als LOKALE Zeit
  return isNaN(d.getTime()) ? null : d.toISOString();
}

@Component({
  selector: 'app-calc-edition-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (data.edition ? 'calc.series.editEdition' : 'calc.series.newEdition') | translate }} — {{ data.chapter }}</h2>
    <mat-dialog-content class="ced">
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'calc.series.videoUrl' | translate }}</mat-label>
        <input matInput [(ngModel)]="videoUrl" placeholder="https://www.youtube.com/watch?v=…">
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'calc.series.publishAt' | translate }}</mat-label>
        <input matInput type="datetime-local" [(ngModel)]="publishLocal">
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'calc.series.testerPreviewAt' | translate }}</mat-label>
        <input matInput type="datetime-local" [(ngModel)]="testerLocal">
      </mat-form-field>
      <p class="hint">{{ 'calc.series.hint' | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.edition) {
        <button mat-button color="warn" (click)="onDelete()"><mat-icon>delete</mat-icon> {{ 'common.delete' | translate }}</button>
      }
      <span class="spacer"></span>
      <button mat-button (click)="ref.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!publishLocal" (click)="onSave()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ced { display: flex; flex-direction: column; min-width: 320px; padding-top: 8px; }
    .full { width: 100%; }
    .hint { color: #9aa4b2; font-size: 12px; margin: 0; }
    .spacer { flex: 1 1 auto; }
  `],
})
export class CalcEditionDialogComponent {
  videoUrl = '';
  publishLocal = '';
  testerLocal = '';

  constructor(
    public ref: MatDialogRef<CalcEditionDialogComponent, CalcEditionDialogResult>,
    @Inject(MAT_DIALOG_DATA) public data: CalcEditionDialogData,
  ) {
    const e = data.edition;
    this.videoUrl = e?.videoUrl ?? '';
    this.publishLocal = isoToLocalInput(e?.publishAt);
    this.testerLocal = isoToLocalInput(e?.testerPreviewAt);
  }

  onSave(): void {
    const publishAt = localInputToIso(this.publishLocal);
    if (!publishAt) return;
    this.ref.close({ save: {
      chapter: this.data.chapter,
      videoUrl: this.videoUrl.trim() || null,
      publishAt,
      testerPreviewAt: localInputToIso(this.testerLocal),
    } });
  }
  onDelete(): void { this.ref.close({ delete: true }); }
}
