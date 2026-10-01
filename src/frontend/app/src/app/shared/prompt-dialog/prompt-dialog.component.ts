import { Component, ChangeDetectionStrategy, Injectable, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { map, Observable } from 'rxjs';

/** Ein Eingabefeld der Abfrage. `label` ist bereits übersetzter Text, `value` die Vorbelegung. */
export interface PromptField {
  key: string;
  label: string;
  value?: string | null;
}

/** Was die Abfrage zeigt; Titel und Hinweis sind bereits übersetzt. */
export interface PromptData {
  title?: string;
  hint?: string;
  fields: PromptField[];
}

/**
 * Texteingabe als Material-Dialog statt `window.prompt` (Codereview W5 F5-016).
 *
 * <p>Die native Abfrage fragt nur EIN Feld ab — die Kinder-Titel kamen deshalb als vier Abfragen
 * hintereinander (de/en/hr/hu), und Abbrechen bei der vierten verwarf die ersten drei. Hier stehen
 * alle Felder in einem Dialog. Dazu gilt, was für `window.confirm` gilt (siehe ConfirmService):
 * die Knöpfe stehen in der Browsersprache, und der Browser kann die Abfrage still unterdrücken.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-prompt-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    @if (data.title) {
      <h2 mat-dialog-title>{{ data.title }}</h2>
    }
    <form (ngSubmit)="submit()">
      <div mat-dialog-content class="prompt-body">
        @if (data.hint) {
          <p class="prompt-hint">{{ data.hint }}</p>
        }
        @for (f of data.fields; track f.key) {
          <mat-form-field appearance="outline" class="prompt-field">
            <mat-label>{{ f.label }}</mat-label>
            <input matInput [name]="f.key" [(ngModel)]="values[f.key]">
          </mat-form-field>
        }
      </div>
      <div mat-dialog-actions align="end">
        <button mat-button type="button" [mat-dialog-close]="null">{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button color="primary" type="submit">{{ 'common.save' | translate }}</button>
      </div>
    </form>
  `,
  styles: [`
    .prompt-body { display: flex; flex-direction: column; padding-top: 0.5rem; min-width: min(320px, 78vw); }
    .prompt-hint { margin: 0 0 0.75rem; font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .prompt-field { width: 100%; }
  `],
})
export class PromptDialogComponent {
  readonly data = inject<PromptData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<PromptDialogComponent, Record<string, string> | null>>(MatDialogRef);
  /** Arbeitsstand je Feld-Key, mit der Vorbelegung gestartet. */
  readonly values: Record<string, string> =
    Object.fromEntries(this.data.fields.map(f => [f.key, f.value ?? '']));

  submit(): void {
    this.ref.close({ ...this.values });
  }
}

/** Abfrage mehrerer Textfelder in EINEM Dialog: `ask({ fields })` liefert die Werte je Key oder `null` (Abbrechen). */
@Injectable({ providedIn: 'root' })
export class PromptService {
  private dialog = inject(MatDialog);

  /** Werte kommen unbeschnitten zurück — Trimmen und Prüfen bleibt beim Aufrufer. */
  ask(data: PromptData): Observable<Record<string, string> | null> {
    return this.dialog
      .open<PromptDialogComponent, PromptData, Record<string, string> | null>(PromptDialogComponent, { data, maxWidth: '32rem' })
      .afterClosed()
      .pipe(map(result => result ?? null));
  }
}
