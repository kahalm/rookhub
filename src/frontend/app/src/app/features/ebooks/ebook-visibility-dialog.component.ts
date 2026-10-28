import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookGroupOption, EbookService } from './ebook.service';

export interface EbookVisibilityDialogData {
  ebookIds: number[];
  groups: EbookGroupOption[];
  /** Vorbelegung (nur bei genau einem Buch sinnvoll). */
  selected: number[];
}

/** „Sichtbarkeit setzen" für ein oder viele Bücher auf einmal: Gruppen ankreuzen, nichts angekreuzt = nur ich. */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-visibility-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.bulk.setVisibility' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ (data.ebookIds.length === 1 ? 'ebooks.bulk.forCountOne' : 'ebooks.bulk.forCount') | translate: { n: data.ebookIds.length } }}</p>
      @if (data.groups.length === 0) {
        <p class="hint">{{ 'ebooks.visibility.noGroups' | translate }}</p>
      } @else {
        <div class="groups">
          @for (g of data.groups; track g.id) {
            <mat-checkbox [checked]="selected.has(g.id)" (change)="toggle(g.id, $event.checked)">
              {{ g.isEveryone ? ('ebooks.visibility.everyone' | translate) : g.name }}
            </mat-checkbox>
          }
        </div>
      }
      <p class="hint">{{ (selected.size === 0 ? 'ebooks.visibility.onlyMeHint' : 'ebooks.visibility.groupsHint') | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="busy()">
        <mat-icon>visibility</mat-icon>{{ 'common.save' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .groups { display: flex; flex-direction: column; }
    .hint { margin: 0 0 8px; font-size: .85rem; opacity: .7; }
  `],
})
export class EbookVisibilityDialogComponent {
  readonly data = inject<EbookVisibilityDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<EbookVisibilityDialogComponent, number | null>);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  selected = new Set<number>(this.data.selected);
  readonly busy = signal(false);

  toggle(groupId: number, checked: boolean): void {
    if (checked) this.selected.add(groupId); else this.selected.delete(groupId);
  }

  save(): void {
    this.busy.set(true);
    this.ebooks.setGroups(this.data.ebookIds, [...this.selected]).subscribe({
      next: r => this.ref.close(r.updated),
      error: err => {
        this.busy.set(false);
        this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed'));
      },
    });
  }
}
