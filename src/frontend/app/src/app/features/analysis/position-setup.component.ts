import {
  Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Color, Key, Role, Piece } from 'chessground/types';
import { Chess } from 'chess.js';

export const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const EMPTY_BOARD = '8/8/8/8/8/8/8/8';

/** Aktiver „Stempel": eine zu setzende Figur oder der Radierer. */
type Brush = { role: Role; color: Color } | 'trash' | null;

const ROLES: Role[] = ['king', 'queen', 'rook', 'bishop', 'knight', 'pawn'];
const ROLE_LETTER: Record<Role, string> = {
  king: 'K', queen: 'Q', rook: 'R', bishop: 'B', knight: 'N', pawn: 'P'
};

/** Rochade-Rechte aus der reinen Brett-FEN ableiten (König+Turm auf Grundfeldern). */
export function deriveCastling(boardFen: string): string {
  const ranks = boardFen.split(' ')[0].split('/');
  if (ranks.length !== 8) return '-';
  const expand = (r: string): string[] => {
    const out: string[] = [];
    for (const ch of r) {
      if (/[1-8]/.test(ch)) for (let i = 0; i < +ch; i++) out.push('');
      else out.push(ch);
    }
    return out.length === 8 ? out : [];
  };
  const rank8 = expand(ranks[0]);   // schwarze Grundreihe
  const rank1 = expand(ranks[7]);   // weiße Grundreihe
  if (rank1.length !== 8 || rank8.length !== 8) return '-';
  let c = '';
  if (rank1[4] === 'K' && rank1[7] === 'R') c += 'K';
  if (rank1[4] === 'K' && rank1[0] === 'R') c += 'Q';
  if (rank8[4] === 'k' && rank8[7] === 'r') c += 'k';
  if (rank8[4] === 'k' && rank8[0] === 'r') c += 'q';
  return c || '-';
}

/** Volle FEN aus Brett-Platzierung + Zugrecht bauen (Rochade abgeleitet, kein e.p.). */
export function composeFen(boardFen: string, side: 'w' | 'b'): string {
  const placement = boardFen.split(' ')[0];
  return `${placement} ${side} ${deriveCastling(placement)} - 0 1`;
}

/**
 * „Stellung aufbauen": eigenständiger Brett-Editor über chessground im Frei-Modus.
 * Figuren-Palette (Antippen wählt einen Stempel, dann Feld antippen = setzen; Maus-Drag
 * zieht sie direkt aufs Brett), Radierer, vorhandene Figuren per Drag verschieben bzw.
 * vom Brett ziehen = löschen. Emittiert beim „Übernehmen" die validierte volle FEN.
 */
@Component({
  selector: 'app-position-setup',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatButtonToggleModule,
    MatTooltipModule, TranslateModule
  ],
  template: `
    <div class="ps-editor">
      <div class="ps-spares">
        @for (r of roles; track r) {
          <div class="sp" [class.active]="isBrush('black', r)"
               (pointerdown)="grabSpare($event, 'black', r)"
               [style.backgroundImage]="pieceUrl('black', r)"
               [attr.aria-label]="'black ' + r"></div>
        }
      </div>

      <div class="ps-board-wrap board-theme-brown piece-set-cburnett">
        <div #board class="ps-board"></div>
      </div>

      <div class="ps-spares">
        @for (r of roles; track r) {
          <div class="sp" [class.active]="isBrush('white', r)"
               (pointerdown)="grabSpare($event, 'white', r)"
               [style.backgroundImage]="pieceUrl('white', r)"
               [attr.aria-label]="'white ' + r"></div>
        }
      </div>

      <div class="ps-tools">
        <button mat-icon-button [class.active]="brush === 'trash'" (click)="toggleTrash()"
                [matTooltip]="'analysis.setup.erase' | translate"><mat-icon>delete</mat-icon></button>
        <mat-button-toggle-group [value]="side" (change)="side = $event.value" hideSingleSelectionIndicator="true">
          <mat-button-toggle value="w">{{ 'analysis.setup.whiteToMove' | translate }}</mat-button-toggle>
          <mat-button-toggle value="b">{{ 'analysis.setup.blackToMove' | translate }}</mat-button-toggle>
        </mat-button-toggle-group>
        <span class="ps-spacer"></span>
        <button mat-icon-button (click)="flip()" [matTooltip]="'analysis.flip' | translate"><mat-icon>cached</mat-icon></button>
        <button mat-icon-button (click)="setStart()" [matTooltip]="'analysis.setup.startpos' | translate"><mat-icon>restart_alt</mat-icon></button>
        <button mat-icon-button (click)="clearBoard()" [matTooltip]="'analysis.setup.clear' | translate"><mat-icon>clear</mat-icon></button>
      </div>

      @if (error) { <p class="ps-error">{{ error }}</p> }

      <div class="ps-actions">
        <button mat-flat-button color="primary" (click)="applyPosition()">
          <mat-icon>check</mat-icon> {{ 'analysis.setup.apply' | translate }}
        </button>
        <button mat-stroked-button (click)="cancel.emit()">
          <mat-icon>close</mat-icon> {{ 'common.cancel' | translate }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .ps-editor { display: flex; flex-direction: column; gap: 8px; width: 100%; }
    .ps-spares { display: flex; width: 100%; }
    .sp {
      flex: 1 1 0; aspect-ratio: 1; cursor: pointer;
      background-size: 88%; background-repeat: no-repeat; background-position: center;
      border-radius: 6px; touch-action: none;
    }
    .sp:hover { background-color: color-mix(in srgb, currentColor 8%, transparent); }
    .sp.active { background-color: color-mix(in srgb, #1976d2 32%, transparent); }
    .ps-board-wrap { width: 100%; }
    .ps-board { width: 100%; aspect-ratio: 1 / 1; }
    .ps-tools { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .ps-tools .active { background: color-mix(in srgb, #1976d2 22%, transparent); border-radius: 50%; }
    .ps-spacer { flex: 1 1 auto; }
    .ps-error { color: #b71c1c; margin: 0; font-size: .85rem; }
    .ps-actions { display: flex; gap: 8px; }
  `]
})
export class PositionSetupComponent implements OnInit, OnDestroy {
  /** Startstellung des Editors (volle FEN; Zugrecht wird übernommen). */
  @Input() initialFen = START_FEN;
  @Input() orientation: Color = 'white';

  /** Volle, validierte FEN. */
  @Output() apply = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  @ViewChild('board', { static: true }) boardEl!: ElementRef<HTMLDivElement>;
  private ground?: Api;

  readonly roles = ROLES;
  brush: Brush = null;
  side: 'w' | 'b' = 'w';
  error = '';

  ngOnInit(): void {
    const parts = this.initialFen.trim().split(/\s+/);
    this.side = parts[1] === 'b' ? 'b' : 'w';
    this.ground = Chessground(this.boardEl.nativeElement, {
      fen: this.initialFen,
      orientation: this.orientation,
      coordinates: true,
      viewOnly: false,
      animation: { enabled: false },
      highlight: { lastMove: false, check: false },
      movable: { free: true, color: 'both', showDests: false },
      draggable: { enabled: true, deleteOnDropOff: true, showGhost: true },
      selectable: { enabled: false },
      drawable: { enabled: false },
      events: {
        // Antippen eines Feldes setzt/löscht den aktiven Stempel (kein Zug: selectable aus).
        select: (key: Key) => this.stamp(key),
      },
    });
    requestAnimationFrame(() => this.ground?.redrawAll());
  }

  ngOnDestroy(): void {
    this.ground?.destroy();
  }

  private stamp(key: Key): void {
    if (!this.ground || !this.brush) return;
    if (this.brush === 'trash') {
      this.ground.setPieces(new Map<Key, Piece | undefined>([[key, undefined]]));
    } else {
      this.ground.newPiece({ role: this.brush.role, color: this.brush.color }, key);
    }
  }

  isBrush(color: Color, role: Role): boolean {
    return this.brush !== null && this.brush !== 'trash' &&
      this.brush.color === color && this.brush.role === role;
  }

  grabSpare(ev: PointerEvent, color: Color, role: Role): void {
    ev.preventDefault();
    this.brush = { role, color };
    // Maus: Figur direkt aufs Brett ziehen. Touch: Antippen = Stempel wählen, dann Feld tippen.
    if (ev.pointerType === 'mouse') {
      try { this.ground?.dragNewPiece({ role, color }, ev); } catch { /* Drag optional */ }
    }
  }

  toggleTrash(): void {
    this.brush = this.brush === 'trash' ? null : 'trash';
  }

  flip(): void {
    this.orientation = this.orientation === 'white' ? 'black' : 'white';
    this.ground?.toggleOrientation();
  }

  setStart(): void {
    this.side = 'w';
    this.error = '';
    this.ground?.set({ fen: START_FEN });
  }

  clearBoard(): void {
    this.error = '';
    this.ground?.set({ fen: EMPTY_BOARD });
  }

  pieceUrl(color: Color, role: Role): string {
    const name = (color === 'white' ? 'w' : 'b') + ROLE_LETTER[role];
    return `url('/piece/cburnett/${name}.svg')`;
  }

  applyPosition(): void {
    if (!this.ground) return;
    const fen = composeFen(this.ground.getFen(), this.side);
    try {
      new Chess(fen);   // wirft bei ungültiger Stellung (z.B. König fehlt)
    } catch (e: any) {
      this.error = (e?.message as string) || 'Invalid position';
      return;
    }
    this.error = '';
    this.apply.emit(fen);
  }
}
