import { Component, EventEmitter, OnDestroy, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { ChessableService, ChessableImport } from './chessable.service';
import { chessableQueueLabel, compareImportsByQueue } from './chessable-progress.util';

/** Aktiver Import + EINMAL je Update vorberechnetes Statuslabel (statt je CD-Zyklus). */
type ActiveImport = ChessableImport & { label: string };

/**
 * Schreibgeschützte Warteschlangen-Anzeige der laufenden/pausierten Chessable-Importe — dieselbe
 * Visualisierung wie der Chessable-Tab („hole Kurs… Kapitel 7/36 · 82/1000 Linien · noch ca. 23 Min"),
 * gedacht für die Kursseite. Pollt selbstständig den eigenen Import-Status (`getImports`) und meldet
 * über `importCompleted`, sobald ein Import endet — damit die Kursliste den neuen Kurs nachladen kann.
 * Pausieren/Abbrechen bleibt bewusst dem Chessable-Tab vorbehalten.
 */
@Component({
  selector: 'app-chessable-imports-banner',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslateModule],
  template: `
    @if (active.length > 0) {
      <mat-card class="queue-card">
        <mat-card-content>
          <h3 class="queue-title"><mat-icon>cloud_download</mat-icon> {{ 'chessable.queueTitle' | translate }}</h3>
          @for (imp of active; track imp.bid) {
            <div class="queue-row">
              @if (imp.status === 'running') {
                <mat-progress-spinner mode="indeterminate" diameter="18"></mat-progress-spinner>
              } @else {
                <mat-icon class="paused-icon">pause_circle</mat-icon>
              }
              <span class="queue-name">{{ imp.courseName || imp.bid }}</span>
              <span class="queue-status">{{ imp.label }}</span>
            </div>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: [`
    .queue-card { margin-bottom: 16px; border-left: 4px solid var(--mat-sys-primary, #3f51b5); }
    .queue-title { display: flex; align-items: center; gap: 0.4rem; margin: 0 0 0.5rem; font-size: 1rem; }
    .queue-title mat-icon { font-size: 1.15rem; width: 1.15rem; height: 1.15rem; }
    .queue-row { display: flex; align-items: center; gap: 0.6rem; padding: 0.3rem 0; flex-wrap: wrap; }
    .queue-row .queue-name { font-weight: 500; flex: 1 1 200px; min-width: 0; overflow-wrap: anywhere; }
    .queue-row .queue-status { font-size: 0.82rem; color: var(--mat-sys-on-surface-variant, #777); }
    .queue-row .paused-icon { color: var(--mat-sys-on-surface-variant, #999); }
  `]
})
export class ChessableImportsBannerComponent implements OnInit, OnDestroy {
  /** Feuert, sobald ein Import endet (completed/failed/cancelled) → Kursliste neu laden. */
  @Output() importCompleted = new EventEmitter<void>();

  active: ActiveImport[] = [];
  private pollSub?: Subscription;
  /** Zuletzt gesehene aktive Import-IDs — verschwindet eine, ist ihr Import fertig. */
  private knownIds = new Set<number>();

  constructor(private chessable: ChessableService, private translate: TranslateService) {}

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  private load(): void {
    this.chessable.getImports().subscribe({
      next: list => { this.apply(list); if (this.hasRunning()) this.ensurePolling(); },
      error: () => { /* nicht kritisch */ },
    });
  }

  private poll(): void {
    this.chessable.getImports().subscribe({
      next: list => { this.apply(list); if (!this.hasRunning()) this.stopPolling(); },
      error: () => { /* nächster Tick versucht es erneut */ },
    });
  }

  /** Liste auf aktive Importe filtern + sortieren; bei verschwundenen IDs `importCompleted` feuern. */
  private apply(list: ChessableImport[]): void {
    const activeRaw = list.filter(i => i.status === 'running' || i.status === 'paused');
    this.active = activeRaw
      .sort(compareImportsByQueue)
      .map(i => ({ ...i, label: chessableQueueLabel(i, this.translate) }));

    const newIds = new Set(activeRaw.map(i => i.id));
    let finished = false;
    for (const id of this.knownIds) if (!newIds.has(id)) finished = true;
    this.knownIds = newIds;
    if (finished) this.importCompleted.emit();
  }

  private hasRunning(): boolean {
    return this.active.some(i => i.status === 'running');
  }

  private ensurePolling(): void {
    if (this.pollSub) return;
    this.pollSub = timer(8000, 8000).subscribe(() => this.poll());
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }
}
