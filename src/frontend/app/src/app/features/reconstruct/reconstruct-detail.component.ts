import { Component, OnInit, inject, ChangeDetectionStrategy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { PositionSetupComponent, START_FEN } from '../analysis/position-setup.component';
import { SnackbarService } from '../../core/snackbar.service';
import { PreferencesService } from '../../core/preferences.service';
import { GapResult, GapSolution, PartKind, PartInput, Reconstruction, ReconstructionPart, ReconstructService } from './reconstruct.service';

/**
 * Der Arbeitsplatz für EINE zu rekonstruierende Partie: Bruchstücke aufzeichnen, ordnen, ergänzen.
 *
 * <p><b>Warum Teile und nicht ein PGN:</b> von einer am Brett gespielten Partie weiß man die ersten
 * Züge meist vollständig, danach nur noch einzelne Stellungen und Zugfolgen. Ein PGN kann eine
 * Stellung ohne den Weg dorthin nicht ausdrücken — hier ist genau das die Eingabe.</p>
 *
 * <p>Die Prüfung macht der SERVER (er kennt die Schachregeln ohnehin für die Analyse) und schickt
 * mit jeder Antwort die ganze Kette zurück: was verankert ist, welcher Zug nicht geht, wie viele
 * Halbzüge ab der Grundstellung schon stehen. Hier wird nur angezeigt und eingegeben — mit einer
 * Ausnahme: das Brett spielt die eingetippten Züge lokal mit (chess.js), damit man beim Klicken
 * sofort sieht, wo man ist, ohne für jeden Zug einen Server-Aufruf zu machen.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-reconstruct-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatCheckboxModule, MatTooltipModule, TranslatePipe, ChessBoardComponent, PositionSetupComponent],
  templateUrl: './reconstruct-detail.component.html',
  styleUrl: './reconstruct-detail.component.scss',
})
export class ReconstructDetailComponent implements OnInit {
  private service = inject(ReconstructService);
  private route = inject(ActivatedRoute);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private prefs = inject(PreferencesService);

  readonly Kind = PartKind;
  readonly data = signal<Reconstruction | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  /** Id des Teils, das gerade bearbeitet wird; 0 = ein neues, noch nicht gespeichertes. */
  readonly editingId = signal<number | null>(null);
  readonly editKind = signal<PartKind>(PartKind.Moves);
  /** Eingabefeld der Zugfolge (SAN, Zugnummern erlaubt) bzw. die bearbeitete Stellung. */
  editMoves = '';
  editFen = START_FEN;
  editNote = '';
  editFromPly: number | null = null;
  /** „Schließt direkt an das vorige Teil an" — ohne den Haken liegt dazwischen eine Lücke. */
  editContinues = false;
  /** „Da bin ich mir sicher" — der Haken ist gesetzt, solange nichts anderes gesagt wird. */
  editCertain = true;
  /** Kopfdaten-Formular (eingeklappt, bis jemand es aufmacht). */
  showHead = false;
  head = { title: '', white: '', black: '', event: '', playedOn: '', result: '', note: '' };

  /** Lückensuche: für welches Teil läuft/lief sie, was kam heraus, wie weit darf sie suchen. */
  readonly gapFor = signal<number | null>(null);
  readonly gapResult = signal<GapResult | null>(null);
  readonly gapSearching = signal(false);
  /** Suchtiefe in HALBZÜGEN — die Auswahl steht im Formular, weil der Baum mit jedem Halbzug wächst. */
  gapPlies = 4;
  readonly gapPlyChoices = [2, 4, 6];

  private id = 0;

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }

  /** Die Stellung, ab der die gerade bearbeitete Zugfolge läuft (Grundstellung, wenn unbekannt). */
  readonly editStartFen = signal<string>(START_FEN);
  /** Ist die Stellung vor der bearbeiteten Zugfolge überhaupt bekannt? */
  readonly editAnchored = signal(true);

  /** Stellung nach den bereits eingetippten Zügen — das Brett zeigt sie und spielt darauf weiter. */
  readonly editBoardFen = computed(() => this.replay(this.editStartFen(), this.editMovesTokens()).fen);
  readonly editBadMove = signal<string | null>(null);
  private readonly movesSignal = signal('');

  ngOnInit(): void {
    this.id = Number(this.route.snapshot.paramMap.get('id')) || 0;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.service.get(this.id).subscribe({
      next: data => {
        this.apply(data);
        this.loading.set(false);
        // Eine leere Rekonstruktion hat genau einen sinnvollen nächsten Schritt: die ersten Züge.
        // Dafür erst einen Knopf suchen zu müssen ist ein Umweg ohne Entscheidung.
        if (data.parts.length === 0) this.startNew(PartKind.Moves);
      },
      error: () => { this.loading.set(false); this.snackbar.warn(this.translate.instant('reconstruct.loadFailed')); },
    });
  }

  private apply(data: Reconstruction): void {
    this.data.set(data);
    this.head = {
      title: data.title, white: data.white ?? '', black: data.black ?? '', event: data.event ?? '',
      playedOn: data.playedOn ?? '', result: data.result ?? '', note: data.note ?? '',
    };
  }

  // ----- Kopfdaten -----

  saveHead(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.service.updateHead(this.id, {
      title: this.head.title.trim() || '?',
      white: this.head.white.trim() || null,
      black: this.head.black.trim() || null,
      event: this.head.event.trim() || null,
      playedOn: this.head.playedOn || null,
      result: this.head.result.trim() || null,
      note: this.head.note.trim() || null,
    }).subscribe({
      next: data => { this.busy.set(false); this.apply(data); this.showHead = false; },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }

  // ----- Teile -----

  /** Neues Teil anlegen: Zugfolge hängt an der letzten bekannten Stellung, Stellung am Editor. */
  startNew(kind: PartKind): void {
    const parts = this.data()?.parts ?? [];
    const last = parts.length ? parts[parts.length - 1] : null;
    this.editingId.set(0);
    this.editKind.set(kind);
    this.editMoves = '';
    this.setMoves('');
    this.editNote = '';
    this.editFromPly = null;
    this.editCertain = true;
    // Nach einer STELLUNG ist die Fortsetzung der Normalfall („in dieser Stellung ging es so weiter"),
    // nach einer Zugfolge das Bruchstück von anderswo — hingen sie aneinander, wären sie ein Teil.
    this.editContinues = !!last && last.kind === PartKind.Position && kind === PartKind.Moves;
    this.editFen = last?.endFen || START_FEN;
    this.editStartFen.set(kind === PartKind.Moves ? (last?.endFen || START_FEN) : START_FEN);
    // Ein neues Teil hängt an der letzten bekannten Stellung — ist keine da, beginnt das Brett in
    // der Grundstellung, und die Züge stehen dann für eine Stelle, die nicht die gemeinte ist.
    this.editAnchored.set(kind !== PartKind.Moves || !!last?.endFen || parts.length === 0);
  }

  edit(part: ReconstructionPart): void {
    this.editingId.set(part.id);
    this.editKind.set(part.kind);
    this.editNote = part.note ?? '';
    this.editFromPly = part.fromPly ?? null;
    this.editContinues = part.continuesPrevious;
    this.editCertain = part.certain;
    this.editFen = part.fen || START_FEN;
    this.editAnchored.set(part.kind !== PartKind.Moves || !!part.startFen);
    // ZUERST die Ausgangsstellung, DANN die Züge: `setMoves` prüft gegen `editStartFen`, und mit der
    // Stellung des zuvor bearbeiteten Teils meldete es Züge als unmöglich, die hier stimmen.
    this.editStartFen.set(part.startFen || START_FEN);
    this.editMoves = part.moves ?? '';
    this.setMoves(this.editMoves);
  }

  cancelEdit(): void { this.editingId.set(null); }

  /** Der Zugtext hat sich geändert (Tippen oder Einfügen) — Brett und Fehlerhinweis nachziehen. */
  onMovesInput(value: string): void {
    this.editMoves = value;
    this.setMoves(value);
  }

  /**
   * Darf am Brett gespielt werden? Nur, solange die eingetippte Zugfolge WIRKLICH bis zum Ende
   * spielbar ist. Sonst zeigt das Brett die Stellung vor dem ersten unmöglichen Zug, jeder weitere
   * Zug landete aber hinter diesem Zug im Text und käme nie auf dem Brett an — gemeldet als
   * „ich kann nur einen Zug machen und nicht mehrere".
   */
  boardPlayable(): boolean { return this.editBadMove() === null; }

  /** Ein Zug auf dem Brett: hinten an die Zugfolge anhängen. */
  onBoardMove(move: UserBoardMove): void {
    if (this.editKind() !== PartKind.Moves) return;
    this.editMoves = `${this.editMoves.trim()} ${move.san}`.trim();
    this.setMoves(this.editMoves);
  }

  /** Letzten Zug der Eingabe zurücknehmen. */
  undoMove(): void {
    const tokens = this.editMovesTokens();
    tokens.pop();
    this.editMoves = tokens.join(' ');
    this.setMoves(this.editMoves);
  }

  /**
   * Speichert das Teil im Editor. Mit <paramref name="then"/> geht es DIREKT mit dem nächsten Teil
   * weiter, statt den Editor zu schließen — so, wie man sich erinnert: Züge, bis es nicht mehr
   * weitergeht, dann die nächste Stellung, dann wieder Züge.
   */
  savePart(then: PartKind | null = null): void {
    if (this.busy()) return;
    const kind = this.editKind();
    const input: PartInput = {
      kind,
      moves: kind === PartKind.Moves ? this.editMoves : null,
      fen: kind === PartKind.Position ? this.editFen : null,
      fromPly: this.editFromPly ?? null,
      continuesPrevious: this.editContinues,
      certain: this.editCertain,
      note: this.editNote.trim() || null,
    };
    const editingId = this.editingId();
    if (editingId === null) return;
    const call = editingId === 0
      ? this.service.addPart(this.id, input)
      : this.service.updatePart(this.id, editingId, input);

    this.busy.set(true);
    call.subscribe({
      next: data => {
        this.busy.set(false);
        this.apply(data);
        if (then === null) this.editingId.set(null);
        else this.startNew(then);   // hängt am jetzt LETZTEN Teil, also am gerade gespeicherten
      },
      error: err => {
        this.busy.set(false);
        const reason = err?.error?.reason;
        const key = reason === 'invalid-fen' ? 'reconstruct.invalidFen'
          : reason === 'no-moves' ? 'reconstruct.noMoves'
          : reason === 'too-many-parts' ? 'reconstruct.tooManyParts'
          : 'reconstruct.saveFailed';
        this.snackbar.warn(this.translate.instant(key));
      },
    });
  }

  removePart(part: ReconstructionPart): void {
    if (this.busy() || !confirm(this.translate.instant('reconstruct.deletePartConfirm'))) return;
    this.busy.set(true);
    this.service.removePart(this.id, part.id).subscribe({
      next: data => { this.busy.set(false); this.apply(data); if (this.editingId() === part.id) this.editingId.set(null); },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }

  /** Teil eine Stelle nach oben/unten schieben. */
  move(part: ReconstructionPart, delta: number): void {
    const parts = [...(this.data()?.parts ?? [])];
    const from = parts.findIndex(p => p.id === part.id);
    const to = from + delta;
    if (from < 0 || to < 0 || to >= parts.length || this.busy()) return;
    parts.splice(to, 0, ...parts.splice(from, 1));
    this.busy.set(true);
    this.service.reorder(this.id, parts.map(p => p.id)).subscribe({
      next: data => { this.busy.set(false); this.apply(data); },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }

  /**
   * Vom Zug-Editor zur Stellung wechseln („hier komme ich mit Zügen nicht weiter"). Getipptes wird
   * vorher gespeichert; ein leerer Editor wechselt nur die Art und legt nichts an.
   */
  toPosition(): void {
    if (this.busy()) return;
    if (this.editMovesTokens().length > 0) this.savePart(PartKind.Position);
    else this.startNew(PartKind.Position);
  }

  /**
   * Stellung aus dem Editor übernehmen (Knopf „Übernehmen" im Stellungs-Editor) — und gleich mit
   * Zügen ab dieser Stellung weitermachen. Wer stattdessen die NÄCHSTE Stellung festhalten will,
   * klickt dort „Stellung eingeben"; der leere Zug-Editor legt dabei nichts an.
   */
  onPositionApplied(fen: string): void {
    this.editFen = fen;
    this.savePart(this.editingId() === 0 ? PartKind.Moves : null);
  }

  /**
   * „Sicher"-Haken eines gespeicherten Teils umlegen, ohne den Editor zu öffnen — beim Aufzeichnen
   * fällt einem oft erst später ein, dass man sich bei etwas doch nicht sicher ist.
   */
  toggleCertain(part: ReconstructionPart): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.service.updatePart(this.id, part.id, {
      kind: part.kind,
      moves: part.kind === PartKind.Moves ? part.moves ?? '' : null,
      fen: part.kind === PartKind.Position ? part.fen ?? '' : null,
      fromPly: part.fromPly ?? null,
      continuesPrevious: part.continuesPrevious,
      certain: !part.certain,
      note: part.note ?? null,
    }).subscribe({
      next: data => { this.busy.set(false); this.apply(data); },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }

  // ----- Lücke schließen -----

  /**
   * Lässt sich vor diesem Teil überhaupt suchen? Nur wenn eine Lücke davor liegt UND das Teil eine
   * STELLUNG ist: eine Zugfolge nach einer Lücke hat selbst keine bekannte Ausgangsstellung, und
   * genau die wäre das Ziel der Suche.
   */
  canCloseGap(part: ReconstructionPart, index: number): boolean {
    return index > 0 && !part.continuesPrevious && part.kind === PartKind.Position;
  }

  closeGap(part: ReconstructionPart): void {
    if (this.gapSearching()) return;
    this.gapFor.set(part.id);
    this.gapResult.set(null);
    this.gapSearching.set(true);
    this.service.solveGap(this.id, part.id, this.gapPlies).subscribe({
      next: result => { this.gapSearching.set(false); this.gapResult.set(result); },
      error: () => {
        this.gapSearching.set(false);
        this.gapFor.set(null);
        this.snackbar.warn(this.translate.instant('reconstruct.gap.failed'));
      },
    });
  }

  hideGap(): void { this.gapFor.set(null); this.gapResult.set(null); }

  /** Einen gefundenen Weg übernehmen — er wird als eigenes Teil vor die Stellung gesetzt. */
  applyGap(solution: GapSolution): void {
    const partId = this.gapFor();
    if (partId === null || this.busy()) return;
    this.busy.set(true);
    this.service.applyGap(this.id, partId, solution.san).subscribe({
      next: data => {
        this.busy.set(false);
        this.apply(data);
        this.hideGap();
        this.snackbar.info(this.translate.instant('reconstruct.gap.applied'));
      },
      error: err => {
        this.busy.set(false);
        const reason = err?.error?.reason;
        this.snackbar.warn(this.translate.instant(
          reason === 'does-not-fit' ? 'reconstruct.gap.doesNotFit' : 'reconstruct.saveFailed'));
      },
    });
  }

  /**
   * Der Satz zum leeren Ergebnis. „Nicht gefunden" und „gibt es nicht" sind verschiedene Aussagen,
   * und die Antwort trennt sie — deshalb hat jeder Grund einen eigenen Text statt eines
   * gemeinsamen „keine Lösung".
   */
  gapMessageKey(): string | null {
    const result = this.gapResult();
    if (!result || result.solutions.length > 0) return null;
    switch (result.reason) {
      case 'unreachable': return 'reconstruct.gap.unreachable';
      case 'too-far': return 'reconstruct.gap.tooFar';
      case 'same-position': return 'reconstruct.gap.samePosition';
      case 'budget': return 'reconstruct.gap.budget';
      case 'no-anchor': return 'reconstruct.gap.noAnchor';
      case 'no-gap': return 'reconstruct.gap.noGap';
      case 'no-previous': return 'reconstruct.gap.noPrevious';
      case 'target-not-a-position': return 'reconstruct.gap.targetNotAPosition';
      case 'invalid-from':
      case 'invalid-to': return 'reconstruct.gap.invalidPosition';
      default: return 'reconstruct.gap.none';
    }
  }

  copyPrefix(): void {
    const san = this.data()?.prefixSan ?? '';
    if (!san) return;
    navigator.clipboard?.writeText(san).then(
      () => this.snackbar.info(this.translate.instant('reconstruct.copied')),
      () => this.snackbar.warn(this.translate.instant('reconstruct.copyFailed')),
    );
  }

  // ----- Anzeige-Helfer -----

  /** Zugnummer aus dem Halbzug (0 → Zug 1). */
  moveNo(ply: number): number { return Math.floor(ply / 2) + 1; }

  /** Gibt es überhaupt ein Teil davor, an das man anschließen könnte? */
  hasPrevious(): boolean {
    const parts = this.data()?.parts ?? [];
    const editingId = this.editingId();
    if (editingId === 0) return parts.length > 0;
    return parts.findIndex(p => p.id === editingId) > 0;
  }

  /** Kurzfassung eines Teils für die Liste. */
  summary(part: ReconstructionPart): string {
    if (part.kind === PartKind.Position) return part.fen ?? '';
    const moves = (part.moves ?? '').split(' ').filter(m => m);
    return moves.length > 12 ? `${moves.slice(0, 12).join(' ')} …` : moves.join(' ');
  }

  /** Der Zustand eines Teils als Schlüssel für Text und Farbe. */
  state(part: ReconstructionPart): 'ok' | 'floating' | 'broken' | 'mismatch' {
    if (part.mismatch) return 'mismatch';
    if (!part.anchored) return 'floating';
    return part.valid ? 'ok' : 'broken';
  }

  private setMoves(text: string): void {
    this.movesSignal.set(text);
    const replayed = this.replay(this.editStartFen(), this.editMovesTokens());
    this.editBadMove.set(replayed.bad);
  }

  private editMovesTokens(): string[] {
    return this.movesSignal()
      .split(/\s+/)
      .map(t => t.trim())
      .filter(t => t && !/^\d+\.*$/.test(t) && !['1-0', '0-1', '1/2-1/2', '*'].includes(t))
      .map(t => t.replace(/^\d+\.+/, ''));
  }

  /** Züge lokal nachspielen — für Brettanzeige und Sofort-Hinweis; die Wahrheit sagt der Server. */
  private replay(startFen: string, moves: string[]): { fen: string; bad: string | null } {
    let chess: Chess;
    try { chess = new Chess(startFen); } catch { return { fen: START_FEN, bad: null }; }
    for (const san of moves) {
      try {
        if (!chess.move(san)) return { fen: chess.fen(), bad: san };
      } catch { return { fen: chess.fen(), bad: san }; }
    }
    return { fen: chess.fen(), bad: null };
  }
}
