import { computed, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubDraft, ClubImportResult } from '../../core/club.models';
import { reasonText } from '../../core/club-format';
import { partLabel, pgnPortions, portionNote } from '../../core/pgn-portions';
import { draftKeys, rememberDraftKey } from './club-anon-keys';
import { ImportReview } from './import-review';

/** Was die Liste von außen braucht — so lässt sie sich ohne TestBed prüfen. */
export interface ClubDraftStoreDeps {
  client: ClubClient;
  /** Token des Teilen-Links (ohne Konto) — sonst `null`. */
  share: string | null;
  /** Verwalter sehen zusätzlich die offenen Listen aller. */
  isManager: boolean;
  allDrafts: () => Promise<ClubDraft[]>;
  savedGame: (id: number) => Promise<{ pgn: string }>;
  /** Rückfrage (ConfirmService) — `true` = ja. */
  confirm: (text: string) => Promise<boolean>;
}

/** Wie lange nach der letzten Änderung der Übersicht gespeichert wird (ms) — Tippen und Klicken sollen nicht je Zug senden. */
export const SAVE_DEBOUNCE_MS = 1500;

/**
 * Die PGN-Liste der Hinzufügen-Seite samt ihrem Online-Entwurf (0.595.0, Wunsch: „soll gleich online abgelegt werden —
 * damit ein Admin den Import fertigstellen kann"): Text, Übersicht, Pakete, offene Listen, Ergebnis. Lebt so lange wie
 * die Seite — der Wechsel auf „Partieformular" und zurück verliert nichts.
 */
export class ClubDraftStore {
  private readonly client: ClubClient;
  private readonly share: string | null;

  readonly replaceClub = signal(true);
  readonly pgn = signal('');
  readonly previewing = signal(false);
  readonly studyUrl = signal('');
  readonly loadingStudy = signal(false);
  readonly importError = signal<string | null>(null);
  /** ChessBase: was aus der Datenbank wurde (gelesen, übersprungen) — steht über der Übersicht. */
  readonly dbNote = signal<string | null>(null);
  /** In Pakete geteilt: wie viele, und ob die übrigen als offene Listen liegen. */
  readonly portionNote = signal<string | null>(null);
  readonly readingDb = signal(false);
  /** Eine PGN-Datei wird gelesen und in die Übersicht geführt (von der Wahl bis die Übersicht steht). */
  readonly readingFile = signal(false);
  /** Läuft schon eine Liste (Datei, Datenbank, Studie oder Übersicht)? Dann startet kein zweiter Weg daneben — sonst
   *  liefen zwei Vorschauen parallel und legten Pakete doppelt bzw. einen verwaisten Entwurf ab (UX-037-Nacharbeit). */
  readonly busy = computed(() => this.readingFile() || this.readingDb() || this.loadingStudy() || this.previewing());
  readonly review = signal<ImportReview | null>(null);
  readonly result = signal<ClubImportResult | null>(null);

  /** Der Entwurf der Liste in der Übersicht; `pgn` = der Text, zu dem er gehört (eine neu eingefügte Liste ist ein neuer). */
  readonly draft = signal<(ClubDraft & { pgn: string }) | null>(null);
  readonly drafts = signal<ClubDraft[]>([]);
  readonly othersDrafts = signal<ClubDraft[]>([]);
  readonly resuming = signal(false);
  /** Stellt ein Verwalter die Liste eines anderen fertig: dessen Name (oder „einem Teilen-Link"). */
  readonly resumedFrom = signal<string | null>(null);
  /** Woher der Text kam — steht in der Liste der offenen Entwürfe. */
  private source = 'text';
  private label: string | null = null;
  private loadedText: string | null = null;
  /** Schon importierte Partien des Entwurfs (aus früheren Läufen) — die neuen kommen dazu. */
  private importedBefore: number[] = [];
  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  private disposed = false;

  constructor(private readonly deps: ClubDraftStoreDeps) {
    this.client = deps.client;
    this.share = deps.share;
  }

  /** Jede Korrektur in der Übersicht landet (gedrosselt) im Entwurf — auch die, die dann niemand mehr importiert. */
  scheduleSave(): void {
    if (this.saveTimer) clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => void this.flushSave(), SAVE_DEBOUNCE_MS);
  }

  /** Seite geht: eine wartende Speicherung jetzt noch absetzen, Pakete nicht mehr nachmelden. */
  dispose(): void {
    this.disposed = true;
    if (this.saveTimer) { clearTimeout(this.saveTimer); void this.flushSave(); }
  }

  private async flushSave(): Promise<void> {
    this.saveTimer = null;
    const d = this.draft(), rv = this.review();
    if (!d || !rv) return;
    try { await this.client.saveDraft(d.ref, { state: rv.snapshot() }); } catch { /* nächste Änderung versucht es wieder */ }
  }

  /** Die offenen Entwürfe — die eigenen, und für Verwalter die aller anderen. */
  async loadDrafts(): Promise<void> {
    try {
      const keys = this.share ? draftKeys(this.share) : [];
      const list = await this.client.drafts(keys);
      if (this.share) for (const k of keys) if (!list.some(d => d.ref === k)) rememberDraftKey(this.share, k, false);
      this.drafts.set(list);
      if (this.deps.isManager) this.othersDrafts.set((await this.deps.allDrafts()).filter(d => !d.mine));
    } catch { /* Beiwerk — die Seite geht auch ohne */ }
  }

  draftTitle(d: ClubDraft): string {
    const src = d.source === 'lichess' ? 'Lichess-Studie' : d.source === 'rookhub' ? 'aus RookHub' : d.source === 'datei' ? 'Datei'
      : d.source === 'chessbase' ? 'ChessBase' : 'eingefügt';
    return `${d.label ?? src} (${d.gameCount} ${d.gameCount === 1 ? 'Partie' : 'Partien'})`;
  }

  draftState(d: ClubDraft): string {
    return d.importedCount ? `${d.importedCount} von ${d.gameCount} importiert` : 'noch nichts importiert';
  }

  /** Einen Entwurf wieder aufnehmen: Text laden, Übersicht neu lesen (für den Einreicher), Korrekturen wiederherstellen. */
  async resume(d: ClubDraft): Promise<void> {
    this.resuming.set(true);
    this.importError.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    this.result.set(null);
    try {
      const full = await this.client.draft(d.ref);
      this.pgn.set(full.pgn);
      this.importedBefore = full.imported;
      this.draft.set({ ...d, pgn: full.pgn });
      this.resumedFrom.set(this.foreignOwner(d));
      const preview = await this.client.preview(full.pgn, this.share ? null : full.id);
      this.review.set(ImportReview.restore(preview, full.state, this.replaceClub()));
    } catch {
      this.importError.set('Die Liste ließ sich nicht öffnen — vielleicht hat sie inzwischen jemand anderes fertiggestellt.');
      void this.loadDrafts();
    } finally {
      this.resuming.set(false);
    }
  }

  /** Die Liste eines anderen (nur Verwalter sehen die): dessen Name bzw. „einem Teilen-Link" — sonst null. */
  private foreignOwner(d: ClubDraft): string | null {
    return d.mine || !this.othersDrafts().some(o => o.ref === d.ref) ? null : d.viaShareLink ? 'einem Teilen-Link' : d.owner ?? 'jemand anderem';
  }

  async discardDraft(d: ClubDraft): Promise<void> {
    const who = this.foreignOwner(d);
    if (!(await this.deps.confirm(`Liste „${this.draftTitle(d)}"${who ? ` von ${who}` : ''} verwerfen? Schon importierte Partien bleiben, der Rest wird gelöscht.`))) return;
    try {
      await this.client.deleteDraft(d.ref);
      if (this.share) rememberDraftKey(this.share, d.ref, false);
    } catch { /* schon weg */ }
    void this.loadDrafts();
  }

  /**
   * „Schließen" in der Übersicht: nur die Übersicht geht zu, der Entwurf bleibt samt Korrekturen unter „Deine offenen
   * Listen" (bzw. „Offene Listen anderer") liegen. Früher hieß der Knopf „Verwerfen" und löschte den Entwurf ohne
   * Rückfrage — beim Fertigstellen auch den eines anderen. Endgültig löschen geht nur noch aus der Liste, mit Rückfrage.
   */
  async closeCurrent(): Promise<void> {
    const d = this.draft(), rv = this.review();
    if (!d && rv && !(await this.deps.confirm('Die Liste liegt nicht online — schließt du die Übersicht, gehen deine Korrekturen verloren. Trotzdem schließen?'))) return;
    const unsaved = !!this.saveTimer;
    this.review.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    this.resetDraft();
    // Der Text liegt im Entwurf — stünde er noch im Feld, legte ein erneutes „Partien prüfen" einen zweiten Entwurf an.
    if (d) this.pgn.set('');
    // Die letzte Korrektur wartet vielleicht noch auf die Drossel — jetzt speichern, sonst fehlt sie beim Weitermachen.
    if (d && rv && unsaved) {
      try { await this.client.saveDraft(d.ref, { state: rv.snapshot() }); } catch { /* bleibt beim letzten Stand */ }
    }
    void this.loadDrafts();
  }

  /** Nach jeder gespeicherten Portion — damit ein Abbruch nicht vergisst, was schon drin ist. */
  onSaved(indices: number[]): void {
    const d = this.draft();
    if (!d) return;
    const all = [...new Set([...this.importedBefore, ...indices])];
    void this.client.saveDraft(d.ref, { imported: all }).catch(() => undefined);
  }

  private resetDraft(): void {
    if (this.saveTimer) { clearTimeout(this.saveTimer); this.saveTimer = null; }
    this.draft.set(null);
    this.resumedFrom.set(null);
    this.importedBefore = [];
  }

  /** Eine Partie aus RookHub (⋮ → „In die Vereins-Datenbank", Wunsch 2026-09-28) — gleich in die Übersicht, wie ein Upload. */
  async loadSavedGame(id: number): Promise<void> {
    this.importError.set(null);
    try {
      this.pgn.set((await this.deps.savedGame(id)).pgn);
      this.loaded(this.pgn(), 'rookhub', `Partie ${id} aus RookHub`);
    } catch {
      this.importError.set('Die Partie aus RookHub ließ sich nicht laden — bist du hier mit demselben Konto angemeldet?');
      return;
    }
    await this.startPreview();
  }

  /**
   * Die Übersicht öffnen. Eine Liste über dem, was die Übersicht auf einmal nimmt (500 Partien, 5 Mio. Zeichen), wird
   * VORHER in Pakete geteilt (0.598.1, Wunsch „auch beim PGN-Upload alle einlesen und dann in Paketen anbieten"): das erste
   * geht in die Übersicht, die übrigen werden danach als offene Listen abgelegt — egal, woher die Liste kam.
   */
  async startPreview(): Promise<void> {
    this.previewing.set(true);
    this.importError.set(null);
    this.result.set(null);
    this.portionNote.set(null);
    let rest: { pgn: string; label: string | null }[] = [];
    let source = 'text';
    const parts = pgnPortions(this.pgn());
    if (parts.length > 1) {
      source = this.pgnSource(this.pgn());
      const label = this.pgnLabel(this.pgn());
      rest = parts.slice(1).map((pgn, i) => ({ pgn, label: partLabel(label, i + 1, parts.length) }));
      this.pgn.set(parts[0]);
      this.loaded(parts[0], source, partLabel(label, 0, parts.length));
      this.portionNote.set(portionNote(parts.length, null));
    }
    try {
      // Sofort online ablegen (egal, woher der Text kam) — geht das nicht (Deckel, Netz), läuft es ohne Entwurf weiter.
      const pgn = this.pgn();
      const d = await this.ensureDraft(pgn);
      this.review.set(new ImportReview(await this.client.preview(pgn, this.share ? null : d?.id ?? null), this.replaceClub()));
      if (rest.length) void this.parkPortions(rest, source, parts.length);
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason)
        : e?.status === 404 && this.share ? 'Dieser Link ist abgelaufen.'
        : e?.status === 403 ? 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).' : 'Lesen hat nicht geklappt.');
    } finally {
      this.previewing.set(false);
    }
  }

  /** Der Entwurf zu diesem Text — der bisherige, wenn er dazu gehört, sonst ein neuer (der alte geht); `null` = keiner. */
  private async ensureDraft(pgn: string): Promise<(ClubDraft & { pgn: string }) | null> {
    const d = this.draft();
    if (d && d.pgn === pgn) return d;
    if (d) { try { await this.client.deleteDraft(d.ref); } catch { /* egal */ } }
    this.resetDraft();
    try {
      const created = await this.client.createDraft(pgn, this.pgnSource(pgn), this.pgnLabel(pgn));
      if (this.share && created.key) rememberDraftKey(this.share, created.key);
      const fresh = { ...created, pgn };
      this.draft.set(fresh);
      return fresh;
    } catch { return null; }
  }

  /** Die übrigen Pakete als offene Listen ablegen — erst NACH der Übersicht, damit das erste auch bei vollem Deckel seinen
   * Entwurf hat. Am Deckel (20 je Konto, 5 ohne) hört es auf und sagt, wie viele fehlen. */
  private async parkPortions(rest: { pgn: string; label: string | null }[], source: string, parts: number): Promise<void> {
    let parked = 0;
    for (const p of rest) {
      try {
        const created = await this.client.createDraft(p.pgn, source, p.label);
        if (this.share && created.key) rememberDraftKey(this.share, created.key);
        parked++;
      } catch { break; }
    }
    if (this.disposed) return;
    if (this.portionNote()) this.portionNote.set(portionNote(parts, parked));
    void this.loadDrafts();
  }

  done(r: ClubImportResult): void {
    this.result.set(r);
    this.review.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    if (r.added) this.pgn.set('');
    // Fertig importiert: der Entwurf (samt Rohtext mit den Namen) geht.
    const d = this.draft();
    this.resetDraft();
    if (d) {
      void this.client.deleteDraft(d.ref).catch(() => undefined).then(() => this.loadDrafts());
      if (this.share) rememberDraftKey(this.share, d.ref, false);
    }
  }

  /** Woher der Text kam (Datei, Studie, RookHub) gilt nur, solange er unverändert ist — wer danach tippt, hat eingefügt. */
  loaded(text: string, source: string, label: string | null): void {
    this.loadedText = text;
    this.source = source;
    this.label = label;
  }
  private pgnSource(pgn: string): string { return pgn === this.loadedText ? this.source : 'text'; }
  private pgnLabel(pgn: string): string | null { return pgn === this.loadedText ? this.label : null; }
}
