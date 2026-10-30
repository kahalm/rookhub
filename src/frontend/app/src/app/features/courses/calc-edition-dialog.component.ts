import { ChangeDetectionStrategy, Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { DateTimeFieldsComponent } from '../../shared/date-field/date-time-fields.component';
import { CalcEdition, CalcEditionInput } from './calc-editions.service';

export interface CalcEditionDialogData { chapter: string; edition?: CalcEdition; }
export type CalcEditionDialogResult = { save: CalcEditionInput } | { delete: true };

/** ISO-UTC → Lokalzeit „YYYY-MM-DDTHH:mm" (Wert von app-date-time-fields). */
function isoToLocalInput(iso?: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}
/** Lokalzeit „YYYY-MM-DDTHH:mm" → ISO-UTC. */
function localInputToIso(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);          // interpretiert die naive Angabe als LOKALE Zeit
  return isNaN(d.getTime()) ? null : d.toISOString();
}

@Component({
  selector: 'app-calc-edition-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, DateTimeFieldsComponent, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (data.edition ? 'calc.series.editEdition' : 'calc.series.newEdition') | translate }} — {{ data.chapter }}</h2>
    <mat-dialog-content class="ced">
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'calc.series.videoUrl' | translate }}</mat-label>
        <input matInput [(ngModel)]="videoUrl" placeholder="https://www.youtube.com/watch?v=…">
      </mat-form-field>
      <!-- d8-rk-ausgabe-datetime-us-format: Datum + Uhrzeit in der App-Sprache statt datetime-local (DESIGN 5.13). -->
      <app-date-time-fields class="full" [(value)]="publishLocal" [required]="true"
                            [dateLabel]="'calc.series.publishAt' | translate" [timeLabel]="'weekly.fields.time' | translate" />
      <app-date-time-fields class="full" [(value)]="testerLocal"
                            [dateLabel]="'calc.series.testerPreviewAt' | translate" [timeLabel]="'weekly.fields.time' | translate"
                            [hint]="'common.optional' | translate" />
      <p class="hint">{{ 'calc.series.hint' | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.edition) {
        <button mat-button color="warn" (click)="onDelete()"><mat-icon>delete</mat-icon> {{ 'common.delete' | translate }}</button>
        <span class="spacer"></span>
      }
      <!-- d7-rk-ausgabe-speichern-ohne-grund: gesperrtes „Speichern“ sagt, was fehlt (DESIGN 5.4). -->
      @if (!publishLocal) { <span class="why" role="status">{{ 'calc.series.publishRequired' | translate }}</span> }
      <button mat-button (click)="ref.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!publishLocal" (click)="onSave()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Oberes Polster für das schwebende Label kommt global aus styles.scss (d7-rk-dialog-erstes-feld-label-abgeschnitten);
       das eigene padding-top hier kam gegen Materials Regel nie an. */
    .ced { display: flex; flex-direction: column; min-width: min(320px, 80vw); }
    .full { width: 100%; }
    /* Dynamischer Hinweisbereich reserviert unten nichts — Luft für die schwebende Beschriftung darunter. */
    app-date-time-fields { margin-bottom: 12px; }
    /* Im Dialog ist currentColor schon on-surface-variant — 60 % davon war doppelt abgeblasst (d7-rk-dialog-hint-kontrast). */
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 12px; margin: 0; }
    /* Knopfleiste mit Grund (.why): global in styles.scss (DESIGN 5.6, d8-rk-dialog-aktionen-umbruch). */
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
