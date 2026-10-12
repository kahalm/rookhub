import { Component, EventEmitter, Input, OnInit, Output, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

interface ReprocessStatus {
  currentVersion: number; total: number; stale: number;
  reprocessableLocally: number; refetchable: number; needsReimport: number;
  /** UI-Sweep 2026-10-11 (r2-rg-rep-reprocess): Zahlen über ALLE Nutzer (Admin-Sicht); ältere Server: fehlt. */
  allUsers?: boolean;
  /** Davon aktualisierbare eigene Datensätze. */
  ownActionable?: number;
}
/**
 * Wiederverwendbares Banner „N … können aktualisiert werden", wenn die Import-/Aufbereitungs-Pipeline
 * weiterentwickelt wurde (z. B. um nachträglich Zug-Kommentare zu extrahieren). Zeigt sich nur, wenn
 * der Server veraltete Datensätze meldet. Ein Klick stößt das Neu-Aufbereiten an; nach Abschluss wird
 * `done` emittiert, damit die Eltern-Liste neu laden kann. Arbeitet generisch gegen
 * `/api/{section}/reprocess[/status]` (section = "courses" | "repertoires").
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-reprocess-banner',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    @if (visible) {
      <div class="reprocess-banner">
        <mat-icon class="rb-icon">auto_fix_high</mat-icon>
        <!-- UI-Sweep 2026-10-11 (d2-rk-courses-zwei-banner): Eltern können einen zweiten Satz hineinreichen
             (z. B. „N brauchen einen neuen Import"), damit über der Liste nur EIN Banner steht (DESIGN 4.6/6). -->
        <span class="rb-text">
          {{ textKey | translate: { count: actionableCount, own: status?.ownActionable ?? 0 } }}
          <ng-content />
        </span>
        @if (allCount > 0) {
          <!-- EIN Aktualisieren-Knopf: bereitet lokal aus der gespeicherten Quelle auf und holt
               Chessable-Kurse per Re-Fetch (gecachte laufen dabei netzfrei über die Fast-Lane). -->
          <button mat-stroked-button (click)="run()" [disabled]="working"
                  [matTooltip]="'reprocess.updateTip' | translate">
            <!-- UI-Sweep 2026-10-11 (d1-rk-update-all-icon-misaligned): Symbol und Text je in eigenem Zweig mit EINER Wurzel —
                 mit beidem in einem Zweig landete das Symbol nicht im Icon-Platz des Knopfs und saß 8 px zu hoch. -->
            @if (working) { <mat-spinner diameter="16" class="rb-spin"></mat-spinner> } @else { <mat-icon>refresh</mat-icon> }
            {{ working ? ('reprocess.working' | translate) : ((status?.allUsers ? 'reprocess.updateAll' : 'reprocess.update') | translate: { count: allCount }) }}
          </button>
        }
      </div>
    }
  `,
  styles: [`
    .reprocess-banner {
      display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
      margin: 0 0 1.25rem; padding: 10px 14px; border-radius: 8px; font-size: 0.9rem;
      background: color-mix(in srgb, var(--rh-warn) 16%, transparent);
      border: 1px solid color-mix(in srgb, var(--rh-warn) 35%, transparent);
    }
    .rb-icon { flex: 0 0 auto; color: var(--rh-warn); }
    .rb-text { flex: 1 1 220px; }
    .rb-spin { display: inline-block; vertical-align: middle; margin-right: 6px; }
  `]
})
export class ReprocessBannerComponent implements OnInit {
  /** API-/i18n-Sektion: "courses" oder "repertoires". */
  @Input({ required: true }) section!: 'courses' | 'repertoires';
  /** Wird nach erfolgreichem Reprocess emittiert (Eltern lädt die Liste neu). */
  @Output() done = new EventEmitter<void>();

  status: ReprocessStatus | null = null;
  /** Läuft gerade eine Aktualisierung? (sperrt den Knopf). */
  working = false;

  /** Aktualisierbare Datensätze: lokal aufbereitbare + per Chessable-Re-Fetch holbare. */
  get allCount(): number {
    return this.status ? this.status.reprocessableLocally + this.status.refetchable : 0;
  }
  /** Irgendetwas aktualisierbar? (steuert das Banner). */
  get actionableCount(): number { return this.allCount; }
  /** Banner sichtbar? Eltern zeigen eigene Hinweise nur dann getrennt, wenn das Banner fehlt. */
  get visible(): boolean { return !!this.status && this.actionableCount > 0; }

  /** UI-Sweep 2026-10-11 (r2-rg-rep-reprocess): Bannertext mit echter Einzahl/Mehrzahl; in der Admin-Sicht
   *  sagt er dazu, dass die Zahl die Einträge ALLER Nutzer umfasst (und wie viele davon eigene sind). */
  get textKey(): string {
    const one = this.actionableCount === 1 ? 'One' : '';
    return `reprocess.available${this.status?.allUsers ? 'All' : ''}${one}.${this.section}`;
  }

  constructor(private http: HttpClient, private snackbar: SnackbarService, private translate: TranslateService) {}

  ngOnInit(): void { this.refresh(); }

  private refresh(): void {
    this.http.get<ReprocessStatus>(`/api/${this.section}/reprocess/status`).subscribe({
      next: s => this.status = s,
      error: () => this.status = null,   // still: kein Banner bei Fehler
    });
  }

  /** Stößt die vollständige Aktualisierung an (lokal aufbereiten + Chessable-Re-Fetch; gecachte laufen
   *  netzfrei über die Fast-Lane). */
  run(): void {
    if (this.working) return;
    this.working = true;
    // Server startet die Aufbereitung im Hintergrund und antwortet sofort (202) — sonst lief der
    // Aufruf bei vielen Chessable-Kursen in den ~60-s-Request-Timeout (500).
    this.http.post(`/api/${this.section}/reprocess`, {}).subscribe({
      next: () => {
        this.working = false;
        this.snackbar.info(this.translate.instant('reprocess.started'));
        // Läuft asynchron: den Status etwas später neu holen (die „veraltet"-Zahl sinkt nach und nach)
        // und die Eltern-Liste aktualisieren.
        setTimeout(() => this.refresh(), 2500);
        this.done.emit();
      },
      error: () => { this.working = false; this.snackbar.info(this.translate.instant('reprocess.failed')); },
    });
  }
}
