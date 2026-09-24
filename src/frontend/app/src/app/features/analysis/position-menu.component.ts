import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AnalysisJobDialogComponent, AnalysisJobDialogData } from './analysis-job-dialog.component';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_LINES_KEY } from './analysis-settings';
import { ExternalEngineService } from './external-engine.service';
import { chessableFenSearchUrl, positionShareUrl } from './position-links.util';

/** Was an externen Engines da ist — entscheidet, ob „Im Hintergrund analysieren" angeboten wird. */
export interface PositionMenuEngines {
  /** Mindestens eine externe Engine im Lichess-Konto → Aufträge sind überhaupt möglich. */
  hasEngines: boolean;
  /** Im Profil ist eine Hintergrund-Engine gewählt (sonst erklärt der Dialog, wo man sie wählt). */
  hasBackground: boolean;
}

/**
 * ⋮-Menü für die Stellung auf dem Brett (seit 0.527.0, gewünscht 2026-09-24) — auf dem Analysebrett und auf der
 * Partieseite (`/games/:id`, `/g/:token`) dasselbe: auf Chessable suchen, Stellung teilen, FEN kopieren und die
 * Hintergrund-Analyse samt Auftragsliste (die beiden standen am Analysebrett vorher als eigene Symbole in der
 * Engine-Zeile).
 *
 * Die Engine-Frage kostet einen Abruf beim Lichess-Konto: das Analysebrett kennt die Antwort schon und reicht sie
 * über `engines` herein; ohne sie fragt das Menü selbst — erst beim ersten Öffnen, nicht bei jedem Seitenaufruf.
 */
@Component({
  selector: 'app-position-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDividerModule, MatIconModule, MatMenuModule, MatTooltipModule, RouterLink, TranslatePipe],
  template: `
    <button mat-icon-button type="button" class="position-menu-btn" [matMenuTriggerFor]="menu" (menuOpened)="ensureEngines()"
            [matTooltip]="'analysis.menu.title' | translate" [attr.aria-label]="'analysis.menu.title' | translate">
      <mat-icon>more_vert</mat-icon>
    </button>
    <mat-menu #menu="matMenu">
      <a mat-menu-item class="pm-chessable" [href]="chessableUrl()" target="_blank" rel="noopener">
        <mat-icon>travel_explore</mat-icon> {{ 'analysis.menu.chessable' | translate }}
      </a>
      <button mat-menu-item type="button" class="pm-share" (click)="share()">
        <mat-icon>share</mat-icon> {{ 'analysis.menu.share' | translate }}
      </button>
      <button mat-menu-item type="button" class="pm-copy" (click)="copyFen()">
        <mat-icon>content_copy</mat-icon> {{ 'analysis.copyFen' | translate }}
      </button>
      @if (auth.isLoggedIn && effectiveEngines()?.hasEngines) {
        <mat-divider />
        <button mat-menu-item type="button" class="pm-background" (click)="queueBackground()">
          <mat-icon>schedule</mat-icon> {{ 'analysis.queueBackground' | translate }}
        </button>
        <a mat-menu-item class="pm-jobs" routerLink="/analysis/jobs">
          <mat-icon>list_alt</mat-icon> {{ 'analysis.openJobs' | translate }}
        </a>
      }
    </mat-menu>
  `,
})
export class PositionMenuComponent {
  readonly auth = inject(AuthService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly externalEngines = inject(ExternalEngineService);
  private readonly dialog = inject(MatDialog);

  /** Die Stellung auf dem Brett (auch die einer eigenen Nebenvariante). */
  readonly fen = input.required<string>();
  /** Wie das Brett gerade steht — der Teilen-Link zeigt die Stellung genauso. */
  readonly orientation = input<'white' | 'black'>('white');
  /** Vom Analysebrett hereingereicht; fehlt es, fragt das Menü beim ersten Öffnen selbst. */
  readonly engines = input<PositionMenuEngines | undefined>(undefined);
  /** Vorbelegung des Auftrags; ohne Angabe die am Analysebrett gemerkten Werte. */
  readonly depth = input<number | undefined>(undefined);
  readonly lines = input<number | undefined>(undefined);

  private readonly loaded = signal<PositionMenuEngines | null>(null);
  private loading = false;

  effectiveEngines(): PositionMenuEngines | null {
    return this.engines() ?? this.loaded();
  }

  chessableUrl(): string {
    return chessableFenSearchUrl(this.fen());
  }

  /** Die Engine-Frage erst beim Öffnen — und nur einmal. Still bei Fehlern: dann fehlt eben der Hintergrund-Eintrag. */
  ensureEngines(): void {
    if (this.engines() || this.loaded() || this.loading || !this.auth.isLoggedIn) return;
    this.loading = true;
    this.externalEngines.listEngines().subscribe({
      next: r => this.loaded.set({ hasEngines: r.engines.length > 0, hasBackground: (r.backgroundEngineIds ?? []).length > 0 }),
      error: () => { this.loading = false; },
    });
  }

  /** Am Handy das Teilen-Blatt des Geräts, am PC (oder ohne Web-Share) der Link in die Zwischenablage. */
  share(): void {
    const url = positionShareUrl(location.origin, this.fen(), this.orientation());
    const nav = navigator as Navigator & { share?: (data: ShareData) => Promise<void> };
    if (typeof nav.share === 'function' && this.coarsePointer()) {
      nav.share({ url, title: this.translate.instant('analysis.menu.shareTitle') }).catch((e: unknown) => {
        // Abbrechen im Teilen-Blatt ist keine Panne; alles andere fällt auf die Zwischenablage zurück.
        if ((e as { name?: string })?.name !== 'AbortError') this.copyLink(url);
      });
      return;
    }
    this.copyLink(url);
  }

  copyFen(): void {
    const fen = this.fen();
    this.writeClipboard(fen, 'analysis.menu.fenCopied');
  }

  queueBackground(): void {
    const e = this.effectiveEngines();
    const data: AnalysisJobDialogData = {
      fen: this.fen(),
      depth: this.depth() ?? this.stored(ANALYSIS_DEPTH_KEY, 6, 60, 22),
      lines: this.lines() ?? this.stored(ANALYSIS_LINES_KEY, 1, 5, 3),
      hasBackgroundEngine: !!e?.hasBackground,
    };
    this.dialog.open(AnalysisJobDialogComponent, { width: '440px', data }).afterClosed().subscribe(job => {
      if (job) this.snackbar.success(this.translate.instant('analysisJobs.created'));
    });
  }

  private copyLink(url: string): void {
    this.writeClipboard(url, 'analysis.menu.shareCopied');
  }

  /** Ohne Zwischenablage (unsicherer Kontext, verweigert) steht der Text in der Meldung — man kann ihn abschreiben. */
  private writeClipboard(text: string, doneKey: string): void {
    const clip = navigator.clipboard;
    if (!clip) { this.snackbar.warn(text); return; }
    clip.writeText(text).then(
      () => this.snackbar.copy(this.translate.instant(doneKey)),
      () => this.snackbar.warn(text),
    );
  }

  private coarsePointer(): boolean {
    try { return window.matchMedia('(pointer: coarse)').matches; } catch { return false; }
  }

  private stored(key: string, min: number, max: number, fallback: number): number {
    try {
      const n = parseInt(localStorage.getItem(key) || '', 10);
      return n >= min && n <= max ? n : fallback;
    } catch { return fallback; }
  }
}
