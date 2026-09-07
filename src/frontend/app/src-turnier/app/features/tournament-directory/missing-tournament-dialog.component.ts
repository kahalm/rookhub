import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentDirectoryService } from './tournament-directory.service';

/**
 * „Mein Turnier fehlt hier."
 *
 * <p>Das Verzeichnis speist sich aus der chess-results-Turniersuche. Wer dort nicht ausschreibt,
 * kommt hier nicht vor — und genau diese Turniere sind die Luecke, die von innen niemand sehen
 * kann. Der LINK ist deshalb das Pflichtfeld und nicht der Turniername: eine Verbands- oder
 * Vereinsseite laesst sich zusaetzlich auswerten, eine Aufzaehlung einzelner Termine nicht.</p>
 */
@Component({
  selector: 'app-missing-tournament-dialog',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule,
    MatInputModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'tournamentDirectory.missing.title' | translate }}</h2>

    <mat-dialog-content>
      <p class="lead">{{ 'tournamentDirectory.missing.lead' | translate }}</p>

      <mat-form-field appearance="outline" class="full">
        <mat-icon matPrefix>link</mat-icon>
        <mat-label>{{ 'tournamentDirectory.missing.link' | translate }}</mat-label>
        <input matInput type="url" [(ngModel)]="link" inputmode="url"
               placeholder="https://" (ngModelChange)="error.set(null)" />
        <mat-hint>{{ 'tournamentDirectory.missing.linkHint' | translate }}</mat-hint>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'tournamentDirectory.missing.message' | translate }}</mat-label>
        <textarea matInput rows="4" [(ngModel)]="message"></textarea>
      </mat-form-field>

      @if (error(); as key) {
        <p class="error">{{ key | translate }}</p>
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
    .lead { margin: 0 0 1rem; font-size: 0.9rem; }
    .full { width: 100%; }
    .error { color: var(--mat-sys-error); font-size: 0.85rem; margin: 0; }
    mat-dialog-content { display: flex; flex-direction: column; }
  `],
})
export class MissingTournamentDialogComponent {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly dialogRef = inject(MatDialogRef<MissingTournamentDialogComponent>);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  link = '';
  message = '';
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);

  send(): void {
    const link = this.link.trim();
    // Vor dem Absenden pruefen, nicht nur serverseitig: ein 400 als Snackbar laesst den Nutzer
    // raten, WELCHES Feld gemeint war, und der Dialog waere dann schon zu.
    if (!/^https?:\/\/\S+\./i.test(link)) {
      this.error.set('tournamentDirectory.missing.linkRequired');
      return;
    }

    this.sending.set(true);
    this.directory.suggestSource(link, this.message.trim() || null).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('tournamentDirectory.missing.thanks'));
        this.dialogRef.close(true);
      },
      error: () => {
        this.sending.set(false);
        this.error.set('tournamentDirectory.missing.sendError');
      },
    });
  }
}
