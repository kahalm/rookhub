import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

export type EbookTreeScope = 'all' | 'here';

export interface EbookTreeSaveDialogData {
  name: string;
  /** „Ab dieser Stellung“ anbieten (nicht an der Grundstellung — dort ist es dasselbe wie „ganzer Baum“). */
  canHere: boolean;
  /** Vorbelegte Farbe je Umfang (Seite, die in der Mehrheit der Linien den letzten Zug macht). */
  colors: Record<EbookTreeScope, 'w' | 'b'>;
  /** Züge je Umfang (zur Einordnung neben der Wahl). */
  counts: Record<EbookTreeScope, number>;
}

export interface EbookTreeSaveDialogResult { name: string; color: 'w' | 'b'; scope: EbookTreeScope; }

/**
 * „Als Repertoire speichern“ aus dem Buchbaum (0.881.0): Name, Farbe, Umfang. Die Farbe folgt dem Umfang, bis man sie
 * selbst wählt. Einfaches Formular → 440 px (DESIGN 5.6).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-tree-save-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.tree.save' | translate }}</h2>
    <mat-dialog-content>
      <form class="ets-form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.name' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" required maxlength="200">
        </mat-form-field>
        @if (data.canHere) {
          <div class="ets-label" id="ets-scope">{{ 'ebooks.tree.scope' | translate }}</div>
          <mat-button-toggle-group class="ets-toggle ets-scope" [value]="scope()" (change)="setScope($event.value)" aria-labelledby="ets-scope">
            <mat-button-toggle value="here">{{ 'ebooks.tree.scopeHere' | translate }} · {{ data.counts.here }}</mat-button-toggle>
            <mat-button-toggle value="all">{{ 'ebooks.tree.scopeAll' | translate }} · {{ data.counts.all }}</mat-button-toggle>
          </mat-button-toggle-group>
        }
        <div class="ets-label" id="ets-color">{{ 'repertoire.holes.color' | translate }}</div>
        <mat-button-toggle-group class="ets-toggle ets-color" [value]="color()" (change)="setColor($event.value)" aria-labelledby="ets-color">
          <mat-button-toggle value="w">{{ 'repertoire.holes.white' | translate }}</mat-button-toggle>
          <mat-button-toggle value="b">{{ 'repertoire.holes.black' | translate }}</mat-button-toggle>
        </mat-button-toggle-group>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="ref.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" type="button" class="ets-save" [disabled]="!name.trim()" (click)="submit()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ets-form { display: flex; flex-direction: column; gap: 4px; min-width: min(320px, 78vw); }
    mat-form-field { width: 100%; }
    .ets-label { font-size: .875rem; color: var(--mat-sys-on-surface-variant); margin-top: 4px; }
    .ets-toggle { align-self: flex-start; max-width: 100%; flex-wrap: wrap; }
  `],
})
export class EbookTreeSaveDialogComponent {
  readonly data = inject<EbookTreeSaveDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<EbookTreeSaveDialogComponent, EbookTreeSaveDialogResult>>(MatDialogRef);

  name = this.data.name;
  readonly scope = signal<EbookTreeScope>(this.data.canHere ? 'here' : 'all');
  readonly color = signal<'w' | 'b'>(this.data.colors[this.data.canHere ? 'here' : 'all']);
  private colorChosen = false;

  setScope(s: EbookTreeScope): void {
    this.scope.set(s);
    if (!this.colorChosen) this.color.set(this.data.colors[s]);
  }

  setColor(c: 'w' | 'b'): void {
    this.colorChosen = true;
    this.color.set(c);
  }

  submit(): void {
    const name = this.name.trim();
    if (!name) return;
    this.ref.close({ name, color: this.color(), scope: this.scope() });
  }
}
