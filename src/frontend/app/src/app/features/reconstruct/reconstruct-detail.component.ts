import { Component, OnInit, HostListener, ViewChild, ElementRef, inject, ChangeDetectionStrategy, signal, computed } from '@angular/core';
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
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { PositionSetupComponent, START_FEN } from '../analysis/position-setup.component';
import { SnackbarService } from '../../core/snackbar.service';
import { PreferencesService } from '../../core/preferences.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { GapResult, PartKind, PartInput, Reconstruction, ReconstructionPart, ReconstructService } from './reconstruct.service';

/**
 * Seite am Zug in einer FEN umstellen. Das en-passant-Feld fällt dabei weg: es beschreibt den Zug
 * DAVOR, und der gehört nach dem Wechsel der anderen Seite.
 */
export function withSideToMove(fen: string, black: boolean): string {
  const f = fen.trim().split(/\s+/);
  if (f.length < 4) return fen;
  f[1] = black ? 'b' : 'w';
  f[3] = '-';
  return f.join(' ');
}

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
    MatIconModule, MatInputModule, MatCheckboxModule, MatTooltipModule, MatButtonToggleModule,
    TranslatePipe, ChessBoardComponent, PositionSetupComponent],
  templateUrl: './reconstruct-detail.component.html',
  styleUrl: './reconstruct-detail.component.scss',
})
export class ReconstructDetailComponent implements OnInit {
  private service = inject(ReconstructService);
  private route = inject(ActivatedRoute);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private prefs = inject(PreferencesService);
  private confirm = inject(ConfirmService);

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
  /** Wählbare Suchtiefen in Halbzügen — bis 12; wie weit sie wirklich kommt, sagt die Antwort. */
  readonly gapPlyChoices = [2, 4, 6, 8, 10, 12];
  /** Vorschläge der Suche mit anzeigen? (Sie stehen in der Liste, zählen aber nicht zur Partie.) */
  showGenerated = true;
  /** Gesetzt, solange der Stellungs-Editor eine Stellung AUS einem Vorschlag bestätigt/korrigiert. */
  readonly waypointFor = signal<number | null>(null);

  /** Der Kasten um das Brett — er bekommt den Fokus, damit die Pfeiltasten sofort blättern. */
  @ViewChild('boardWrap') boardWrap?: ElementRef<HTMLElement>;

  private id = 0;

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }

  /** Die Stellung, ab der die gerade bearbeitete Zugfolge läuft (Grundstellung, wenn unbekannt). */
  readonly editStartFen = signal<string>(START_FEN);
  /** Ist die Stellung vor der bearbeiteten Zugfolge überhaupt bekannt? */
  readonly editAnchored = signal(true);

  /**
   * Wie viele Halbzüge der Eingabe das Brett zeigt — `null` heißt „alle" (der Normalfall beim
   * Eintippen). Mit den Pfeiltasten bzw. den Knöpfen blättert man hier durch die Zugfolge.
   */
  readonly editPly = signal<number | null>(null);

  /** Stellung nach den gezeigten Zügen — das Brett zeigt sie und spielt darauf weiter. */
  readonly editBoardFen = computed(() =>
    this.replay(this.editStartFen(), this.shownTokens()).fen);
  readonly editBadMove = signal<string | null>(null);
  private readonly movesSignal = signal('');

  /** Die Züge bis zum Blätter-Stand (alle, solange nicht geblättert wird). */
  private shownTokens(): string[] {
    const tokens = this.editMovesTokens();
    const ply = this.editPly();
    return ply === null ? tokens : tokens.slice(0, Math.max(0, Math.min(ply, tokens.length)));
  }

  /** Zahl der eingetippten Halbzüge. */
  plyTotal(): number { return this.editMovesTokens().length; }

  /** Der angezeigte Halbzug (0 = Ausgangsstellung). */
  plyShown(): number { const ply = this.editPly(); return ply === null ? this.plyTotal() : Math.min(ply, this.plyTotal()); }

  /** Steht das Brett am Ende der Eingabe? Nur dort darf gespielt werden. */
  atEnd(): boolean { return this.plyShown() >= this.plyTotal(); }

  /**
   * Blättern: `delta` Halbzüge vor oder zurück; `null` → ans Ende.
   *
   * <p>Am ENDE eines Teils geht es mit dem nächsten weiter, am Anfang mit dem vorigen (dort ans
   * Ende) — die Liste links ist eine Folge, und beim Durchsehen will man nicht an jeder Grenze
   * zur Maus greifen. Ein noch nicht gespeichertes Teil bleibt stehen: ein Tastendruck darf keine
   * Eingabe verwerfen.</p>
   */
  goPly(delta: number | 'start' | 'end'): void {
    const total = this.plyTotal();
    if (delta === 'end') { this.editPly.set(null); return; }
    if (delta === 'start') { this.editPly.set(0); return; }
    if (delta > 0 && this.atEnd()) { this.jumpPart(1); return; }
    if (delta < 0 && this.plyShown() === 0) { this.jumpPart(-1); return; }
    const next = Math.max(0, Math.min(this.plyShown() + delta, total));
    this.editPly.set(next >= total ? null : next);
  }

  /** Zum nächsten/vorigen Teil der Liste springen (Vorschläge zählen mit, wenn sie sichtbar sind). */
  private jumpPart(dir: 1 | -1): void {
    const id = this.editingId();
    if (!id) return;
    const parts = this.visibleParts();
    const index = parts.findIndex(p => p.id === id);
    const next = index < 0 ? undefined : parts[index + dir];
    if (!next) return;
    this.edit(next);
    this.editPly.set(dir === 1 ? 0 : null);
  }

  /**
   * „Ab hier weiterspielen": schneidet die Züge nach dem angezeigten Halbzug ab. Bewusst ein
   * eigener Knopf statt eines stillen Abschneidens beim nächsten Brettzug — sonst löschte ein
   * versehentliches Ziehen auf dem Brett den Rest einer langen Zugfolge.
   */
  truncateHere(): void {
    const ply = this.plyShown();
    this.editMoves = this.editMovesTokens().slice(0, ply).join(' ');
    this.setMoves(this.editMoves);
    this.editPly.set(null);
  }

  /** Pfeiltasten am PC: blättern, solange der Fokus nicht in einem Textfeld steht. */
  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    const target = e.target as HTMLElement | null;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) return;
    if (this.editingId() === null) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.goPly(-1); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); this.goPly(1); }
    else if (e.key === 'Home') { e.preventDefault(); this.goPly('start'); }
    else if (e.key === 'End') { e.preventDefault(); this.goPly('end'); }
  }

  // ----- Wer ist am Zug -----

  /** Steht in der Ausgangsstellung des Editors Schwarz am Zug? */
  sideBlack(): boolean { return this.editStartFen().split(' ')[1] === 'b'; }

  /**
   * Die Seite am Zug umstellen — jederzeit, auch mitten im Aufzeichnen („und dann schlug ER auf f7").
   *
   * <p>Hängt das Teil an der Stellung davor, steht die Seite dort fest; wer sie hier ändert, sagt
   * damit, dass das Bruchstück NICHT daran anschließt. Genau das passiert dann auch, statt eine
   * Angabe stehen zu lassen, die der Kette widerspricht.</p>
   */
  setSideBlack(black: boolean): void {
    if (this.sideBlack() === black) return;
    if (this.editContinues) this.editContinues = false;
    this.editAnchored.set(false);
    this.editStartFen.set(withSideToMove(this.editStartFen(), black));
    this.setMoves(this.editMoves);
  }

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
    this.waypointFor.set(null);
    // Angehängt wird an das letzte AUFGEZEICHNETE Teil; ein Vorschlag ist keine Stellung, an der
    // man weiterschreibt.
    const parts = (this.data()?.parts ?? []).filter(p => !p.generated);
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
    this.editPly.set(null);
    if (kind === PartKind.Moves) this.focusBoard();
    // Ein neues Teil hängt an der letzten bekannten Stellung — ist keine da, beginnt das Brett in
    // der Grundstellung, und die Züge stehen dann für eine Stelle, die nicht die gemeinte ist.
    this.editAnchored.set(kind !== PartKind.Moves || !!last?.endFen || parts.length === 0);
  }

  edit(part: ReconstructionPart): void {
    this.waypointFor.set(null);
    this.editingId.set(part.id);
    this.editKind.set(part.kind);
    this.editNote = part.note ?? '';
    this.editFromPly = part.fromPly ?? null;
    this.editContinues = part.continuesPrevious;
    this.editCertain = part.certain;
    this.editFen = part.fen || START_FEN;
    // ZUERST die Ausgangsstellung, DANN die Züge: `setMoves` prüft gegen `editStartFen`, und mit der
    // Stellung des zuvor bearbeiteten Teils meldete es Züge als unmöglich, die hier stimmen.
    // Ohne Anker gibt es keine Stellung davor — dann sagt der gespeicherte Haken, wer am Zug war.
    // Ein VORSCHLAG hat keinen Ketten-Eintrag (er zählt nicht zur Partie), hängt aber sehr wohl an
    // der letzten aufgezeichneten Stellung — ohne sie zeigte das Brett die Grundstellung.
    const anchor = part.generated ? this.recordedEndBefore(part) : part.startFen;
    this.editAnchored.set(part.kind !== PartKind.Moves || !!anchor);
    this.editStartFen.set(anchor || withSideToMove(START_FEN, part.blackToMove));
    // Das Brett steht am ANFANG des Teils, nicht am Ende: man klickt es an, um zu sehen, wo es
    // losgeht, und blättert dann mit den Pfeiltasten durch.
    this.editPly.set(0);
    this.editMoves = part.moves ?? '';
    this.setMoves(this.editMoves);
    if (part.kind === PartKind.Moves) this.focusBoard();
  }

  cancelEdit(): void { this.editingId.set(null); this.waypointFor.set(null); }

  /**
   * Den Brett-Kasten fokussieren. Die Pfeiltasten dürfen dem TEXTFELD nicht weggenommen werden —
   * dort bewegen sie den Schreibcursor. Also bekommt das Brett den Fokus, sobald der Editor
   * aufgeht; danach blättern die Pfeiltasten. Wer ins Zugfeld klickt, tippt dort weiter, und ein
   * Klick aufs Brett holt das Blättern zurück.
   */
  private focusBoard(): void {
    setTimeout(() => this.boardWrap?.nativeElement?.focus({ preventScroll: true }), 0);
  }

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
  boardPlayable(): boolean { return this.editBadMove() === null && this.atEnd(); }

  /** Ein Zug auf dem Brett: hinten an die Zugfolge anhängen (gespielt wird nur am Ende, s. o.). */
  onBoardMove(move: UserBoardMove): void {
    if (this.editKind() !== PartKind.Moves || !this.atEnd()) return;
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
      // Nur wenn das Teil NICHT anschließt, ist die Seite eine eigene Aussage — sonst steht sie in
      // der Stellung davor, und zwei Quellen für dieselbe Angabe widersprechen sich irgendwann.
      blackToMove: kind === PartKind.Moves && !this.editContinues && this.sideBlack(),
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
    if (this.busy()) return;
    // Bewusst KEIN window.confirm: im Vollbild rendert der Browser nur den Teilbaum des
    // Vollbild-Elements, und die native Rückfrage lag dahinter — unsichtbar, aber blockierend.
    this.confirm.ask('reconstruct.deletePartConfirm').subscribe(ok => {
      if (!ok) return;
      this.busy.set(true);
      this.service.removePart(this.id, part.id).subscribe({
        next: data => { this.busy.set(false); this.apply(data); if (this.editingId() === part.id) this.editingId.set(null); },
        error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
      });
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
    const waypoint = this.waypointFor();
    if (waypoint !== null) { this.saveWaypoint(waypoint, fen, this.editCertain); return; }
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
      blackToMove: part.blackToMove,
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

  /**
   * „Lücke schließen": sucht die Wege und setzt sie als VORSCHLÄGE in die Liste — dort lassen sie
   * sich durchklicken. Übernommen wird nichts von selbst; die Lücke bleibt offen, bis ein Mensch
   * eine Stellung bestätigt oder eine ganze Linie übernimmt.
   */
  closeGap(part: ReconstructionPart): void {
    if (this.gapSearching()) return;
    this.gapFor.set(part.id);
    this.gapResult.set(null);
    this.gapSearching.set(true);
    this.service.proposeGap(this.id, part.id, this.gapPlies).subscribe({
      next: result => {
        this.gapSearching.set(false);
        this.gapResult.set({
          partId: result.partId, maxPlies: result.maxPlies, nodes: result.nodes,
          budgetExhausted: result.budgetExhausted, deepestSearched: result.deepestSearched,
          reason: result.reason, solutions: [],
        });
        this.apply(result.detail);
        if (result.inserted > 0) {
          this.showGenerated = true;
          const first = result.detail.parts.find(p => p.generated);
          if (first) this.edit(first);   // gleich ansehen können, darum geht es
        }
      },
      error: () => {
        this.gapSearching.set(false);
        this.gapFor.set(null);
        this.snackbar.warn(this.translate.instant('reconstruct.gap.failed'));
      },
    });
  }

  /** Alle Vorschläge dieser Lücke verwerfen. */
  discardProposals(part: ReconstructionPart): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.service.discardProposals(this.id, part.id).subscribe({
      next: data => { this.busy.set(false); this.apply(data); this.editingId.set(null); this.hideGap(); },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }

  /**
   * Lässt sich die gezeigte Stellung als Wegpunkt übernehmen? Nur MITTEN im Vorschlag: der Anfang
   * ist die Stellung davor und das Ende die Zielstellung — beide stehen schon in der Liste.
   */
  canAcceptWaypoint(): boolean {
    return this.editingGenerated() && this.plyShown() > 0 && !this.atEnd();
  }

  /** „Diese Stellung stimmt": die gerade gezeigte Stellung des Vorschlags wird ein eigenes Teil. */
  acceptWaypoint(): void {
    const proposal = this.editingPart();
    const target = proposal ? this.targetAfter(proposal) : null;
    if (!proposal || !target || this.busy()) return;
    // Bestätigt heißt SICHER — der Vorschlag selbst ist unsicher, der Mensch sagt hier das Gegenteil.
    this.saveWaypoint(target.id, this.editBoardFen(), true);
  }

  /** „Diese Stellung korrigieren": dieselbe Stellung im Stellungs-Editor öffnen. */
  correctWaypoint(): void {
    const proposal = this.editingPart();
    const target = proposal ? this.targetAfter(proposal) : null;
    if (!proposal || !target) return;
    this.waypointFor.set(target.id);
    this.editKind.set(PartKind.Position);
    this.editFen = this.editBoardFen();
    this.editCertain = true;
  }

  /** Die ganze vorgeschlagene Linie übernehmen — damit ist die Lücke zu. */
  acceptProposal(): void {
    const proposal = this.editingPart();
    const target = proposal ? this.targetAfter(proposal) : null;
    if (!proposal || !target || this.busy()) return;
    this.busy.set(true);
    this.service.applyGap(this.id, target.id, proposal.moves ?? '').subscribe({
      next: data => {
        this.busy.set(false);
        this.apply(data);
        this.editingId.set(null);
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

  private saveWaypoint(targetId: number, fen: string, certain: boolean): void {
    this.busy.set(true);
    this.service.addWaypoint(this.id, targetId, fen, certain).subscribe({
      next: data => {
        this.busy.set(false);
        this.apply(data);
        this.waypointFor.set(null);
        this.editingId.set(null);
        this.hideGap();
      },
      error: err => {
        this.busy.set(false);
        const reason = err?.error?.reason;
        this.snackbar.warn(this.translate.instant(
          reason === 'invalid-fen' ? 'reconstruct.invalidFen' : 'reconstruct.saveFailed'));
      },
    });
  }

  hideGap(): void { this.gapFor.set(null); this.gapResult.set(null); }

  /**
   * Der Satz zum leeren Ergebnis. „Nicht gefunden" und „gibt es nicht" sind verschiedene Aussagen,
   * und die Antwort trennt sie — deshalb hat jeder Grund einen eigenen Text statt eines
   * gemeinsamen „keine Lösung".
   */
  gapMessageKey(): string | null {
    const result = this.gapResult();
    // Kein Grund = es hat geklappt; die Wege stehen dann als Vorschläge in der Liste.
    if (!result || result.reason == null || result.solutions.length > 0) return null;
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

  // ----- Teilen -----

  /**
   * „Ganze Partie teilen": schaltet den öffentlichen Link ein und legt ihn in die Zwischenablage.
   *
   * <p>Geteilt wird die REKONSTRUKTION, nicht eine Kopie davon — wer den Link öffnet, sieht den
   * Stand von jetzt, samt der Lücken, die noch offen sind. Genau darum geht es beim Weitergeben:
   * „so weit habe ich die Partie, erkennst du den Rest wieder?". Ein schon vergebenes Token bleibt
   * dasselbe, damit ein bereits verschickter Link gültig bleibt.</p>
   */
  shareGame(): void {
    const data = this.data();
    if (!data || this.busy()) return;
    if (data.shareToken) { this.copyShareUrl(data.shareToken); return; }

    this.busy.set(true);
    this.service.share(this.id).subscribe({
      next: token => {
        this.busy.set(false);
        this.data.set({ ...data, shareToken: token });
        this.copyShareUrl(token);
      },
      error: () => {
        this.busy.set(false);
        this.snackbar.warn(this.translate.instant('reconstruct.share.failed'));
      },
    });
  }

  /** Den Link abschalten — er läuft danach ins Leere, deshalb wird gefragt. */
  stopSharing(): void {
    const data = this.data();
    if (!data?.shareToken || this.busy()) return;
    this.confirm.ask('reconstruct.share.stopConfirm').subscribe(ok => {
      if (!ok) return;
      this.busy.set(true);
      this.service.unshare(this.id).subscribe({
        next: () => {
          this.busy.set(false);
          this.data.set({ ...data, shareToken: null });
          this.snackbar.quick(this.translate.instant('reconstruct.share.stopped'));
        },
        error: () => {
          this.busy.set(false);
          this.snackbar.warn(this.translate.instant('reconstruct.share.failed'));
        },
      });
    });
  }

  /** Die Adresse hinter dem Link; leer, solange nicht geteilt ist. */
  shareUrl(): string {
    const token = this.data()?.shareToken;
    return token ? this.service.shareUrl(token) : '';
  }

  private copyShareUrl(token: string): void {
    const url = this.service.shareUrl(token);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('reconstruct.share.copied')),
      () => this.snackbar.warn(this.translate.instant('reconstruct.copyFailed')),
    );
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
    const parts = (this.data()?.parts ?? []).filter(p => !p.generated);
    const editingId = this.editingId();
    if (editingId === 0) return parts.length > 0;
    return parts.findIndex(p => p.id === editingId) > 0;
  }

  /** Die Teile, wie sie links stehen — Vorschläge lassen sich ausblenden. */
  visibleParts(): ReconstructionPart[] {
    const parts = this.data()?.parts ?? [];
    return this.showGenerated ? parts : parts.filter(p => !p.generated);
  }

  /** Wie viele Vorschläge liegen gerade in der Liste? (Für den Schalter oben.) */
  generatedCount(): number { return (this.data()?.parts ?? []).filter(p => p.generated).length; }

  /** Das gerade bearbeitete Teil (null bei einem neuen). */
  editingPart(): ReconstructionPart | null {
    const id = this.editingId();
    return id ? (this.data()?.parts.find(p => p.id === id) ?? null) : null;
  }

  /** Wird gerade ein VORSCHLAG angesehen? Dann gibt es „stimmt" / „korrigieren" / „übernehmen". */
  editingGenerated(): boolean { return !!this.editingPart()?.generated; }

  /**
   * Zu welchem Teil gehört die Lücke VOR der Zeile <paramref name="index"/> — oder null, wenn dort
   * keine ist? Die Zeile steht vor dem ERSTEN Eintrag der Lücke, und das ist bei vorhandenen
   * Vorschlägen der erste Vorschlag: sie liegen ja IN der Lücke, nicht davor.
   */
  gapTargetAt(index: number): ReconstructionPart | null {
    const parts = this.visibleParts();
    const part = parts[index];
    if (!part || index === 0) return null;
    if (parts[index - 1]?.generated) return null;          // mitten in den Vorschlägen derselben Lücke
    if (part.generated) return this.targetAfter(part);     // erster Vorschlag → die Lücke gehört dem Teil danach
    return part.continuesPrevious ? null : part;
  }

  /** Die Stellung, an der ein Vorschlag hängt: das Ende des letzten AUFGEZEICHNETEN Teils davor. */
  private recordedEndBefore(part: ReconstructionPart): string | null {
    const parts = this.data()?.parts ?? [];
    for (let i = parts.findIndex(p => p.id === part.id) - 1; i >= 0; i--)
      if (!parts[i].generated) return parts[i].endFen ?? null;
    return null;
  }

  /** Halbzüge eines Teils — bei Vorschlägen rechnet sie der Server nicht mit (sie zählen nicht zur Partie). */
  plyCountOf(part: ReconstructionPart): number {
    if (!part.generated) return part.plyCount;
    return (part.moves ?? '').split(/\s+/).filter(t => t).length;
  }

  /** Stehen vor diesem Teil Vorschläge? (Dann gibt es an der Lücke ein „Verwerfen".) */
  hasProposalsBefore(part: ReconstructionPart): boolean {
    const parts = this.data()?.parts ?? [];
    const index = parts.findIndex(p => p.id === part.id);
    return index > 0 && parts[index - 1].generated;
  }

  /** Das aufgezeichnete Teil NACH diesem Vorschlag — darauf beziehen sich Übernehmen und Verwerfen. */
  targetAfter(part: ReconstructionPart): ReconstructionPart | null {
    return (this.data()?.parts ?? []).find(p => p.ordinal > part.ordinal && !p.generated) ?? null;
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
