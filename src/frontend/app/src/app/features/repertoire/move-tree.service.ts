import { Injectable } from '@angular/core';
import { ParsedGame, START_FEN, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

export interface MoveTreeNode {
  san: string;
  fen: string;
  from: string;
  to: string;
  count: number;
  children: Map<string, MoveTreeNode>;
  parent: MoveTreeNode | null;
  depth: number;
}

export interface TreeChild {
  san: string;
  count: number;
  node: MoveTreeNode;
}

export interface Breadcrumb {
  san: string;
  depth: number;
  node: MoveTreeNode;
}

@Injectable()
export class MoveTreeService {
  private root: MoveTreeNode = this.createRoot();
  private current: MoveTreeNode = this.root;

  get currentFen(): string {
    return this.current.fen;
  }

  get lastMove(): [string, string] | undefined {
    if (this.current === this.root) return undefined;
    return [this.current.from, this.current.to];
  }

  get breadcrumbs(): Breadcrumb[] {
    const crumbs: Breadcrumb[] = [];
    let node: MoveTreeNode | null = this.current;
    while (node && node !== this.root) {
      crumbs.unshift({ san: node.san, depth: node.depth, node });
      node = node.parent;
    }
    return crumbs;
  }

  get children(): TreeChild[] {
    return Array.from(this.current.children.values())
      .sort((a, b) => b.count - a.count)
      .map(node => ({ san: node.san, count: node.count, node }));
  }

  get totalGames(): number {
    return this.root.count;
  }

  buildTree(pgnText: string): void {
    const games = parsePgnText(pgnText);
    this.root = this.createRoot();
    this.root.count = games.length;

    for (const game of games) {
      let node = this.root;
      for (const move of game.moves) {
        if (!node.children.has(move.san)) {
          node.children.set(move.san, {
            san: move.san,
            fen: move.after,
            from: move.from,
            to: move.to,
            count: 0,
            children: new Map(),
            parent: node,
            depth: node.depth + 1,
          });
        }
        const child = node.children.get(move.san)!;
        child.count++;
        node = child;
      }
    }

    this.current = this.root;
  }

  selectChild(san: string): void {
    const child = this.current.children.get(san);
    if (child) {
      this.current = child;
    }
  }

  goUp(): void {
    if (this.current.parent) {
      this.current = this.current.parent;
    }
  }

  goToRoot(): void {
    this.current = this.root;
  }

  goToDepth(depth: number): void {
    const crumbs = this.breadcrumbs;
    if (depth === 0) {
      this.goToRoot();
      return;
    }
    const target = crumbs.find(c => c.depth === depth);
    if (target) {
      this.current = target.node;
    }
  }

  private createRoot(): MoveTreeNode {
    return {
      san: '',
      fen: START_FEN,
      from: '',
      to: '',
      count: 0,
      children: new Map(),
      parent: null,
      depth: 0,
    };
  }
}
