import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { TournamentPlayer } from '@rh/core/models';
import { chessTitle } from './tournament-table.util';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-team-players-dialog',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe],
  template: `
    <!-- d8-t-dialog-titel-fett: Titel/Inhalt/Knöpfe aus dem Baukasten (DESIGN 5.6); die Tabelle scrollt im Dialoginhalt -->
    <h2 mat-dialog-title class="team-title">{{ data.teamName }}</h2>
    <mat-dialog-content class="rh-scroll-hint">
      <table mat-table [dataSource]="data.players" class="full-width">
        <ng-container matColumnDef="boardNumber">
          <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.players.boardShort' | translate }}</th>
          <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
        </ng-container>
        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.players.title' | translate }}</th>
          <td mat-cell *matCellDef="let p">@if (chessTitle(p.title); as t) { {{ t }} }</td>
        </ng-container>
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.players.name' | translate }}</th>
          <td mat-cell *matCellDef="let p">{{ p.name }}</td>
        </ng-container>
        <ng-container matColumnDef="elo">
          <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.players.elo' | translate }}</th>
          <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns;"></tr>
      </table>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .team-title { overflow-wrap: anywhere; }
    /* d8-t-team-dialog-tabelle-kasten-dunkel: die Tabelle liegt ohne eigenen Grund auf der Dialogfläche — mat-table behielt
       sonst „surface“ (Seitenfarbe) und stand im Dunkeln als schwarzer Kasten auf dem helleren Dialog. */
    .full-width { width: 100%; --mat-table-background-color: transparent; }
  `]
})
export class TeamPlayersDialogComponent {
  readonly chessTitle = chessTitle;
  /** Titel nur echte (GM, IM …): Rohcodes wie „Z56“ fallen weg, ohne echten Titel fehlt die Spalte (DESIGN 8.2, d7). */
  readonly columns: string[];
  constructor(@Inject(MAT_DIALOG_DATA) public data: { teamName: string; players: TournamentPlayer[] }) {
    const hasTitle = (data.players ?? []).some(p => chessTitle(p.title));
    this.columns = hasTitle ? ['boardNumber', 'title', 'name', 'elo'] : ['boardNumber', 'name', 'elo'];
  }
}
