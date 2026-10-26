import { computed, signal } from '@angular/core';
import { START_FEN } from '../analysis/position-setup.component';
import { moveTokens, replayMoves, tokensUpTo, withSideToMove } from './reconstruct-moves.util';
import { whiteToMove } from '../../shared/chess/fen-side';

/**
 * Die Zugfolge im Editor eines Rekonstruktions-Teils: eingetippter Text, die Stellung davor, der Blätter-Stand
 * und die daraus nachgespielte Brett-Stellung samt erstem unmöglichen Zug. Das Brett spielt die Züge lokal
 * (chess.js) mit — geprüft wird beim Speichern vom Server.
 */
export class ReconstructMovesDraft {
  /** Eingabefeld der Zugfolge (SAN, Zugnummern erlaubt). */
  text = '';
  /** Die Stellung, ab der die gerade bearbeitete Zugfolge läuft (Grundstellung, wenn unbekannt). */
  readonly startFen = signal<string>(START_FEN);
  /** Ist die Stellung vor der bearbeiteten Zugfolge überhaupt bekannt? */
  readonly anchored = signal(true);

  /**
   * Wie viele Halbzüge der Eingabe das Brett zeigt — `null` heißt „alle" (der Normalfall beim
   * Eintippen). Mit den Pfeiltasten bzw. den Knöpfen blättert man hier durch die Zugfolge.
   */
  readonly ply = signal<number | null>(null);

  /** Stellung nach den gezeigten Zügen — das Brett zeigt sie und spielt darauf weiter. */
  readonly boardFen = computed(() =>
    replayMoves(this.startFen(), this.shownTokens()).fen);
  readonly badMove = signal<string | null>(null);
  private readonly movesSignal = signal('');

  /** Die Züge bis zum Blätter-Stand (alle, solange nicht geblättert wird). */
  private shownTokens(): string[] {
    return tokensUpTo(this.tokens(), this.ply());
  }

  /** Zahl der eingetippten Halbzüge. */
  plyTotal(): number { return this.tokens().length; }

  /** Der angezeigte Halbzug (0 = Ausgangsstellung). */
  plyShown(): number { const ply = this.ply(); return ply === null ? this.plyTotal() : Math.min(ply, this.plyTotal()); }

  /** Steht das Brett am Ende der Eingabe? Nur dort darf gespielt werden. */
  atEnd(): boolean { return this.plyShown() >= this.plyTotal(); }

  /**
   * Darf am Brett gespielt werden? Nur, solange die eingetippte Zugfolge WIRKLICH bis zum Ende
   * spielbar ist. Sonst zeigt das Brett die Stellung vor dem ersten unmöglichen Zug, jeder weitere
   * Zug landete aber hinter diesem Zug im Text und käme nie auf dem Brett an — gemeldet als
   * „ich kann nur einen Zug machen und nicht mehrere".
   */
  playable(): boolean { return this.badMove() === null && this.atEnd(); }

  /** Steht in der Ausgangsstellung des Editors Schwarz am Zug? */
  sideBlack(): boolean { return !whiteToMove(this.startFen()); }

  /** Die Seite am Zug der Ausgangsstellung umstellen — die Stellung davor gilt damit als unbekannt. */
  setSideBlack(black: boolean): void {
    this.anchored.set(false);
    this.startFen.set(withSideToMove(this.startFen(), black));
    this.setText(this.text);
  }

  /**
   * „Ab hier weiterspielen": schneidet die Züge nach dem angezeigten Halbzug ab. Bewusst ein
   * eigener Knopf statt eines stillen Abschneidens beim nächsten Brettzug — sonst löschte ein
   * versehentliches Ziehen auf dem Brett den Rest einer langen Zugfolge.
   */
  truncateHere(): void {
    const ply = this.plyShown();
    this.setText(this.tokens().slice(0, ply).join(' '));
    this.ply.set(null);
  }

  /** Einen Zug hinten an die Zugfolge anhängen. */
  append(san: string): void {
    this.setText(`${this.text.trim()} ${san}`.trim());
  }

  /** Letzten Zug der Eingabe zurücknehmen. */
  undoMove(): void {
    const tokens = this.tokens();
    tokens.pop();
    this.setText(tokens.join(' '));
  }

  /** Zugtext setzen (Tippen, Einfügen, Brettzug) — Brett und Fehlerhinweis ziehen nach. */
  setText(text: string): void {
    this.text = text;
    this.movesSignal.set(text);
    const replayed = replayMoves(this.startFen(), this.tokens());
    this.badMove.set(replayed.bad);
  }

  tokens(): string[] { return moveTokens(this.movesSignal()); }
}
