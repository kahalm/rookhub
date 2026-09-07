import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentDirectoryService } from './tournament-directory.service';
import {
  DIRECTORY_AGE_GROUPS, DIRECTORY_KINDS, DirectoryEntry, TournamentSpeed,
} from './tournament-directory.model';

export interface ReportEntryDialogData {
  entry: DirectoryEntry;
}

/**
 * „Falsches Event melden."
 *
 * <p>Der Zweck ist NICHT, dass der Nutzer die Datenbank pflegt — die Vorschlagsfelder sind
 * Bequemlichkeit. Der Zweck sind die zwei Fragen am Ende: Alter und Publikum eines Turniers
 * stehen nur im Namen, und diese Namen sind regional („Schachrallye" ist in Tirol immer
 * Nachwuchs). Solche Kennungen kann von aussen niemand erraten; wer sie einmal nennt, verbessert
 * die Einordnung aller kuenftigen Ausgaben derselben Reihe — nicht nur dieses einen Eintrags.</p>
 *
 * <p>Alles ist freiwillig, auch der Text. Ein Absenden ohne ein Wort ist eine gueltige Meldung
 * („hier stimmt etwas nicht"), und die Huerde soll niedrig sein: wer erst Formulararbeit leisten
 * muss, meldet nichts.</p>
 */
@Component({
  selector: 'app-report-entry-dialog',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'tournamentDirectory.report.title' | translate }}</h2>

    <mat-dialog-content>
      <p class="lead">{{ 'tournamentDirectory.report.lead' | translate }}</p>

      <p class="current">
        <strong>{{ 'tournamentDirectory.report.current' | translate }}:</strong>
        {{ summary }}
      </p>

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'tournamentDirectory.report.message' | translate }}</mat-label>
        <textarea matInput rows="3" [(ngModel)]="message"></textarea>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-icon matPrefix>place</mat-icon>
        <mat-label>{{ 'tournamentDirectory.report.location' | translate }}</mat-label>
        <input matInput [(ngModel)]="location" />
      </mat-form-field>

      <div class="row">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'tournamentDirectory.report.kind' | translate }}</mat-label>
          <mat-select [(ngModel)]="kind">
            <mat-option [value]="null">{{ 'tournamentDirectory.report.unset' | translate }}</mat-option>
            @for (option of kinds; track option) {
              <mat-option [value]="option">{{ 'tournamentDirectory.kind.' + option | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'tournamentDirectory.report.gender' | translate }}</mat-label>
          <mat-select [(ngModel)]="gender">
            <mat-option [value]="null">{{ 'tournamentDirectory.report.unset' | translate }}</mat-option>
            @for (option of genders; track option) {
              <mat-option [value]="option">{{ 'tournamentDirectory.gender.' + option | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'tournamentDirectory.report.speed' | translate }}</mat-label>
          <mat-select [(ngModel)]="speed">
            <mat-option [value]="null">{{ 'tournamentDirectory.report.unset' | translate }}</mat-option>
            @for (option of speeds; track option) {
              <mat-option [value]="option">{{ 'tournamentDirectory.speed.' + option | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'tournamentDirectory.report.ageGroups' | translate }}</mat-label>
        <mat-select [(ngModel)]="selectedAgeGroups" multiple>
          @for (group of ageGroups; track group) {
            <mat-option [value]="group">{{ 'tournamentDirectory.age.' + group | translate }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-checkbox [(ngModel)]="isLeague">
        {{ 'tournamentDirectory.report.isLeague' | translate }}
      </mat-checkbox>

      <h3 class="learn">{{ 'tournamentDirectory.report.learnTitle' | translate }}</h3>

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'tournamentDirectory.report.namePattern' | translate }}</mat-label>
        <input matInput [(ngModel)]="namePattern" />
        <mat-hint>{{ 'tournamentDirectory.report.namePatternHint' | translate }}</mat-hint>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full spaced">
        <mat-icon matPrefix>link</mat-icon>
        <mat-label>{{ 'tournamentDirectory.report.sourceLink' | translate }}</mat-label>
        <input matInput type="url" inputmode="url" placeholder="https://" [(ngModel)]="sourceLink" />
      </mat-form-field>

      @if (failed()) {
        <p class="error">{{ 'tournamentDirectory.report.sendError' | translate }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button (click)="send()" [disabled]="sending()">
        {{ 'common.send' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .lead { margin: 0 0 0.75rem; font-size: 0.9rem; }

    .current {
      margin: 0 0 1rem;
      padding: 0.5rem 0.75rem;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-low);
      font-size: 0.82rem;
    }

    .full { width: 100%; }
    /* Der Hinweistext unter dem Namensmuster ist mehrzeilig — ohne Abstand laeuft er ins
       naechste Feld. */
    .spaced { margin-top: 1.75rem; }
    .row { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .row mat-form-field { flex: 1 1 150px; }
    .learn { margin: 1.5rem 0 0.5rem; font-size: 0.95rem; }
    .error { color: var(--mat-sys-error); font-size: 0.85rem; margin: 0.5rem 0 0; }
    mat-dialog-content { display: flex; flex-direction: column; }
  `],
})
export class ReportEntryDialogComponent {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  readonly kinds = DIRECTORY_KINDS;
  readonly genders = ['Open', 'Female', 'Male'];
  readonly speeds: TournamentSpeed[] = ['Standard', 'Rapid', 'Blitz'];
  readonly ageGroups = DIRECTORY_AGE_GROUPS;

  message = '';
  location = '';
  kind: string | null = null;
  gender: string | null = null;
  speed: string | null = null;
  selectedAgeGroups: string[] = [];
  isLeague = false;
  namePattern = '';
  sourceLink = '';

  readonly sending = signal(false);
  readonly failed = signal(false);

  constructor(
    private dialogRef: MatDialogRef<ReportEntryDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: ReportEntryDialogData,
  ) {
    // Der Haken steht auf dem IST-Stand. Sonst hiesse ein nicht angefasster Haken „das ist keine
    // Liga" und die Meldung widersprueche stillschweigend dem, was drinsteht.
    this.isLeague = data.entry.isLeague;
  }

  /**
   * Was das Verzeichnis heute behauptet — in einer Zeile. Ohne sie meldet jemand einen Ort, der
   * schon so gespeichert ist, weil auf der Seite nur der Ortstext von chess-results steht und
   * nicht, worauf er verortet wurde.
   */
  get summary(): string {
    const entry = this.data.entry;
    const parts = [
      entry.geoPlaceName ?? entry.location ?? '—',
      this.translate.instant('tournamentDirectory.kind.' + entry.kind),
      this.translate.instant('tournamentDirectory.speed.' + entry.speed),
    ];
    if (entry.isLeague) parts.push(this.translate.instant('tournamentDirectory.hideLeagues'));
    for (const group of entry.ageGroups) {
      parts.push(this.translate.instant('tournamentDirectory.age.' + group));
    }
    if (entry.gender !== 'Open') {
      parts.push(this.translate.instant('tournamentDirectory.gender.' + entry.gender));
    }
    return parts.join(' · ');
  }

  send(): void {
    this.sending.set(true);
    this.failed.set(false);

    this.directory.report(this.data.entry.chessResultsId, {
      message: this.message.trim() || null,
      location: this.location.trim() || null,
      kind: this.kind,
      // Kommagetrennt: der Server nimmt hier bewusst FREITEXT — ein Mensch soll auch
      // „U10 bis U14" schreiben koennen, ohne die interne Wertetabelle zu kennen.
      ageGroups: this.selectedAgeGroups.length > 0 ? this.selectedAgeGroups.join(', ') : null,
      gender: this.gender,
      speed: this.speed,
      // Nur mitschicken, wenn er vom gespeicherten Stand ABWEICHT — sonst stuende in jeder
      // Meldung ein „Vorschlag", der nichts vorschlaegt.
      isLeague: this.isLeague === this.data.entry.isLeague ? null : this.isLeague,
      namePattern: this.namePattern.trim() || null,
      sourceLink: this.sourceLink.trim() || null,
    }).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('tournamentDirectory.report.thanks'));
        this.dialogRef.close(true);
      },
      error: () => {
        this.sending.set(false);
        this.failed.set(true);
      },
    });
  }
}
