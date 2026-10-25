import { signal } from '@angular/core';
import { SheetPgnInput, sheetPgn, sheetPgnFileName } from '../../core/club-format';
import { downloadBlob } from '@rh/shared/download.util';

/** Was die Ausgabe der geprüften Partie braucht — so lässt sie sich ohne TestBed prüfen. */
export interface ScanExportDeps {
  /** Die geprüfte Partie: legale Züge, Namen wie im Formular. */
  input: () => SheetPgnInput;
  /** In RookHubs „Meine Partien" ablegen (nur angemeldet). */
  addToMyGames: (pgn: string) => Promise<{ imported: number; ids: number[] }>;
  /** Für Tests ersetzbar; Vorgabe: Datei herunterladen bzw. Zwischenablage des Browsers. */
  download?: (blob: Blob, name: string) => void;
  copy?: (text: string) => Promise<void>;
}

/**
 * Die geprüfte Partie der Formular-Prüfseite nach draußen: PGN herunterladen, kopieren, zu „Meine Partien" hinzufügen
 * (mit den Namen wie im Formular, ohne „Schwaz").
 */
export class ScanExport {
  readonly adding = signal(false);
  readonly myGameId = signal<number | null>(null);
  readonly pgnMsg = signal<{ text: string; err: boolean } | null>(null);

  constructor(private readonly deps: ScanExportDeps) {}

  downloadPgn(): void {
    const g = this.deps.input();
    (this.deps.download ?? downloadBlob)(new Blob([sheetPgn(g)], { type: 'application/x-chess-pgn' }), sheetPgnFileName(g));
  }

  async copyPgn(): Promise<void> {
    try {
      const text = sheetPgn(this.deps.input());
      await (this.deps.copy ? this.deps.copy(text) : navigator.clipboard.writeText(text));
      this.pgnMsg.set({ text: 'PGN kopiert.', err: false });
    } catch {
      this.pgnMsg.set({ text: 'Kopieren ging nicht — bitte „PGN herunterladen".', err: true });
    }
  }

  /** In RookHubs „Meine Partien" (nur angemeldet) — mit den Namen wie im Formular, ohne „Schwaz". */
  async addToMyGames(): Promise<void> {
    this.adding.set(true);
    this.pgnMsg.set(null);
    try {
      const r = await this.deps.addToMyGames(sheetPgn(this.deps.input()));
      this.myGameId.set(r.ids[0] ?? null);
      this.pgnMsg.set({ text: r.imported ? 'In deinen Partien gespeichert.' : 'Die Partie war schon in deinen Partien.', err: false });
    } catch {
      this.pgnMsg.set({ text: 'Speichern in deinen Partien hat nicht geklappt.', err: true });
    } finally {
      this.adding.set(false);
    }
  }
}
