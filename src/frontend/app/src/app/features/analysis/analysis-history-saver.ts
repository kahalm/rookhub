import { Observable } from 'rxjs';
import { AnalysisHistoryEntry, SaveAnalysisHistoryRequest } from './analysis-history.service';
import { AnalysisNode, toDto } from './analysis-tree';

/** Analyse-Verlauf (0.603.0): so lange nach der letzten Änderung, bis der Stand zum Server geht. */
export const HISTORY_SAVE_MS = 1500;

export interface AnalysisHistorySaverOptions {
  save: (req: SaveAnalysisHistoryRequest) => Observable<AnalysisHistoryEntry>;
  /** Nur angemeldet wird gespeichert. */
  loggedIn: () => boolean;
  /** Der Zugbaum und die Stellung, die gerade auf dem Brett steht. */
  snapshot: () => { root: AnalysisNode; current: AnalysisNode };
  /** Grundstellung — ein leerer Baum darauf ist keine Analyse und wird nicht gespeichert. */
  blankFen: string;
}

/**
 * Speichert die Analyse des Analysebretts gedrosselt in den Verlauf (0.603.0): Zugbaum samt Varianten und Sternen, der
 * Zug auf dem Brett und der Titel. Die erste Antwort vergibt die Kennung, danach wird unter ihr weiter gespeichert; eine
 * neue Analyse (`newSession`) beginnt ohne Kennung. Antworten einer verlassenen Analyse verfallen.
 */
export class AnalysisHistorySaver {
  /** Kennung des Verlauf-Eintrags dieser Analyse (null = neue Analyse, der Server vergibt sie beim ersten Speichern). */
  id: number | null = null;
  /** Titel aus den PGN-Kopfdaten („Weiß – Schwarz"), sonst leer. */
  private title: string | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private saving = false;
  private again = false;
  private lastSig = '';
  /** Zählt die Analysen dieser Seite — eine Antwort, die nach einem Wechsel eintrifft, gehört nicht mehr dazu. */
  private session = 0;

  constructor(private readonly opts: AnalysisHistorySaverOptions) {}

  /** Gedrosselt speichern — nur angemeldet, und nur, was eine Analyse IST (Züge, eigene Stellung oder Sterne). */
  schedule(): void {
    if (!this.opts.loggedIn()) return;
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.timer = null; this.saveNow(); }, HISTORY_SAVE_MS);
  }

  /** Ein wartendes Speichern sofort ausführen (Seite verlassen, Analyse wechseln). */
  flush(): void {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; this.saveNow(); }
  }

  /** Eine neue Analyse beginnt (Zurücksetzen, FEN/PGN/Stellung geladen): der bisherige Stand geht noch raus, dann
   *  ohne Kennung weiter (der neue Baum bringt keine Sterne mit). */
  newSession(title: string | null = null): void {
    this.flush();
    this.session++;
    this.id = null;
    this.title = title;
    this.lastSig = '';
  }

  /** Ein Eintrag des Verlaufs kommt aufs Brett: weiter speichern unter DERSELBEN Kennung. Nach dem Aufbau des Baums
   *  `markSaved()` aufrufen. */
  openEntry(e: AnalysisHistoryEntry): void {
    this.flush();
    this.session++;
    this.id = e.id;
    this.title = e.title;
  }

  /** Der Stand auf dem Brett liegt schon so im Verlauf (eben geladen). */
  markSaved(): void {
    this.lastSig = this.signature();
  }

  /** Was gespeichert würde, als Vergleichswert — gleich = nichts zu tun. */
  private signature(): string {
    const { root, current } = this.opts.snapshot();
    const dto = toDto(root, current);
    return JSON.stringify([root.fen, dto.tree, dto.current, this.title ?? '']);
  }

  private saveNow(): void {
    if (!this.opts.loggedIn()) return;
    const { root, current: node } = this.opts.snapshot();
    const startFen = root.fen;
    if (root.children.length === 0 && startFen === this.opts.blankFen && !root.starred) return;
    const { tree, current } = toDto(root, node);
    const sig = JSON.stringify([startFen, tree, current, this.title ?? '']);
    if (sig === this.lastSig) return;
    // Läuft noch ein Speichern (ohne Kennung), würde ein zweites einen zweiten Eintrag anlegen — danach nachholen.
    if (this.saving) { this.again = true; return; }
    this.saving = true;
    const session = this.session;
    this.opts.save({ id: this.id, startFen, title: this.title, tree, current }).subscribe({
      next: e => {
        this.saving = false;
        // Nur übernehmen, wenn inzwischen keine NEUE Analyse begonnen hat.
        if (this.session === session) { this.id = e.id; this.lastSig = sig; }
        if (this.again) { this.again = false; this.saveNow(); }
      },
      error: () => { this.saving = false; this.again = false; },   // Hintergrund: der nächste Zug versucht es wieder
    });
  }
}
