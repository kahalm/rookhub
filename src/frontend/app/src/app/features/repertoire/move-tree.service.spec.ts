import { MoveTreeService } from './move-tree.service';

const SAMPLE_PGN = `[Event "Game 1"]
[White "A"]
[Black "B"]
[Result "*"]

1. e4 e5 2. Nf3 Nc6 *

[Event "Game 2"]
[White "C"]
[Black "D"]
[Result "*"]

1. e4 c5 2. Nf3 d6 *

[Event "Game 3"]
[White "E"]
[Black "F"]
[Result "*"]

1. e4 e5 2. Bc4 Nf6 *

[Event "Game 4"]
[White "G"]
[Black "H"]
[Result "*"]

1. d4 d5 *`;

describe('MoveTreeService', () => {
  let service: MoveTreeService;

  beforeEach(() => {
    service = new MoveTreeService();
  });

  describe('buildTree', () => {
    it('should build tree from PGN', () => {
      service.buildTree(SAMPLE_PGN);
      expect(service.totalGames).toBe(4);
    });

    it('should count children correctly at root', () => {
      service.buildTree(SAMPLE_PGN);
      const children = service.children;
      expect(children.length).toBe(2); // e4, d4
      const e4 = children.find(c => c.san === 'e4');
      const d4 = children.find(c => c.san === 'd4');
      expect(e4!.count).toBe(3);
      expect(d4!.count).toBe(1);
    });

    it('should sort children by count descending', () => {
      service.buildTree(SAMPLE_PGN);
      const children = service.children;
      expect(children[0].san).toBe('e4');
      expect(children[1].san).toBe('d4');
    });

    it('should handle empty PGN', () => {
      service.buildTree('');
      expect(service.totalGames).toBe(0);
      expect(service.children.length).toBe(0);
    });
  });

  describe('navigation', () => {
    beforeEach(() => service.buildTree(SAMPLE_PGN));

    it('should start at root with start FEN', () => {
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
      expect(service.lastMove).toBeUndefined();
      expect(service.breadcrumbs.length).toBe(0);
    });

    it('selectChild should navigate into tree', () => {
      service.selectChild('e4');
      expect(service.breadcrumbs.length).toBe(1);
      expect(service.breadcrumbs[0].san).toBe('e4');
      expect(service.lastMove).toEqual(['e2', 'e4']);
    });

    it('should show correct children after navigating', () => {
      service.selectChild('e4');
      const children = service.children;
      expect(children.length).toBe(2); // e5, c5
      const e5 = children.find(c => c.san === 'e5');
      const c5 = children.find(c => c.san === 'c5');
      expect(e5!.count).toBe(2);
      expect(c5!.count).toBe(1);
    });

    it('goUp should navigate to parent', () => {
      service.selectChild('e4');
      service.selectChild('e5');
      service.goUp();
      expect(service.breadcrumbs.length).toBe(1);
      expect(service.breadcrumbs[0].san).toBe('e4');
    });

    it('goUp at root should stay at root', () => {
      service.goUp();
      expect(service.breadcrumbs.length).toBe(0);
    });

    it('goToRoot should return to start', () => {
      service.selectChild('e4');
      service.selectChild('e5');
      service.selectChild('Nf3');
      service.goToRoot();
      expect(service.breadcrumbs.length).toBe(0);
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
    });

    it('goToDepth should jump to specific depth', () => {
      service.selectChild('e4');
      service.selectChild('e5');
      service.selectChild('Nf3');
      service.goToDepth(1); // back to e4
      expect(service.breadcrumbs.length).toBe(1);
      expect(service.breadcrumbs[0].san).toBe('e4');
    });

    it('goToDepth(0) should go to root', () => {
      service.selectChild('e4');
      service.selectChild('e5');
      service.goToDepth(0);
      expect(service.breadcrumbs.length).toBe(0);
    });

    it('selectChild should ignore invalid san', () => {
      service.selectChild('Zz9');
      expect(service.breadcrumbs.length).toBe(0);
    });

    it('should drill down to leaf with no children', () => {
      service.selectChild('d4');
      service.selectChild('d5');
      expect(service.children.length).toBe(0);
    });
  });

  describe('breadcrumbs', () => {
    beforeEach(() => service.buildTree(SAMPLE_PGN));

    it('should build breadcrumb trail', () => {
      service.selectChild('e4');
      service.selectChild('e5');
      service.selectChild('Nf3');
      const crumbs = service.breadcrumbs;
      expect(crumbs.length).toBe(3);
      expect(crumbs[0].san).toBe('e4');
      expect(crumbs[0].depth).toBe(1);
      expect(crumbs[1].san).toBe('e5');
      expect(crumbs[1].depth).toBe(2);
      expect(crumbs[2].san).toBe('Nf3');
      expect(crumbs[2].depth).toBe(3);
    });
  });

  describe('rebuild', () => {
    it('should reset navigation on rebuild', () => {
      service.buildTree(SAMPLE_PGN);
      service.selectChild('e4');
      service.buildTree(SAMPLE_PGN);
      expect(service.breadcrumbs.length).toBe(0);
    });
  });
});
