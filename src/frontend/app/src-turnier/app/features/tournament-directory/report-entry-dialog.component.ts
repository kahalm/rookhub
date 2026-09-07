import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentDirectoryService } from './tournament-directory.service';
import { DirectoryEntry } from './tournament-directory.model';

export interface ReportEntryDialogData {
  entry: DirectoryEntry;
}

/**
 * „Falsches Event melden."
 *
 * <p>Der Zweck ist NICHT, dass der Nutzer die Datenbank pflegt. Strukturierte Vorschlagsfelder
 * (Ort, Art, Klasse, Bedenkzeit, Liga) standen hier einmal und sind wieder weg: sie verlangten
 * genau die Wertetabelle, die der Melder nicht kennen muss, und machten aus einer Rueckmeldung
 * ein Formular. Ein Satz Freitext sagt dasselbe besser. Der eigentliche Zweck sind die zwei
 * Fragen am Ende: Alter und Publikum eines Turniers
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
    FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule,
    MatInputModule, TranslatePipe,
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
        <textarea matInput rows="4" [(ngModel)]="message"></textarea>
      </mat-form-field>

      <h3 class="learn">{{ 'tournamentDirectory.report.learnTitle' | translate }}</h3>

      <p class="learn-hint">{{ 'tournamentDirectory.report.namePatternHint' | translate }}</p>

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'tournamentDirectory.report.namePattern' | translate }}</mat-label>
        <input matInput [(ngModel)]="namePattern" />
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
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
    /* Der Dialog ist eine SPALTE mit gleichmaessigen Abstaenden. Die Felder tragen ihren
       Abstand nicht selbst: ein mat-form-field mit mehrzeiligem mat-hint ist hoeher als seine
       Box, und ohne einen Abstand am Container schiebt der Hinweistext sich unter das
       naechste Feld — genau das
       war am unteren Teil dieses Dialogs zu sehen. */
    mat-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 1.25rem;
      padding-top: 0.5rem;
    }

    .lead { margin: 0; font-size: 0.9rem; }

    .current {
      margin: 0;
      padding: 0.5rem 0.75rem;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-low);
      font-size: 0.82rem;
    }

    .full { width: 100%; }

    /* Die zwei Lern-Fragen sind ein eigener Block — sie fragen nicht nach DIESEM Turnier,
       sondern nach der Regel dahinter. Die Trennlinie sagt das. */
    .learn {
      margin: 0.5rem 0 0;
      padding-top: 1rem;
      border-top: 1px solid var(--mat-sys-outline-variant);
      font-size: 0.95rem;
    }

    .learn-hint { margin: 0; font-size: 0.82rem; color: color-mix(in srgb, currentColor 65%, transparent); }

    .error { color: var(--mat-sys-error); font-size: 0.85rem; margin: 0; }
  `],
})
export class ReportEntryDialogComponent {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  message = '';
  namePattern = '';
  sourceLink = '';

  readonly sending = signal(false);
  readonly failed = signal(false);

  constructor(
    private dialogRef: MatDialogRef<ReportEntryDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: ReportEntryDialogData,
  ) {}

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

    this.directory.report(this.data.entry.id, {
      message: this.message.trim() || null,
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
