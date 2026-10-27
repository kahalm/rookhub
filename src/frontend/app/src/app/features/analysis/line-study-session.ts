import { Subscription, timer } from 'rxjs';
import { AnalysisNode, fromDto, toDto } from './analysis-tree';
import { LineStudy, LineStudyEvals, LineStudyService } from './line-study.service';
import { LineComparison, NodeEval, compareLines, flatNodes } from './line-study.util';
import { formatEval } from '../games/game-review.util';

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

  private saveSub?: Subscription;
  private pollSub?: Subscription;
  private dirty = false;
  private saving = false;
  /** Knoten in der Reihenfolge des zuletzt gespeicherten Baums — die Indizes der Bewertungen zeigen hierauf. */
  private savedFlat: AnalysisNode[] = [];
  private savedRoot: AnalysisNode | null = null;
  private destroyed = false;

  constructor(private readonly api: LineStudyService, private readonly host: LineStudyHost,
              readonly gameKey: string, private readonly gameUcis: readonly string[], private readonly title: string | null) {}

  /** Vorhandenen Baum holen (dann aufs Brett) oder den Partie-Baum als neuen anlegen. */
  open(): void {
    this.api.get(this.gameKey).subscribe({
      next: s => {
        if (this.destroyed) return;
        const { root, nodes } = fromDto(s.startFen, s.tree);
        const current = s.current >= 0 ? nodes[s.current] ?? root : root;
        this.host.setTree(root, current);
        this.accept(s, root);
        if (this.revealed) this.host.revealed();
        this.refreshEvals();
      },
      error: () => { if (!this.destroyed) this.saveNow(); },   // 404 = neu
    });
  }

  /** Nach jeder Änderung am Baum (gedrosselt). */
  schedule(): void {
    if (this.destroyed) return;
    this.dirty = true;
    this.saveSub?.unsubscribe();
    this.saveSub = timer(LineStudySession.SaveDelayMs).subscribe(() => this.saveNow());
  }

  /** Beim Verlassen: ausstehende Änderung sofort schicken. */
  flush(): void {
    if (this.dirty) this.saveNow();
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
    if (this.dirty) this.saveNow(go); else go();
  }

  destroy(): void {
    this.flush();
    this.destroyed = true;
    this.saveSub?.unsubscribe();
    this.pollSub?.unsubscribe();
  }

  private saveNow(after?: () => void): void {
    if (this.saving) { this.dirty = true; return; }
    const root = this.host.root();
    const { tree, current } = toDto(root, this.host.current());
    const flat = flatNodes(root);
    this.dirty = false;
    this.saving = true;
    this.api.save({ gameKey: this.gameKey, startFen: root.fen, title: this.title, tree, current }).subscribe({
      next: s => {
        this.saving = false;
        this.savedFlat = flat;
        this.accept(s, root);
        if (this.dirty) this.schedule();
        after?.();
        if (this.revealed) this.refreshEvals();
      },
      error: () => { this.saving = false; },
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
    }
    this.host.changed();
  }
}
