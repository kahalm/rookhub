import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

interface ReprocessStatus {
  currentVersion: number; total: number; stale: number;
  reprocessableLocally: number; refetchable: number; needsReimport: number;
}
/**
 * Wiederverwendbares Banner „N … können aktualisiert werden", wenn die Import-/Aufbereitungs-Pipeline
 * weiterentwickelt wurde (z. B. um nachträglich Zug-Kommentare zu extrahieren). Zeigt sich nur, wenn
 * der Server veraltete Datensätze meldet. Ein Klick stößt das Neu-Aufbereiten an; nach Abschluss wird
 * `done` emittiert, damit die Eltern-Liste neu laden kann. Arbeitet generisch gegen
 * `/api/{section}/reprocess[/status]` (section = "courses" | "repertoires").
 */
@Component({
  selector: 'app-reprocess-banner',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslateModule],
  template: `
    @if (status && (actionableCount > 0 || showReimportNote)) {
      <div class="reprocess-banner">
        <mat-icon class="rb-icon">auto_fix_high</mat-icon>
        <span class="rb-text">
          @if (actionableCount > 0) {
            {{ ('reprocess.available.' + section) | translate: { count: actionableCount } }}
          }
          @if (showReimportNote) {
            <span class="rb-note">{{ ('reprocess.needsReimport.' + section) | translate: { count: status.needsReimport } }}</span>
          }
        </span>
        @if (allCount > 0) {
          <!-- EIN Aktualisieren-Knopf: bereitet lokal aus der gespeicherten Quelle auf und holt
               Chessable-Kurse per Re-Fetch (gecachte laufen dabei netzfrei über die Fast-Lane). -->
          <button mat-stroked-button (click)="run()" [disabled]="working"
                  [matTooltip]="'reprocess.updateTip' | translate">
            @if (working) {
              <mat-spinner diameter="16" class="rb-spin"></mat-spinner> {{ 'reprocess.working' | translate }}
            } @else {
              <mat-icon>refresh</mat-icon> {{ 'reprocess.update' | translate: { count: allCount } }}
            }
          </button>
        }
        @if (showReimportNote) {
          <!-- Hinweis bestätigen/wegklicken: bleibt versteckt, bis neue manuelle Kurse hinzukommen. -->
          <button mat-icon-button class="rb-dismiss" (click)="dismissReimport()"
                  [attr.aria-label]="'reprocess.dismiss' | translate" [matTooltip]="'reprocess.dismiss' | translate">
            <mat-icon>close</mat-icon>
          </button>
        }
      </div>
    }
  `,
  styles: [`
    .reprocess-banner {
      display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
      margin: 0 0 1.25rem; padding: 10px 14px; border-radius: 8px; font-size: 0.9rem;
      background: color-mix(in srgb, #ffb300 16%, transparent);
      border: 1px solid color-mix(in srgb, #ffb300 35%, transparent);
    }
    .rb-icon { flex: 0 0 auto; color: #ef6c00; }
    .rb-text { flex: 1 1 220px; }
    .rb-note { display: block; margin-top: 4px; opacity: 0.85; font-size: 0.85em; }
    .rb-spin { display: inline-block; vertical-align: middle; margin-right: 6px; }
    .rb-dismiss { flex: 0 0 auto; margin-left: auto; color: #ef6c00; }
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
  /** Zuletzt bestätigter Re-Import-Hinweis (Anzahl bei der Bestätigung) — aus localStorage. */
  private reimportDismissedAt = 0;

  /** Aktualisierbare Datensätze: lokal aufbereitbare + per Chessable-Re-Fetch holbare. */
  get allCount(): number {
    return this.status ? this.status.reprocessableLocally + this.status.refetchable : 0;
  }
  /** Irgendetwas aktualisierbar? (steuert das Banner zusammen mit dem Re-Import-Hinweis). */
  get actionableCount(): number { return this.allCount; }

  /** Re-Import-Hinweis nur zeigen, solange es mehr manuelle Kurse sind als zuletzt bestätigt
   *  (einmal weggeklickt bleibt er weg, bis NEUE hinzukommen). */
  get showReimportNote(): boolean {
    return !!this.status && this.status.needsReimport > this.reimportDismissedAt;
  }

  private get dismissKey(): string { return `rookhub_reprocess_reimport_dismissed_${this.section}`; }

  constructor(private http: HttpClient, private snackbar: SnackbarService, private translate: TranslateService) {}

  ngOnInit(): void {
    const raw = Number(localStorage.getItem(this.dismissKey));
    this.reimportDismissedAt = Number.isFinite(raw) && raw > 0 ? raw : 0;
    this.refresh();
  }

  /** „Verstanden": den Re-Import-Hinweis für die aktuelle Anzahl ausblenden (persistiert). */
  dismissReimport(): void {
    if (!this.status) return;
    this.reimportDismissedAt = this.status.needsReimport;
    try { localStorage.setItem(this.dismissKey, String(this.reimportDismissedAt)); } catch { /* ignore */ }
  }

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
