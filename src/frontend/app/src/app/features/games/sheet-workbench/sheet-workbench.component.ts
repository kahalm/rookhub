import { ChangeDetectionStrategy, Component, HostListener, computed, effect, input, model, signal, untracked } from '@angular/core';
import { NgClass } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../../shared/pgn-viewer/chess-board.component';
import { EvalGraphComponent } from '../../../shared/pgn-viewer/eval-graph.component';
import { SanPipe } from '../../../shared/chess/san.pipe';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import { GameEvals } from '../game-review.util';
import { SheetEditSession } from '../sheet-edit-session';
import { correctionCurve } from './correction-curve';
import { ScanPhotos } from './scan-photos';
import { SheetEngineComponent } from './sheet-engine.component';

/**
 * Die Formular-Werkbank (0.816.0, Wunsch 2026-10-21: „die beiden sollten ident sein und nicht doppelt programmiert"): EIN
 * Bauteil für die Korrektur eines eingelesenen Partieformulars in RookHub (`/games/:id/edit`) und LeagueHub (Formular prüfen,
 * Vereinspartie korrigieren). Aufteilung wie bisher in LeagueHub, die der Nutzer besser fand:
 *
 * - **Foto**: Seiten- und Fotowahl („Foto 1 / Foto 2" für das zweite Exemplar), das ganze Foto mit der markierten Zeile des
 *   gewählten Zugs, „Größer"; am Handy eingeklappt (der Ausschnitt steht schon im Prüfteil).
 * - **Brett**: Navigation, Ersetzen/Davor einfügen, nächste unsichere Stelle, Bewertungskurve der gespeicherten Partie
 *   (`evals` + `savedSans`, ein Zickzack zeigt einen falsch gelesenen Zug), zuschaltbare Engine.
 * - **Prüfen**: Ausschnitt der Formularzeile, der Zug mit Hinweisen, „Stimmt so" / „Zug löschen", Lesarten; darunter die
 *   Zugliste. Am Handy steht dieser Teil OBEN (Wunsch 2026-09-28).
 *
 * Der Zustand liegt in der `SheetEditSession` der Seite (Speichern, Laden und Kopfdaten bleiben dort); die Seite kann über
 * Inhaltsplätze ergänzen: `[sheetPhotoTools]` (Knöpfe in der Fotoleiste), `[sheetPlyExtra]` (unter dem gewählten Zug, z. B. der
 * Kommentar in RookHub), `[sheetEnd]` (am Ende der Partie, z. B. „Weiter zu den Namen").
 */
@Component({
  selector: 'app-sheet-workbench',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass, TranslatePipe, SanPipe, ChessBoardComponent, EvalGraphComponent, SheetEngineComponent],
  templateUrl: './sheet-workbench.component.html',
  styleUrl: './sheet-workbench.component.scss',
})
export class SheetWorkbenchComponent {
  /** Der Arbeitsstand der Seite. */
  readonly s = input.required<SheetEditSession>({ alias: 'session' });
  /** Die Fotos des Formulars (Seiten, weitere Fotos je Seite). */
  readonly ph = input.required<ScanPhotos>({ alias: 'photos' });
  /** Brett gedreht (Schwarz unten) — die Seite setzt die Vorgabe, „⇅" dreht. */
  readonly flipped = model(false);
  /** Analyse der GESPEICHERTEN Partie und deren Züge — daraus die Bewertungskurve (`null` = keine). */
  readonly evals = input<GameEvals | null>(null);
  readonly savedSans = input<string[]>([]);
  readonly boardTheme = input('brown');
  readonly pieceSet = input('cburnett');
  /** Engine-Häkchen und -Auswahl als Material-Felder (RookHub, d5-rk-edit-native-engine-select); LeagueHub bleibt nativ. */
  readonly materialControls = input(false);

  readonly zoom = signal(false);
  /** Am Handy ist das ganze Foto eingeklappt (UX-036), am PC steht es immer da. */
  readonly photoOpen = signal(false);
  /** Bester Zug der zugeschalteten Engine (blau) — zum gelben Pfeil des bisherigen Zugs dazu (0.722.0). */
  readonly engineArrows = signal<BoardArrow[]>([]);
  readonly boardArrows = computed<BoardArrow[]>(() => [...this.s().arrows(), ...this.engineArrows()]);
  readonly curve = computed(() => {
    const s = this.s();
    return correctionCurve(this.evals(), this.savedSans(), s.plies(), s.fens());
  });

  constructor() {
    // Der gewählte Zug steht auf einer anderen Seite des Formulars: dorthin blättern (von Hand blättern bleibt, bis er wechselt).
    effect(() => {
      const page = this.s().currentPage();
      if (!page) return;
      untracked(() => {
        const ph = this.ph();
        const p = Math.min(page, ph.pageCount());
        if (p !== ph.shownPage()) ph.showPage(p);
      });
    });
  }

  onPhotoLoad(e: Event): void {
    const ph = this.ph();
    if (ph.shownView() > 0) return;   // die Maße für den Ausschnitt kommen nur vom Hauptfoto
    const img = e.target as HTMLImageElement;
    if (ph.shownPage() === 1) this.s().onPhotoLoad(e);
    else this.s().setPageSize(ph.shownPage(), img.naturalWidth, img.naturalHeight);
  }

  onBoardMove(m: UserBoardMove): void {
    this.s().play(m.san);
  }

  /** Klick auf die Kurve: der Zug NACH der Stellung wird der gewählte. */
  onCurveClick(ply: number): void {
    this.s().go(ply < 0 ? 0 : ply);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    const s = this.s();
    if (e.key === 'ArrowLeft') { s.go(s.cursor() - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { s.go(s.cursor() + 1); e.preventDefault(); }
  }
}
