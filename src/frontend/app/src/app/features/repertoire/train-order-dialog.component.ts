import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, map } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import {
  DEFAULT_LINE_ORDER, LINE_ORDERS, LineOrder, effectiveLineOrder, lineOrderHintKey, lineOrderLabelKey, parseLineOrder,
  readLegacyFrequencyOrder,
} from './repertoire-line-order';
import { updateRepertoireOfflineSettings } from './repertoire-offline.util';

export interface TrainOrderDialogData {
  repertoireId: number;
  /** Name des Repertoires — kleine Zeile unter dem Titel (DESIGN 5.6: Titel kurz, lange Namen darunter). */
  repertoireName?: string | null;
  /** Die geltende Reihenfolge, wenn der Aufrufer sie schon kennt (Trainer); sonst holt der Dialog sie vom Server. */
  current?: LineOrder;
}

/** Den Dialog öffnen — EIN Baustein für Trainer, Linienleiste der Detailseite und ⋮ der Repertoire-Karte.
 *  Ergebnis: die gespeicherte Reihenfolge, `undefined` nach Abbrechen. */
export function openTrainOrderDialog(dialog: MatDialog, data: TrainOrderDialogData): Observable<LineOrder | undefined> {
  return dialog.open<TrainOrderDialogComponent, TrainOrderDialogData, unknown>(TrainOrderDialogComponent, {
    data, width: '440px', maxWidth: '94vw', autoFocus: 'dialog',
  }).afterClosed().pipe(
    // „Abbrechen" (mat-dialog-close) schließt mit '', Esc/Klick daneben mit undefined — beides heißt: nichts gewählt.
    map(result => parseLineOrder(result) ?? undefined),
  );
}

/**
 * „Reihenfolge im Training" (0.996.0): in welcher Reihenfolge der Trainer die Linien DIESES Repertoires bringt — eine
 * Wahl aus {@link LINE_ORDERS}, je Nutzer und Repertoire am Server gespeichert (gilt auf allen Geräten, auch für ein
 * geteiltes fremdes Repertoire). Gespeichert wird erst mit „Speichern"; scheitert das, bleibt der Dialog offen und eine
 * Snackbar sagt es — nie ein vorgetäuschtes „gespeichert".
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-train-order-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatRadioModule, MatProgressSpinnerModule, TranslatePipe, LoadErrorComponent],
  template: `
    <h2 mat-dialog-title>{{ 'repertoire.trainOrder.title' | translate }}</h2>
    <mat-dialog-content>
      @if (data.repertoireName) {
        <p class="subtitle">{{ data.repertoireName }}</p>
      }
      @if (loading()) {
        <div class="load"><mat-spinner diameter="32"></mat-spinner></div>
      } @else if (loadError() !== null) {
        <!-- Ohne den Stand vom Server keine Auswahl: sonst sähe die Vorgabe wie die gespeicherte Wahl aus. -->
        <app-load-error [error]="loadError()" (retry)="load()" />
      } @else {
        <mat-radio-group class="orders" [value]="selected()" (change)="selected.set($event.value)"
                         [attr.aria-label]="'repertoire.trainOrder.title' | translate">
          @for (o of orders; track o) {
            <mat-radio-button [value]="o" [attr.data-order]="o">
              <span class="name">{{ labelKey(o) | translate }}</span>
              <span class="hint">{{ hintKey(o) | translate }}</span>
            </mat-radio-button>
          }
        </mat-radio-group>
        <p class="hint scope">{{ 'repertoire.trainOrder.scope' | translate }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      @if (!loading() && loadError() === null) {
        <button mat-flat-button color="primary" class="save" (click)="save()" [disabled]="saving()">
          {{ 'common.save' | translate }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .load { display: flex; justify-content: center; padding: 24px; }
    .subtitle { margin: 0 0 8px; font-size: .9rem; color: var(--mat-sys-on-surface-variant);
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .orders { display: flex; flex-direction: column; gap: 4px; }
    /* Name und Erklärsatz untereinander; die Zeile ist als Ganzes das Tippziel (mind. 44 px). */
    .orders mat-radio-button { min-height: 44px; }
    .name { display: block; }
    /* Im Dialog ist currentColor schon on-surface-variant (DESIGN 5.6) — der Hinweis nimmt den Ton direkt. */
    .hint { display: block; color: var(--mat-sys-on-surface-variant); font-size: .85rem; line-height: 1.3; white-space: normal; }
    .scope { margin: 12px 0 0; }
  `],
})
export class TrainOrderDialogComponent implements OnInit {
  readonly data = inject<TrainOrderDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TrainOrderDialogComponent, LineOrder>>(MatDialogRef);
  private readonly training = inject(RepertoireTrainingService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  readonly orders = LINE_ORDERS;
  readonly loading = signal(true);
  readonly loadError = signal<unknown>(null);
  readonly saving = signal(false);
  readonly selected = signal<LineOrder>(DEFAULT_LINE_ORDER);

  labelKey(o: LineOrder): string { return lineOrderLabelKey(o); }
  hintKey(o: LineOrder): string { return lineOrderHintKey(o); }

  ngOnInit(): void {
    if (this.data.current) {
      this.selected.set(this.data.current);
      this.loading.set(false);
    } else {
      this.load();
    }
  }

  /** Den gespeicherten Stand holen (Liste/Detailseite kennen ihn nicht) — vorgewählt ist, was im Trainer gilt. */
  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.training.getConfig(this.data.repertoireId).subscribe({
      next: cfg => {
        this.selected.set(effectiveLineOrder(cfg?.lineOrder, readLegacyFrequencyOrder()));
        this.loading.set(false);
      },
      error: err => { this.loadError.set(err ?? true); this.loading.set(false); },
    });
  }

  save(): void {
    if (this.saving()) return;
    const order = this.selected();
    this.saving.set(true);
    this.training.setLineOrder(this.data.repertoireId, order).subscribe({
      next: () => {
        updateRepertoireOfflineSettings(this.data.repertoireId, { lineOrder: order });   // No-op ohne Offline-Kopie
        this.snackbar.success(this.translate.instant('repertoire.trainOrder.saved'));
        this.ref.close(order);
      },
      error: () => {
        this.saving.set(false);
        this.snackbar.warn(this.translate.instant('repertoire.trainOrder.saveFailed'));
      },
    });
  }
}
