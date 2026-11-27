import { MatDialog } from '@angular/material/dialog';
import { Key } from 'chessground/types';
import { BookPuzzleDto } from './puzzle.service';
import type { BookState } from './book-mode-host';
import { CommentSegment } from './comment-variation.util';
import { CommentBlockCache } from './line-step.util';
import { CommentDialogComponent, commentLength, LONG_COMMENT_CHARS } from './comment-dialog.component';
import { displayComment as computeDisplayComment, buildCommentLines } from './book-comment.util';

/** Brett-Vorschau einer angeklickten Kommentar-Variante (überlagert boardFen/lastMove). */
export interface VariationPreview {
  fen: string; lastMove: [Key, Key];
  /** Der Zug ist eine Drohung aus der Ausgangsstellung (Nullzug) — die Leiste sagt „Zeigt die Drohung“. */
  threat?: boolean;
}

/** Was die Kommentar-Anzeige vom Buch-Löser braucht. */
export interface BookCommentHost {
  readonly puzzle: BookPuzzleDto | null;
  readonly state: BookState;
  readonly reviewMode: boolean;
  readonly reviewIndex: number;
  readonly moveComment: string | null;
  readonly onSolutionPath: boolean;
  /** Index des nächsten erwarteten Lösungszugs bzw. Vorspiel-Halbzug des Lös-Automaten. */
  readonly moveIndex: number;
  readonly startPly: number;
  readonly titleDisplay: string | null;
  readonly chapterDisplay: string | null;
  readonly displayBookName: string;
  readonly dialog: MatDialog;
  variationPreview: VariationPreview | null;
}

/**
 * Info-/Zug-Kommentar des Buch-Lösers (Zerlegung Z18): welche Absätze gerade gelten (Review/Info: der zum
 * durchgespielten Zug, beim Lösen alle bisher gespielten, davor die Einleitung), deren klickbare Zug-Chips (Variante
 * als Brett-Vorschau) und das Fenster für lange Kommentare. Darstellung in `BookCommentBlockComponent`.
 */
export class BookCommentView {
  private readonly cache = new CommentBlockCache();

  constructor(private readonly h: BookCommentHost) {}

  /** Einzel-Kommentar für Review-/Info-Modus (Kommentar zum aktuell durchgespielten Zug, Fallback
   *  Einleitung) — Basis der Ply-Navigation; die Anzeige läuft über {@link lines}. */
  get display(): string | null {
    const h = this.h;
    if (!h.puzzle) return null;
    return computeDisplayComment(h.reviewMode, h.reviewIndex, h.moveComment, h.puzzle.comment);
  }

  /** Die im Kontext-Block angezeigten Kommentar-Absätze (gestapelt).
   *  - Review/Info: EIN Absatz (der zum durchgespielten Zug bzw. die Einleitung).
   *  - WÄHREND des Lösens: die Kommentare ALLER bereits gespielten Lösungszüge in Reihenfolge
   *    (jeder neue darunter). Züge ohne Kommentar fügen NICHTS hinzu; es gibt KEINEN Rückfall auf
   *    die Einleitung, sobald gespielt wird (leere Liste → Block ausgeblendet).
   *  - Vor dem ersten Zug: die Einleitung des Puzzles. */
  get lines(): string[] {
    const h = this.h;
    if (!h.puzzle) return [];
    return buildCommentLines({
      reviewMode: h.reviewMode,
      reviewIndex: h.reviewIndex,
      moveComment: h.moveComment,
      puzzleComment: h.puzzle.comment,
      moveComments: h.puzzle.moveComments,
      onSolutionPath: h.onSolutionPath,
      moveIndex: h.moveIndex,
      solving: h.state === 'AWAITING_USER_MOVE' || h.state === 'THINKING',
      startPly: h.startPly,
    });
  }

  /** {@link lines} in klickbare Segmente zerlegt (Text + spielbare Zug-Chips). Gecacht je
   *  Kommentar-Inhalt + Puzzle (+ Spoilerschutz-Länge), da die Auflösung nur von Puzzle-FEN/-Zügen abhängt (nicht vom
   *  Ply); derselbe Cache wie in der Kurs-Durchsicht (`line-step.util`).
   *
   *  Spoilerschutz (Woodpecker, Aufgabe 334, gemeldet 2026-11-27): solange die Aufgabe weder gelöst noch aufgedeckt
   *  ist, verankern Kommentar-Züge nur an den bisher GESPIELTEN Halbzügen ({@link spoilerPlayed}) — sonst spielte ein
   *  Klick auf „1...Bxe1“ erst den weißen Lösungszug vor. Die Einleitung steht an der Start-FEN, wenn die Linie ohne
   *  Aufbauzüge beginnt (`startPly < 0`): ihre Züge der nicht ziehenden Seite sind dann zuerst Drohungen. */
  get blocks(): CommentSegment[][] {
    const h = this.h;
    const p = h.puzzle;
    return this.cache.get(p?.id ?? 0, this.lines, p?.fen ?? '', () => (p?.moves ?? '').split(' ').filter(m => m), {
      played: this.spoilerPlayed,
      startComment: h.startPly < 0 ? p?.comment : null,
    });
  }

  /** Halbzüge der Linie, die der Kommentar-Parser verwenden darf: beim Lösen die bisher gespielten (`moveIndex`),
   *  in Durchsicht, Info und nach dem Lösen/Aufgeben `undefined` (ganze Linie). */
  get spoilerPlayed(): number | undefined {
    const h = this.h;
    if (h.reviewMode) return undefined;
    if (h.state === 'SOLVED' || h.state === 'FAILED' || h.state === 'INFO' || h.state === 'COURSE_DONE') return undefined;
    return Math.max(0, h.moveIndex);
  }

  /** Langer Kommentar (Vorwort, Einleitung): steht weiter VOLLSTÄNDIG im Kasten, ein Klick öffnet ihn zusätzlich im
   *  Fenster (Wunsch 2026-10-07: „modal, damit mehr Platz … trotzdem alles anzeigen, damit ich nicht klicken muss"). */
  get isLong(): boolean { return commentLength(this.blocks) >= LONG_COMMENT_CHARS; }

  /** Spielt die angeklickte Variante bis zu diesem Zug als Brett-Vorschau (view-only). */
  previewMove(seg: CommentSegment): void {
    if (!seg.fen || !seg.from || !seg.to) return;
    this.h.variationPreview = {
      fen: seg.fen, lastMove: [seg.from as Key, seg.to as Key], ...(seg.threat ? { threat: true } : {}),
    };
  }

  /** Vorschau beenden → zurück zur Puzzle-/Review-Stellung. */
  exitPreview(): void { this.h.variationPreview = null; }

  /** Kommentar im Fenster; ein Zug darin schließt es und spielt die Variante am Brett vor wie im Kasten. */
  openDialog(): void {
    const h = this.h;
    const subtitle = [h.chapterDisplay, h.displayBookName].filter(s => !!s).join(' · ') || null;
    h.dialog.open(CommentDialogComponent, {
      data: { title: h.titleDisplay, subtitle, blocks: this.blocks },
      width: '760px', maxWidth: '96vw', maxHeight: '90vh', autoFocus: false,
    }).afterClosed().subscribe((seg?: CommentSegment) => { if (seg) this.previewMove(seg); });
  }

  /** Klick in den Kommentartext: nur Text öffnet das Fenster — Zug-Chips und markierter Text nicht. */
  onBodyClick(ev: MouseEvent): void {
    if (!this.isLong) return;
    if ((ev.target as HTMLElement | null)?.closest('button')) return;
    if ((window.getSelection?.()?.toString() ?? '') !== '') return;
    this.openDialog();
  }
}
