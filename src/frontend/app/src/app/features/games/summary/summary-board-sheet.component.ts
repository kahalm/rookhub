import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { SummaryGameBoardComponent, SummaryJump } from './summary-game-board.component';

export interface SummaryBoardSheetData { pgn: string; flipped: boolean; jump: SummaryJump; }

/** Wischweg nach unten, ab dem das Blatt schließt (px). */
const SWIPE_CLOSE_PX = 80;

/**
 * Brett der Zusammenfassung als Blatt am Handy (Wunsch 2026-11-13: „wenn man am mobile einen zug anklickt soll er nicht
 * zum ende springen sondern board modal darüber einblenden + zugliste darunter zum durchklicken + freizug mit engine“).
 * Dasselbe {@link SummaryGameBoardComponent} wie neben dem Text am Desktop — Blättern, Zugliste, Varianten, eigene Züge und
 * die Engine —, im `MatDialog` nach DESIGN 5.6 („Blatt am Handy“). Die Leseposition im Text bleibt, das Seitenbrett unter
 * dem Text wird nicht verstellt. Schließen per X, Hintergrund, Esc, Wischen am Griff nach unten und mit der Zurück-Taste
 * (eigener Verlaufseintrag, MatDialog schließt bei `popstate` von selbst).
 */
@Component({
  selector: 'app-summary-board-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SummaryGameBoardComponent, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="sheet-head" (touchstart)="touchStart($event)" (touchend)="touchEnd($event)">
      <span class="grip" aria-hidden="true"></span>
      <h2 class="sheet-title">{{ 'games.summary.theGame' | translate }}</h2>
      <button mat-icon-button type="button" class="sheet-close" (click)="close()" [attr.aria-label]="'common.close' | translate">
        <mat-icon>close</mat-icon>
      </button>
    </div>
    <div class="sheet-body">
      <app-summary-game-board [pgn]="data.pgn" [flipped]="data.flipped" [jump]="data.jump" [inSheet]="true" [engineAtStart]="true" />
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; max-height: 100dvh; }
    .sheet-head { position: relative; display: flex; align-items: center; gap: 8px; padding: 14px 8px 4px 16px; touch-action: none; }
    .grip { position: absolute; top: 6px; left: 50%; width: 36px; height: 4px; margin-left: -18px; border-radius: 2px;
      background: color-mix(in srgb, currentColor 30%, transparent); }
    .sheet-title { flex: 1 1 auto; margin: 0; font-size: 1.05rem; }
    .sheet-body { overflow-y: auto; padding: 0 12px 12px; }
  `],
})
export class SummaryBoardSheetComponent {
  readonly data = inject<SummaryBoardSheetData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<SummaryBoardSheetComponent>);
  private startY: number | null = null;

  close(): void { this.ref.close(); }

  touchStart(e: TouchEvent): void { this.startY = e.touches[0]?.clientY ?? null; }
  touchEnd(e: TouchEvent): void {
    const end = e.changedTouches[0]?.clientY;
    if (this.startY !== null && end !== undefined && end - this.startY > SWIPE_CLOSE_PX) this.close();
    this.startY = null;
  }

  /**
   * Öffnet das Blatt. Unter 600 px am unteren Rand in voller Breite, sonst 560 px (DESIGN 5.6). Ein Verlaufseintrag macht
   * die Zurück-Taste zum Schließen; schließt man anders, nimmt `history.back()` ihn wieder weg (gleiche Adresse — der
   * Router bleibt stehen).
   */
  static open(dialog: MatDialog, data: SummaryBoardSheetData): MatDialogRef<SummaryBoardSheetComponent> {
    const narrow = typeof matchMedia === 'function' && matchMedia('(max-width: 599.98px)').matches;
    const marker = { ...(history.state ?? {}), rhSummarySheet: true };
    history.pushState(marker, '');
    const ref = dialog.open(SummaryBoardSheetComponent, {
      ...(narrow
        ? { width: '100vw', maxWidth: '100vw', position: { bottom: '0' }, panelClass: ['solve-mode-panel', 'solve-mode-sheet'] }
        : { width: '560px', maxWidth: '94vw', panelClass: 'solve-mode-panel' }),
      maxHeight: '100dvh', hasBackdrop: true, backdropClass: 'solve-mode-backdrop', autoFocus: 'dialog', data,
    });
    ref.afterClosed().subscribe(() => { if (history.state?.rhSummarySheet) history.back(); });
    return ref;
  }
}
