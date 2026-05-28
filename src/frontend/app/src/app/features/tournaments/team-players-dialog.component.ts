import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TournamentPlayer } from '../../core/models';

@Component({
  selector: 'app-team-players-dialog',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatIconModule, MatDialogModule],
  template: `
    <h2 class="dialog-title">{{ data.teamName }}</h2>
    <div class="dialog-table-scroll">
      <table mat-table [dataSource]="data.players" class="full-width">
        <ng-container matColumnDef="boardNumber">
          <th mat-header-cell *matHeaderCellDef>Br.</th>
          <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
        </ng-container>
        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef>Title</th>
          <td mat-cell *matCellDef="let p">{{ p.title }}</td>
        </ng-container>
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let p">{{ p.name }}</td>
        </ng-container>
        <ng-container matColumnDef="elo">
          <th mat-header-cell *matHeaderCellDef>Elo</th>
          <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns;"></tr>
      </table>
    </div>
    <div class="dialog-actions">
      <button mat-button mat-dialog-close>Close</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; }
    .dialog-title { margin: 0 0 1rem; font-size: 1.2rem; word-break: break-word; }
    .dialog-table-scroll { overflow-x: auto; max-height: 60vh; }
    .full-width { width: 100%; }
    .dialog-actions { display: flex; justify-content: flex-end; margin-top: 1rem; }
  `]
})
export class TeamPlayersDialogComponent {
  columns = ['boardNumber', 'title', 'name', 'elo'];
  constructor(@Inject(MAT_DIALOG_DATA) public data: { teamName: string; players: TournamentPlayer[] }) {}
}
