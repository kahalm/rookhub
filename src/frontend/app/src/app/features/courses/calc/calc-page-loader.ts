import { Subscription } from 'rxjs';
import { applyUci, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { CalcChapterModel } from './calc-chapter-model';
import { chapterGroupLabel, pickChapterIndex } from './calc-chapters.util';
import { CalcBackend, CalcBook, CalcPosition } from './calculation.service';

/** Was das Laden von der Kalkulations-Seite braucht (alles live gelesen). */
export interface CalcPageLoaderHost {
  backend(): CalcBackend;
  bookId(): number;
  /** Angefragte Sprache der Kurs-Texte. */
  lang(): string;
  chapters: CalcChapterModel;
  enterChapter(chapterIndex: number, requestedPositionId: number | null, updateUrl: boolean): void;
  /** Geladene Stellung aufs Brett (Baum, Uhr, Bewertung, Speicher). */
  applyPosition(pos: CalcPosition): void;
  stopTraining(): void;
  /** Abo an die Lebensdauer der Seite hängen. */
  track(sub: Subscription): void;
}

/**
 * Ausgangsstellung: Header-FEN plus den (nicht lösungsrelevanten) Vorlauf `setupMoves`.
 * Scheitert das — illegale Muster-FEN oder unspielbarer Zug —, bleibt die Header-FEN stehen und
 * die Zug-Eingabe wird gesperrt (`illegal`; das Brett selbst rendert auch illegale Stellungen).
 */
export function calcStartFen(pos: CalcPosition): { fen: string; illegal: boolean } {
  const chess = tryLoadFen(pos.fen);
  if (!chess) return { fen: pos.fen, illegal: true };
  const setup = (pos.setupMoves || '').split(' ').filter(m => m.length >= 4);
  for (const uci of setup) {
    try { applyUci(chess, uci); }
    catch { return { fen: pos.fen, illegal: true }; }
  }
  return { fen: chess.fen(), illegal: false };
}

/**
 * Laden der Kalkulations-Seite (ohne Angular): das Buch (Sprungliste, Kapitel, Einstiegskapitel), die einzelne
 * Stellung (mit Epoche gegen überholte Antworten) und das Nachladen der Texte nach einem Sprachwechsel.
 */
export class CalcPageLoader {
  book: CalcBook | null = null;
  position: CalcPosition | null = null;
  loading = true;
  loadError = false;
  /**
   * Kapitel-Wunsch aus `?chapter=` (Kurz-URL `/{slug}/{kapitel}`) — er WÄHLT das Kapitel vor,
   * filtert aber nichts mehr weg: gewechselt werden kann weiterhin zu jedem anderen Kapitel.
   * Was in der URL stand, kann anders geschrieben sein als der Kapitelname im Buch.
   */
  requestedChapter: string | null = null;
  /** Entwertet überholte Ladevorgänge (siehe loadPosition). */
  private loadEpoch = 0;

  constructor(private readonly host: CalcPageLoaderHost) {}

  loadBook(requestedPositionId: number | null): void {
    const h = this.host;
    this.loading = true;
    this.loadError = false;
    h.track(h.backend().getBook(h.bookId(), h.lang()).subscribe({
      next: book => {
        this.book = book;
        h.chapters.setBook(book);
        if (h.chapters.positions.length === 0) { this.loading = false; return; }
        // Erst das KAPITEL, dann die Stellung darin — der Modus arbeitet ein Kapitel durch.
        h.enterChapter(pickChapterIndex(h.chapters.groups, this.requestedChapter, requestedPositionId), requestedPositionId, false);
      },
      error: () => { this.loading = false; this.loadError = true; },
    }));
  }

  /**
   * FALLE: bei schnellem Weiterklicken laufen zwei Ladevorgänge parallel und können
   * out-of-order eintreffen — die ältere Antwort würde Brett/Baum auf die VORHERIGE Stellung
   * setzen, während Index/URL/Sprungliste schon auf der neuen stehen (Eingaben landeten dann
   * unter der falschen Stellung). Der Epoch-Zähler entwertet jede überholte Antwort.
   */
  loadPosition(bookPuzzleId: number): void {
    const h = this.host;
    this.loading = true;
    const epoch = ++this.loadEpoch;
    h.track(h.backend().getPosition(bookPuzzleId, h.lang()).subscribe({
      next: pos => {
        if (epoch !== this.loadEpoch) return;
        // Ein früherer Fehlschlag (Funkloch, 502 beim Deploy) darf nicht kleben bleiben: sonst
        // bliebe `canTrain` für den Rest des Besuchs false, obwohl diese Stellung geladen ist.
        this.loadError = false;
        this.position = pos;
        h.applyPosition(pos);
        this.loading = false;
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        this.loading = false;
        this.loadError = true;
        // Beim WEITERblättern stünde sonst weiter das Brett der VORHERIGEN Stellung da, während
        // Index, URL und Sprungliste schon auf der neuen stehen — dieselbe Verwechslung, gegen die
        // oben der Epoch-Zähler schützt, nur über den Fehlerpfad. Die Arbeit an der alten Stellung
        // ist zu diesem Zeitpunkt bereits abgeflossen (`leaveCurrentPosition` vor dem Laden), es
        // geht also nichts verloren; ohne Stellung zeigt die Vorlage die Fehlermeldung.
        this.position = null;
        // Und ohne Stellung wird nicht gemessen: sonst tickte die Kapitel-Uhr weiter, während man
        // auf eine Fehlermeldung schaut. `stopTraining` sichert die bis hierhin gemessene Zeit.
        h.stopTraining();
      },
    }));
  }

  /**
   * Überschriften und Aufgabentext in der gewählten Sprache holen. Übernommen werden nur die TEXTE
   * (Labels, Kommentar, Sprache, maschinell?) — die Stellungsliste trägt daneben Festlegung, Zeit und
   * Bewertung, die hier gerade in Arbeit sein können.
   */
  refreshTexts(): void {
    const h = this.host;
    const lang = h.lang();
    h.track(h.backend().getBook(h.bookId(), lang).subscribe({
      next: book => {
        const byId = new Map(book.positions.map(p => [p.id, p]));
        for (const item of h.chapters.positions) {
          const fresh = byId.get(item.id);
          if (!fresh) continue;
          item.titleLabel = fresh.titleLabel ?? null;
          item.chapterLabel = fresh.chapterLabel ?? null;
        }
        for (const g of h.chapters.groups) g.label = chapterGroupLabel(g.items, g.chapter);
        const pos = this.position;
        if (!pos) return;
        h.track(h.backend().getPosition(pos.id, lang).subscribe({
          next: fresh => {
            if (this.position?.id !== fresh.id) return;   // inzwischen weitergeblättert
            Object.assign(this.position, {
              comment: fresh.comment,
              titleLabel: fresh.titleLabel ?? null,
              chapterLabel: fresh.chapterLabel ?? null,
              commentLanguage: fresh.commentLanguage ?? null,
              commentMachine: !!fresh.commentMachine,
            });
          },
          error: () => { /* der Text bleibt, wie er war */ },
        }));
      },
      error: () => { /* Beiwerk — die Arbeit an der Stellung geht weiter */ },
    }));
  }
}
