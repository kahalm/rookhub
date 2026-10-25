import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ScanRef } from '../../core/club.models';

/** Was das Zusammenführen braucht — so lässt es sich ohne TestBed prüfen. */
export interface ScanMergeDeps {
  /** Die Einlesung, die gerade geprüft wird. */
  scanRef: () => string;
  scans: () => Promise<ScanRef[]>;
  merge: (into: string, from: string) => Promise<unknown>;
  /** Rückfrage (ConfirmService) — `true` = ja. */
  confirm: (text: string) => Promise<boolean>;
  /** Nach dem Zusammenführen: die Einlesung neu laden (sie wird neu gelesen). */
  reload: () => void;
}

/**
 * Zwei Fotos derselben Seite getrennt eingelesen (0.736.0, Wunsch 2026-10-10) → die andere Einlesung hier dazunehmen:
 * ihre Fotos werden weitere Fotos dieser Einlesung, sie selbst wird geschlossen, die Partie neu gelesen.
 */
export class ScanMerge {
  /** Andere offene Einlesungen zur Wahl — `null` = Auswahl zu. */
  readonly mergeCandidates = signal<ScanRef[] | null>(null);
  readonly merging = signal(false);
  readonly mergeError = signal<string | null>(null);

  constructor(private readonly deps: ScanMergeDeps) {}

  async openMerge(): Promise<void> {
    this.mergeError.set(null);
    try {
      const list = await this.deps.scans();
      this.mergeCandidates.set(list.filter(x => x.ref !== this.deps.scanRef() && x.scan.status !== 'running'));
    } catch {
      this.mergeError.set('Die Einlesungen ließen sich gerade nicht laden.');
    }
  }

  candidateLabel(c: ScanRef): string {
    const names = c.scan.white || c.scan.black ? `${c.scan.white ?? '?'} – ${c.scan.black ?? '?'}` : (c.scan.fileName ?? 'Formular');
    const moves = c.scan.moveCount ? ` · ${c.scan.moveCount} Halbzüge` : '';
    return `#${c.ref} · ${names}${moves}`;
  }

  async merge(c: ScanRef): Promise<void> {
    const ok = await this.deps.confirm(`Einlesung #${c.ref} hier dazunehmen? Ihre Fotos werden weitere Fotos dieser `
      + 'Einlesung, sie selbst wird geschlossen, und die Partie wird mit allen Fotos neu gelesen — Änderungen hier gehen dabei verloren.');
    if (!ok) return;
    this.merging.set(true);
    this.mergeError.set(null);
    try {
      await this.deps.merge(this.deps.scanRef(), c.ref);
      this.mergeCandidates.set(null);
      this.deps.reload();
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? (err.error as { reason?: string } | null)?.reason : undefined;
      this.mergeError.set(reason === 'tooManyViews' ? 'Zu viele Fotos für diese Seite (höchstens vier).'
        : reason === 'busy' ? 'Eine der beiden wird gerade gelesen — bitte kurz warten.'
        : 'Zusammenführen hat nicht geklappt.');
    } finally {
      this.merging.set(false);
    }
  }
}
