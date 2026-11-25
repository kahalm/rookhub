import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TournamentPlayer } from '@rh/core/models';
import { chessTitle } from './tournament-table.util';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-team-players-dialog',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatIconModule, MatDialogModule, RouterLink, TranslatePipe],
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
          <!-- Name klickbar (wenn der Aufrufer es anbietet): die Begegnungen des Spielers öffnen sich über diesem Fenster. -->
          <td mat-cell *matCellDef="let p">
            @if (data.onPlayer) {
              <button type="button" class="player-link" (click)="data.onPlayer(p)">{{ p.name }}</button>
            } @else {
              {{ p.name }}
            }
          </td>
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
      <!-- Live-Bretter (1.014.0): mit verknüpfter Übertragung die Bretter dieser Mannschaft, sonst ein leiser Hinweis. -->
      @if (data.live?.linked) {
        <a mat-stroked-button class="live-team" [routerLink]="data.live!.link" [queryParams]="data.live!.query" mat-dialog-close>
          <mat-icon>live_tv</mat-icon> {{ 'tournaments.live.title' | translate }}
        </a>
      } @else if (data.live) {
        <a mat-button class="live-hint" [routerLink]="data.live.link" mat-dialog-close>{{ 'tournaments.live.noBroadcastShort' | translate }}</a>
      }
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .team-title { overflow-wrap: anywhere; }
    /* d8-t-team-dialog-tabelle-kasten-dunkel: die Tabelle liegt ohne eigenen Grund auf der Dialogfläche — mat-table behielt
       sonst „surface“ (Seitenfarbe) und stand im Dunkeln als schwarzer Kasten auf dem helleren Dialog. */
    .full-width { width: 100%; --mat-table-background-color: transparent; }
    .player-link {
      background: none; border: 0; padding: 0; font: inherit; text-align: left; cursor: pointer;
      color: var(--rh-accent); text-decoration: underline;
    }
    .player-link:focus-visible { outline: 2px solid var(--rh-accent); outline-offset: 2px; }
    .live-hint { color: var(--mat-sys-on-surface-variant); }
  `]
})
export class TeamPlayersDialogComponent {
  readonly chessTitle = chessTitle;
  /** Titel nur echte (GM, IM …): Rohcodes wie „Z56“ fallen weg, ohne echten Titel fehlt die Spalte (DESIGN 8.2, d7). */
  readonly columns: string[];
  constructor(@Inject(MAT_DIALOG_DATA) public data: {
    teamName: string; players: TournamentPlayer[]; onPlayer?: (player: TournamentPlayer) => void;
    /** Live-Bretter: verknüpft? Weg + Abfrage (Mannschaft, ggf. Runde). Fehlt = Stand noch unbekannt, nichts zeigen. */
    live?: { linked: boolean; link: (string | number)[]; query: Record<string, string | number> };
  }) {
    const hasTitle = (data.players ?? []).some(p => chessTitle(p.title));
    this.columns = hasTitle ? ['boardNumber', 'title', 'name', 'elo'] : ['boardNumber', 'name', 'elo'];
  }
}
