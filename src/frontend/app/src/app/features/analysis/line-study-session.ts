import { Subscription, timer } from 'rxjs';
import { AnalysisNode, fromDto, toDto } from './analysis-tree';
import { LineStudy, LineStudyEvals, LineStudyService } from './line-study.service';
import { LineComparison, NodeEval, compareLines, flatNodes } from './line-study.util';
import { MOVE_CLASS_COLORS, formatEval } from '../games/game-review.util';
import { MOVE_CLASS_SYMBOLS } from '../games/move-badge.util';

/** Was die Sitzung vom Analysebrett braucht. */
export interface LineStudyHost {
  root(): AnalysisNode;
  current(): AnalysisNode;
  /** Gespeicherten Baum aufs Brett legen (beim Wiederöffnen). */
  setTree(root: AnalysisNode, current: AnalysisNode): void;
  /** Die Engine wurde eingeschaltet — das Brett zeigt ab jetzt wieder Bewertungen. */
  revealed(): void;
  /** Zustand geändert (Zähler, Bewertungen am Baum) — neu zeichnen. */
  changed(): void;
  /** Speichern gescheitert (F2-003) — die Änderung bleibt offen, der nächste Zug bzw. das Verlassen versucht es erneut. */
  saveFailed(err: unknown): void;
}

/**
 * „Ohne Engine analysieren" (0.855.0) am Analysebrett: der eigene Baum zu EINER Partie wird gedrosselt gespeichert
 * (`PUT /api/line-studies`), der Server rechnet jede Stellung im Hintergrund. Solange die Engine aus ist, gibt es nur den
 * Fortschritt; nach „Engine einschalten" kommen die Bewertungen je Knoten (`evals`, Indizes des zuletzt GESPEICHERTEN Baums —
 * deshalb hält die Sitzung dessen Knoten fest) samt Vergleich mit dem besten Zug.
 */
export class LineStudySession {
  static readonly SaveDelayMs = 1500;
  static readonly PollMs = 15000;

  id: number | null = null;
  revealed = false;
  analyzed = 0;
  total = 0;
  comparison: LineComparison | null = null;
  /** Laden des gespeicherten Baums gescheitert (nicht 404, F2-002): Speichern ist gesperrt, bis {@link open} gelingt. */
  loadError: unknown = null;

  private saveSub?: Subscription;
  private pollSub?: Subscription;
  private dirty = false;
  private saving = false;
  /** Knoten in der Reihenfolge des zuletzt gespeicherten Baums — die Indizes der Bewertungen zeigen hierauf. */
  private savedFlat: AnalysisNode[] = [];
  private savedRoot: AnalysisNode | null = null;
  private destroyed = false;
  /** „Mit Engine“ gewählt, bevor der Baum eine Kennung hat — aufdecken, sobald er gespeichert bzw. geladen ist. */
  private revealPending = false;
  /** Erst nach einem gelungenen Laden (oder 404 = neu) darf gespeichert werden — sonst ersetzte die nackte Partie auf dem
   *  Brett den gespeicherten Baum (F2-002). */
  private loaded = false;
  /** „Engine einschalten“ kam während eines laufenden Speicherns — nach dem nächsten gelungenen Speichern ausführen (F2-004). */
  private afterSave?: () => void;

  constructor(private readonly api: LineStudyService, private readonly host: LineStudyHost,
              readonly gameKey: string, private readonly gameUcis: readonly string[], private readonly title: string | null) {}

  /**
   * Vorhandenen Baum holen (dann aufs Brett) oder — NUR bei 404 — den Partie-Baum als neuen anlegen (`createOnly`: gibt es
   * ihn inzwischen doch, antwortet der Server 409 und die Sitzung lädt neu). Jeder andere Ladefehler sperrt das Speichern
   * und steht in {@link loadError}; ein erneuter Aufruf ist der Neuversuch (F2-002).
   */
  open(): void {
    if (this.destroyed) return;
    this.loadError = null;
    this.api.get(this.gameKey).subscribe({
      next: s => {
        if (this.destroyed) return;
        this.loaded = true;
        this.dirty = false;
        const { root, nodes } = fromDto(s.startFen, s.tree);
        const current = s.current >= 0 ? nodes[s.current] ?? root : root;
        this.host.setTree(root, current);
        this.accept(s, root);
        if (this.revealed) this.host.revealed();
        this.refreshEvals();
      },
      error: err => {
        if (this.destroyed) return;
        if ((err as { status?: number } | null)?.status === 404) {
          this.loaded = true;
          this.saveNow(undefined, true);
          return;
        }
        this.loadError = err ?? true;
        this.host.changed();
      },
    });
  }

  /** Laden gescheitert, Speichern gesperrt (F2-002). */
  get loadFailed(): boolean { return this.loadError != null; }

  /** Nach jeder Änderung am Baum (gedrosselt). */
  schedule(): void {
    if (this.destroyed) return;
    this.dirty = true;
    if (!this.loaded) return;   // F2-002: ohne geladenen Stand nie speichern
    this.saveSub?.unsubscribe();
    this.saveSub = timer(LineStudySession.SaveDelayMs).subscribe(() => this.saveNow());
  }

  /** Beim Verlassen: ausstehende Änderung sofort schicken. */
  flush(): void {
    if (this.dirty && this.loaded && !this.destroyed) this.saveNow();
  }

  /** „Engine einschalten": erst den letzten Stand speichern, dann aufdecken und die Bewertungen holen. */
  reveal(): void {
    if (this.id == null) return;
    this.saveSub?.unsubscribe();
    const go = () => this.api.reveal(this.id!).subscribe({
      next: s => {
        this.accept(s, this.savedRoot);
        this.host.revealed();
        this.refreshEvals();
      },
      error: () => { /* still: der Knopf bleibt, ein zweiter Klick versucht es erneut */ },
    });
    if (this.dirty || this.saving) this.saveNow(go); else go();
  }

  /** Aufdecken, sobald möglich (0.898.0: Brett startet „Mit Engine“ bzw. Kopf-Schalter vor dem ersten Speichern). */
  revealWhenReady(): void {
    if (this.revealed) return;
    if (this.id != null) this.reveal(); else this.revealPending = true;
  }

  destroy(): void {
    this.flush();
    this.destroyed = true;
    this.saveSub?.unsubscribe();
    this.pollSub?.unsubscribe();
  }

  private saveNow(after?: () => void, createOnly = false): void {
    if (!this.loaded) return;
    if (this.saving) {
      // Läuft schon ein Speichern: offen halten und den Rückruf vormerken (F2-004) — er kommt nach dem nächsten Speichern.
      if (after) this.afterSave = after; else this.dirty = true;
      return;
    }
    const root = this.host.root();
    const { tree, current } = toDto(root, this.host.current());
    const flat = flatNodes(root);
    this.dirty = false;
    this.saving = true;
    const req = { gameKey: this.gameKey, startFen: root.fen, title: this.title, tree, current, ...(createOnly ? { createOnly: true } : {}) };
    this.api.save(req).subscribe({
      next: s => {
        this.saving = false;
        this.savedFlat = flat;
        this.accept(s, root);
        if (this.dirty) {
          // Weitere Änderung kam dazwischen: sofort hinterher, der vorgemerkte Rückruf danach.
          if (after) this.afterSave = after;
          if (this.destroyed) this.saveNow(); else this.schedule();
          return;
        }
        const pending = this.afterSave;
        this.afterSave = undefined;
        after?.();
        if (pending && pending !== after) pending();
        if (this.revealed) this.refreshEvals();
      },
      error: err => {
        this.saving = false;
        if (createOnly && (err as { status?: number } | null)?.status === 409) {
          // Zwischen 404 und Anlegen entstand der Baum (anderer Tab): den gespeicherten laden statt ihn zu ersetzen.
          this.loaded = false;
          if (after || this.afterSave) { this.afterSave = undefined; this.revealPending = true; }
          this.open();
          return;
        }
        // F2-003: die Änderung bleibt offen — der nächste Zug (schedule) bzw. das Verlassen (flush) versucht es erneut.
        this.dirty = true;
        if (after) this.afterSave = after;
        this.host.saveFailed(err);   // auch nach dem Verlassen: die Meldung erscheint auf der nächsten Seite
      },
    });
  }

  private accept(s: LineStudy, root: AnalysisNode | null): void {
    this.id = s.id;
    this.revealed = s.revealed;
    this.analyzed = s.analyzed;
    this.total = s.total;
    if (root && root !== this.savedRoot) {
      this.savedRoot = root;
      if (this.savedFlat.length === 0 || this.savedFlat[0]?.parent !== root) this.savedFlat = flatNodes(root);
    }
    this.host.changed();
    if (this.revealPending && this.id != null) {
      this.revealPending = false;
      if (!this.revealed) this.reveal();
    }
  }

  /** Fortschritt (und nach dem Einschalten die Bewertungen) holen; solange noch gerechnet wird, alle 15 s wieder. */
  private refreshEvals(): void {
    if (this.id == null || this.destroyed) return;
    this.pollSub?.unsubscribe();
    this.api.evals(this.id).subscribe({
      next: e => {
        if (this.destroyed) return;
        this.applyEvals(e);
        if (e.analyzed < e.total) this.pollSub = timer(LineStudySession.PollMs).subscribe(() => this.refreshEvals());
      },
      error: () => { /* still: Fortschritt ist ein Hintergrund-Feed, der nächste Abruf kommt mit der nächsten Änderung */ },
    });
  }

  private applyEvals(e: LineStudyEvals): void {
    this.analyzed = e.analyzed;
    this.total = e.total;
    this.revealed = e.revealed;
    if (e.revealed && this.savedRoot) {
      const evalOf = new Map<AnalysisNode, NodeEval>();
      for (const n of e.nodes) {
        const node: AnalysisNode | undefined = n.index === -1 ? this.savedRoot : this.savedFlat[n.index];
        if (!node) continue;
        const score: NodeEval = { cp: n.cp ?? null, mate: n.mate ?? null, bestUci: n.bestUci ?? null };
        evalOf.set(node, score);
        if (node !== this.savedRoot) node.evalText = formatEval(score);
      }
      this.comparison = compareLines(this.savedRoot, evalOf, this.gameUcis);
      // Das Urteil steht am Zug in der Zugliste (Wunsch 2026-10-27: „besser hinter jeden Zug die Eval schreiben" statt Liste oben).
      for (const node of flatNodes(this.savedRoot)) delete node.verdict;
      for (const v of this.comparison.verdicts) {
        v.node.verdict = { symbol: MOVE_CLASS_SYMBOLS[v.cls], color: MOVE_CLASS_COLORS[v.cls], better: v.bestSan };
      }
    }
    this.host.changed();
  }
}
