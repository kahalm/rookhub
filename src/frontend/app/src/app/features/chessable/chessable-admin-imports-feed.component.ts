import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { ChessableService, ChessableImport, ChessableAdminImport } from './chessable.service';
import { chessableQueueLabel, formatDuration } from './chessable-progress.util';

/**
 * Admin-Live-Feed aller Chessable-Importe (Verlauf + aktive, alle User), alle 12 s aktualisiert.
 * Aus <c>ChessableComponent</c> ausgegliedert: rein lesende Anzeige, unabhängig vom eigenen
 * Import-Flow des Users. Nur einbinden, wenn der Betrachter Admin ist.
 */
@Component({
  selector: 'app-chessable-admin-imports-feed',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, TranslatePipe],
  template: `
    @if (imports !== null) {
      <mat-card class="admin-imports-card">
        <mat-card-header>
          <mat-icon mat-card-avatar>admin_panel_settings</mat-icon>
          <mat-card-title>{{ 'chessable.adminImportsTitle' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'chessable.adminImportsSubtitle' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (imports.length === 0) {
            <p class="empty">{{ 'chessable.adminNoImports' | translate }}</p>
          } @else {
            <div class="admin-list">
              @for (imp of imports; track imp.id) {
                <div class="admin-row" [class.active]="imp.status === 'running' || imp.status === 'paused'">
                  <span class="admin-user"><mat-icon>person</mat-icon> {{ imp.username }}</span>
                  <span class="admin-name">{{ imp.courseName || imp.bid }}</span>
                  <span class="admin-target">{{ ('chessable.target_' + imp.target) | translate }}</span>
                  <span class="admin-status" [attr.data-status]="imp.status">{{ imp.statusLabel }}</span>
                  @if (imp.durationLabel; as dur) { <span class="admin-duration">{{ dur }}</span> }
                  <span class="admin-date">{{ imp.createdAt | date:'short' }}</span>
                </div>
              }
            </div>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: [`
    .empty { color: var(--mat-sys-on-surface-variant, #888); }
    .admin-imports-card { margin-top: 1rem; border-left: 4px solid var(--mat-sys-tertiary, #7b1fa2); }
    .admin-list { display: flex; flex-direction: column; }
    .admin-row { display: grid; grid-template-columns: minmax(90px, 1fr) minmax(120px, 2fr) auto auto auto;
      align-items: center; gap: 0.5rem 0.75rem; padding: 0.45rem 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant, #e3e3e3); font-size: 0.88rem; }
    .admin-row:last-child { border-bottom: none; }
    .admin-row.active { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 7%, transparent); }
    .admin-row .admin-user { display: inline-flex; align-items: center; gap: 0.2rem; font-weight: 500; overflow-wrap: anywhere; }
    .admin-row .admin-user mat-icon { font-size: 1.05rem; width: 1.05rem; height: 1.05rem; color: var(--mat-sys-on-surface-variant, #888); }
    .admin-row .admin-name { overflow-wrap: anywhere; }
    .admin-row .admin-target { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant, #777); }
    .admin-row .admin-status { font-size: 0.8rem; }
    .admin-row .admin-status[data-status="completed"] { color: #2e7d32; }
    .admin-row .admin-status[data-status="failed"] { color: #c62828; }
    .admin-row .admin-status[data-status="cancelled"] { color: var(--mat-sys-on-surface-variant, #888); }
    .admin-row .admin-duration { font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    .admin-row .admin-date { font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    @media (max-width: 600px) {
      .admin-row { grid-template-columns: 1fr auto; }
      .admin-row .admin-date { grid-column: 2; }
    }
  `]
})
export class ChessableAdminImportsFeedComponent implements OnInit, OnDestroy {
  /** null = (noch) nicht geladen. */
  imports: (ChessableAdminImport & { statusLabel: string; durationLabel: string })[] | null = null;
  private pollSub?: Subscription;

  constructor(private chessable: ChessableService, private translate: TranslateService) {}

  ngOnInit(): void {
    this.load();
    this.pollSub = timer(5000, 12000).subscribe(() => this.load());
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  private load(): void {
    this.chessable.getAllImportsAdmin().subscribe({
      // Labels EINMAL je Poll berechnen + cachen (statt je CD-Zyklus translate.instant pro Zeile).
      next: list => this.imports = list.map(imp => ({
        ...imp,
        statusLabel: this.adminStatusLabel(imp),
        durationLabel: this.importDurationLabel(imp),
      })),
      error: () => { /* nicht kritisch */ }
    });
  }

  /** Zeilen-Status: aktive nutzen die Queue-/Phasen-Anzeige, erledigte den Endstatus. */
  private adminStatusLabel(imp: ChessableAdminImport): string {
    if (imp.status === 'running' || imp.status === 'paused') return chessableQueueLabel(imp, this.translate);
    return this.translate.instant('chessable.adminStatus_' + imp.status);
  }

  private importDurationLabel(imp: ChessableImport): string {
    if (!imp.startedAt || !imp.completedAt) return '';
    const queue = formatDuration(Date.parse(imp.startedAt) - Date.parse(imp.createdAt));
    const fetch = formatDuration(Date.parse(imp.completedAt) - Date.parse(imp.startedAt));
    return this.translate.instant('chessable.importDuration', { queue, fetch });
  }
}
