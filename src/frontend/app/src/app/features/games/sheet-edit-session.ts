import { computed, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Chess } from 'chess.js';
import { BoardArrow } from '../../shared/pgn-viewer/chess-board.component';
import { ScoresheetOption, ScoresheetPly, ScoresheetResolveResult } from './scoresheet.service';
import {
  EditPly, START_FEN, cropView, fensOf, fromServer, nextUncertainFrom, resolveRequest, revalidate, stripSheetNotes, userPly,
  writtenIndexAt,
} from './game-edit.util';

/** Eine Zeile der Zugliste: Zugnummer + Index des weißen und des schwarzen Halbzugs. */
export interface MoveRow { no: number; white: number; black: number | null; }

/** Was die Sitzung von ihrer Seite braucht. */
export interface SheetEditHost {
  /** Den Rest ab `writtenFrom` neu aufbereiten (Server, ohne Modell-Aufruf). */
  resolve(prefix: string[], writtenFrom: number): Observable<ScoresheetResolveResult>;
  /** Etwas wurde geändert (Seite merkt sich „ungespeichert"). */
  changed?(): void;
  /** Der Cursor ist gewandert (Seite hält den Halbzug in der Zugliste sichtbar). */
  moved?(): void;
  /** Neu aufbereiten ging nicht — der bisherige Rest bleibt stehen. */
  resolveFailed?(): void;
  /** Abonnement an die Lebensdauer der Seite hängen (`takeUntilDestroyed`). */
  bind?<T>(o: Observable<T>): Observable<T>;
}

/**
 * Der Arbeitsstand einer Partie-Korrektur: Halbzüge mit Formular-Einträgen und Lesarten, der Cursor (das Brett
 * zeigt die Stellung VOR dem gewählten Halbzug), Ersetzen/Einfügen/Löschen, Bestätigen — und nach jeder Änderung an
 * einer eingelesenen Partie wird der REST aus den Formular-Einträgen neu aufbereitet.
 *
 * <para>Geteilt zwischen RookHubs Korrekturseite (`GameEditComponent`, `/games/:id/edit`) und der Formular-Korrektur
 * der Vereins-Datenbank in LeagueHub. Die Regeln selbst (welcher Eintrag zu welchem Halbzug gehört usw.) stehen rein in
 * `game-edit.util.ts`; hier steht nur, wie sie zusammenspielen. Alles in Signalen — die Antworten des Servers kommen
 * außerhalb der Zone an.</para>
 */
export class SheetEditSession {
  readonly plies = signal<EditPly[]>([]);
  readonly cursor = signal(0);
  readonly mode = signal<'replace' | 'insert'>('replace');
  readonly unresolved = signal<string[]>([]);
  readonly unresolvedFrom = signal<number | null>(null);
  /** Eingelesene Partie (Formular-Einträge da) — sonst eine gewöhnliche, dann wird nur nachgespielt. */
  readonly isScoresheet = signal(false);
  /** Je Formular-Eintrag der Kasten auf dem Foto (vom Modell; ältere Einlesungen haben keine). */
  readonly boxes = signal<(number[] | null)[]>([]);
  readonly sheetEntries = signal<string[]>([]);
  /** Pixelmaße des Fotos (aufrecht, wie der Browser es zeigt) — für das Seitenverhältnis des Ausschnitts. */
  readonly photoSize = signal<{ w: number; h: number } | null>(null);
  /** Formular über mehrere Fotos (0.600.0): je Formular-Eintrag seine Seite (ab 1; fehlt = 1) … */
  readonly entryPages = signal<number[]>([]);
  /** … und die Pixelmaße jeder Seite (Seite 1 fällt auf `photoSize` zurück). */
  readonly pageSizes = signal<Record<number, { w: number; h: number }>>({});
  readonly busy = signal(false);
  /** Ausgangsstellung: der FEN-Kopf einer Stellungspartie, sonst die Grundstellung (eingelesene Formulare immer). Brett
   *  und Legalität rechnen ab hier — vorher immer ab der Grundstellung, und bei einer Partie mit FEN-Kopf zeigte das Brett
   *  für jeden Halbzug die Grundstellung, ein Zug daran machte den Rest illegal. */
  readonly startFen = signal(START_FEN);

  readonly legalCount = computed(() => {
    const idx = this.plies().findIndex(p => p.illegal);
    return idx < 0 ? this.plies().length : idx;
  });
  readonly illegalCount = computed(() => this.plies().length - this.legalCount());
  readonly fens = computed(() => fensOf(this.plies(), this.startFen()));
  readonly cursorFen = computed(() => this.fens()[Math.min(this.cursor(), this.fens().length - 1)]);
  readonly current = computed<EditPly | null>(() => this.plies()[this.cursor()] ?? null);
  readonly lastMove = computed<[string, string] | undefined>(() => {
    const prev = this.plies()[this.cursor() - 1];
    return prev && !prev.illegal ? [prev.uci.slice(0, 2), prev.uci.slice(2, 4)] : undefined;
  });
  /** Der bisherige Zug an dieser Stelle als gelber Pfeil — was man ersetzen würde. */
  readonly arrows = computed<BoardArrow[]>(() => {
    const p = this.current();
    return p && !p.illegal && p.uci ? [{ from: p.uci.slice(0, 2), to: p.uci.slice(2, 4), brush: 'yellow' }] : [];
  });
  /**
   * Der Ausschnitt des Formulars zum gewählten Halbzug: der Eintrag, aus dem der Zug stammt, mit Umfeld. Am Ende der
   * Zugliste der erste Eintrag, der sich nicht auflösen ließ. Ohne Kasten (eingefügter Zug, ältere Einlesung) keiner.
   */
  readonly crop = computed(() => {
    const w = this.entryIndex();
    if (w === null) return null;
    const page = this.pageOf(w);
    const size = this.sizeOf(page);
    if (!size) return null;
    const p = this.current();
    const view = cropView(this.boxes()[w], size.w, size.h);
    if (!view) return null;
    return { view, page, written: this.sheetEntries()[w] ?? '', uncertain: p ? p.uncertain && !p.confirmed : true };
  });
  /** Auf welcher Seite der Eintrag des gewählten Halbzugs steht (`null` = keiner) — die Korrekturseite blättert dorthin. */
  readonly currentPage = computed(() => {
    const w = this.entryIndex();
    return w === null ? null : this.pageOf(w);
  });
  /**
   * Wo der Eintrag des gewählten Halbzugs auf dem GANZEN Foto steht (Prozent von links/oben, Breite, Höhe) — zum
   * Markieren im großen Bild, damit man die Zeile im Zusammenhang sieht (Wunsch 2026-09-28). Dieselbe Wahl des
   * Eintrags wie beim Ausschnitt.
   */
  readonly mark = computed(() => {
    const p = this.current();
    const w = this.entryIndex();
    if (w === null) return null;
    const b = this.boxes()[w];
    if (!b || b.length !== 4) return null;
    const c = (v: number) => Math.max(0, Math.min(1000, v)) / 10;
    const [x0, y0, x1, y1] = [c(b[0]), c(b[1]), c(b[2]), c(b[3])];
    if (x1 <= x0 || y1 <= y0) return null;
    return { left: x0, top: y0, width: x1 - x0, height: y1 - y0, page: this.pageOf(w),
      uncertain: p ? p.uncertain && !p.confirmed : true };
  });
  /** Der Formular-Eintrag des gewählten Halbzugs; am Ende der Zugliste der erste unaufgelöste. */
  private readonly entryIndex = computed<number | null>(() => {
    const p = this.current();
    const w = p ? p.w : this.unresolved().length ? this.unresolvedFrom() : null;
    return w === null || w === undefined ? null : w;
  });
  readonly uncertainLeft = computed(() => this.plies().filter(p => p.uncertain && !p.confirmed && !p.illegal).length);
  readonly rows = computed<MoveRow[]>(() => {
    const out: MoveRow[] = [];
    const n = this.plies().length;
    for (let i = 0; i < n; i += 2) out.push({ no: i / 2 + 1, white: i, black: i + 1 < n ? i + 1 : null });
    return out;
  });

  constructor(private readonly host: SheetEditHost) {}

  /** Seite eines Formular-Eintrags (ab 1). */
  pageOf(w: number): number {
    const n = this.entryPages()[w];
    return n && n > 1 ? n : 1;
  }

  /** Pixelmaße einer Seite, sobald ihr Foto geladen ist. */
  sizeOf(page: number): { w: number; h: number } | null {
    return this.pageSizes()[page] ?? (page === 1 ? this.photoSize() : null);
  }

  setPageSize(page: number, w: number, h: number): void {
    if (w > 0 && h > 0) this.pageSizes.update(s => ({ ...s, [page]: { w, h } }));
  }

  /** Den Stand einer Einlesung übernehmen und auf die erste unsichere Stelle gehen. */
  loadSheet(state: {
    plies: readonly ScoresheetPly[]; unresolved?: readonly string[]; unresolvedFrom?: number | null;
    boxes?: readonly (number[] | null)[]; written?: readonly string[]; pages?: readonly number[];
  }, comments: readonly (string | null)[] = []): void {
    this.isScoresheet.set(true);
    this.plies.set(fromServer(state.plies, comments));
    this.unresolved.set([...(state.unresolved ?? [])]);
    this.unresolvedFrom.set(state.unresolvedFrom ?? null);
    this.boxes.set([...(state.boxes ?? [])]);
    this.sheetEntries.set([...(state.written ?? [])]);
    this.entryPages.set([...(state.pages ?? [])]);
    this.goToFirstUncertain();
  }

  goToFirstUncertain(): void {
    const first = this.plies().findIndex(p => p.uncertain && !p.confirmed);
    if (first >= 0) this.go(first);
  }

  onPhotoLoad(e: Event): void {
    const img = e.target as HTMLImageElement;
    if (img.naturalWidth > 0 && img.naturalHeight > 0) this.photoSize.set({ w: img.naturalWidth, h: img.naturalHeight });
  }

  go(i: number): void {
    this.cursor.set(Math.max(0, Math.min(i, this.legalCount())));
    this.host.moved?.();
  }

  nextUncertain(): void {
    const idx = nextUncertainFrom(this.plies(), this.cursor() + 1);
    if (idx !== null) this.go(idx);
  }

  plyLabel(i: number): string {
    const no = Math.floor(i / 2) + 1;
    return i % 2 === 0 ? `${no}.` : `${no}…`;
  }

  plyClass(i: number): Record<string, boolean> {
    const p = this.plies()[i];
    return {
      uncertain: p.uncertain && !p.confirmed && !p.illegal,
      confirmed: p.confirmed && this.isScoresheet(),
      user: p.match === 'user' && this.isScoresheet(),
      illegal: p.illegal,
      cursor: this.cursor() === i,
    };
  }

  /** Ein Zug am Brett: ersetzt den Halbzug am Cursor (oder fügt davor ein). */
  play(san: string): void {
    this.apply(san, this.mode());
  }

  /** Eine Lesart wählen. Die schon gewählte anzuklicken heißt „passt so" — wie Bestätigen. Danach geht es wie
   *  beim Bestätigen zur nächsten unsicheren Stelle, nicht bloß zum nächsten Halbzug (gewünscht 2026-09-27). */
  choose(o: ScoresheetOption): void {
    const p = this.current();
    if (p && !p.illegal && o.uci === p.uci) { this.confirm(); return; }
    this.apply(o.san, 'replace', true);
  }

  private apply(san: string, mode: 'replace' | 'insert', thenNextUncertain = false): void {
    const i = this.cursor();
    const list = this.plies();
    const old = list[i];
    const written = mode === 'insert' ? '' : old?.written ?? '';
    // Ein Halbzug ohne Eintrag (eingefügt) bleibt ohne; nur Anhängen am Ende verbraucht den nächsten offenen.
    const w = mode === 'insert' ? null : i < list.length ? list[i].w : writtenIndexAt(list, i);
    const comment = mode === 'replace' ? stripSheetNotes(old?.comment) : null;
    const uci = uciOf(san, this.cursorFen());
    if (!uci) return;
    const mine = userPly(san, uci, w, written, comment);
    this.host.changed?.();

    if (!this.isScoresheet()) {
      const tail = mode === 'insert' ? list.slice(i) : list.slice(i + 1);
      this.plies.set(revalidate([...list.slice(0, i), mine, ...tail], this.startFen()));
      this.cursor.set(i + 1);
      return;
    }
    const req = resolveRequest(list, i, mode, san);
    this.reResolve(req, [...list.slice(0, i), mine], mode === 'insert' ? list.slice(i) : list.slice(i + 1), i + 1,
      thenNextUncertain);
  }

  /** Den Halbzug am Cursor streichen (ein doppelt notierter oder erfundener Eintrag). */
  remove(): void {
    const i = this.cursor();
    const list = this.plies();
    if (i >= list.length) return;
    this.host.changed?.();
    if (!this.isScoresheet()) {
      this.plies.set(revalidate([...list.slice(0, i), ...list.slice(i + 1)], this.startFen()));
      return;
    }
    this.reResolve(resolveRequest(list, i, 'delete'), list.slice(0, i), list.slice(i + 1), i);
  }

  /** Ja, dieser Zug stimmt — die Stelle ist nicht mehr unsicher. */
  confirm(): void {
    const i = this.cursor();
    this.plies.update(list => list.map((p, k) => k === i ? { ...p, confirmed: true, uncertain: false } : p));
    this.host.changed?.();
    if (this.uncertainLeft() > 0) this.nextUncertain();
    else this.go(i + 1);
  }

  setComment(text: string): void {
    const i = this.cursor();
    this.plies.update(list => list.map((p, k) => k === i ? { ...p, comment: text.trim() ? text : null } : p));
    this.host.changed?.();
  }

  /**
   * Den Rest ab einer Stelle vom Server neu aufbereiten lassen (Formular-Einträge ab `writtenFrom`). Scheitert
   * das, bleibt der bisherige Rest stehen, soweit er noch legal ist — `fallbackTail`.
   */
  private reResolve(req: { prefix: string[]; writtenFrom: number }, head: EditPly[], fallbackTail: EditPly[],
    nextCursor: number, thenNextUncertain = false): void {
    this.busy.set(true);
    const call = this.host.resolve(req.prefix, req.writtenFrom);
    (this.host.bind ? this.host.bind(call) : call).subscribe({
      next: res => {
        this.plies.set(revalidate([...head, ...fromServer(res.plies)], this.startFen()));
        this.unresolved.set(res.unresolved);
        this.unresolvedFrom.set(res.unresolvedFrom ?? null);
        this.busy.set(false);
        this.go(thenNextUncertain ? nextUncertainFrom(this.plies(), nextCursor) ?? nextCursor : nextCursor);
      },
      error: () => {
        this.busy.set(false);
        this.plies.set(revalidate([...head, ...fallbackTail], this.startFen()));
        this.go(nextCursor);
        this.host.resolveFailed?.();
      },
    });
  }
}

function uciOf(san: string, fen: string): string | null {
  try {
    const m = new Chess(fen).move(san);
    return m.from + m.to + (m.promotion ?? '');
  } catch {
    return null;
  }
}
