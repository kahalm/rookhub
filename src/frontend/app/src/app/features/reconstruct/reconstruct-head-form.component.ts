import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { IsoDateFieldComponent } from '../../shared/date-field/iso-date-field.component';

/** Die Kopfdaten im Formular (Titel, Spieler, Turnier, Datum, Ergebnis, Notiz). */
export interface ReconstructHead {
  title: string; white: string; black: string; event: string; playedOn: string; result: string; note: string;
}

/**
 * Formular der Kopfdaten einer Rekonstruktion (Zerlegung Z25, aus `ReconstructDetailComponent`).
 * Die Felder ändern das übergebene `head`-Objekt der Seite direkt (wie vorher per `ngModel`);
 * gespeichert wird in der Seite.
 */
@Component({
  selector: 'app-reconstruct-head-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, TranslatePipe,
    IsoDateFieldComponent],
  template: `
    <mat-card class="head-form">
      <h2 class="form-head">{{ 'reconstruct.editHead' | translate }}</h2>
      <mat-form-field appearance="outline" class="wide">
        <mat-label>{{ 'reconstruct.newTitle' | translate }}</mat-label>
        <input matInput [(ngModel)]="head().title" maxlength="200">
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'reconstruct.white' | translate }}</mat-label>
        <input matInput [(ngModel)]="head().white" maxlength="120">
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'reconstruct.black' | translate }}</mat-label>
        <input matInput [(ngModel)]="head().black" maxlength="120">
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'reconstruct.event' | translate }}</mat-label>
        <input matInput [(ngModel)]="head().event" maxlength="200">
      </mat-form-field>
      <!-- b1-rk-edit-datum-us-format: Datum in der App-Sprache statt im Browserformat (DESIGN 5.13). -->
      <app-iso-date-field subscriptSizing="fixed" [label]="'reconstruct.playedOn' | translate" [(value)]="head().playedOn" />
      <mat-form-field appearance="outline">
        <mat-label>{{ 'reconstruct.result' | translate }}</mat-label>
        <input matInput [(ngModel)]="head().result" placeholder="1-0" maxlength="12">
      </mat-form-field>
      <mat-form-field appearance="outline" class="wide">
        <mat-label>{{ 'reconstruct.note' | translate }}</mat-label>
        <textarea matInput rows="2" [(ngModel)]="head().note" maxlength="2000"></textarea>
      </mat-form-field>
      <div class="form-actions">
        <button mat-button (click)="cancel.emit()">{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button color="primary" [disabled]="busy()" (click)="save.emit()">
          {{ 'common.save' | translate }}
        </button>
      </div>
    </mat-card>
  `,
  styles: [`
    /* Kein eigener Kasten: die Karte bleibt das Kind der Seite wie vor der Zerlegung. */
    :host { display: contents; }
    /* Die Angaben zur Partie. Feste DREI Spalten statt „auto-fit": mit auto-fit standen auf einem
       breiten Bildschirm Titel, Weiß, Schwarz, Turnier, Datum und Ergebnis in EINER Reihe, jedes Feld
       200 px schmal — der Titel, das mit Abstand längste Feld, war das schmalste. Titel und Notiz
       bekommen deshalb die ganze Breite, die kurzen Felder teilen sich die Reihen darunter. */
    .head-form {
      display: grid;
      grid-template-columns: 1fr;
      gap: 0 0.75rem;
      padding: 1rem;
      margin-bottom: 1rem;

      @media (min-width: 700px) { grid-template-columns: repeat(3, minmax(0, 1fr)); }

      .form-head { grid-column: 1 / -1; margin: 0 0 0.75rem; font-size: 1.05rem; font-weight: 600; }
      .wide { grid-column: 1 / -1; }
      .form-actions { grid-column: 1 / -1; display: flex; gap: 0.5rem; justify-content: flex-end; }
    }
  `],
})
export class ReconstructHeadFormComponent {
  readonly head = input.required<ReconstructHead>();
  readonly busy = input(false);

  readonly save = output<void>();
  readonly cancel = output<void>();
}
