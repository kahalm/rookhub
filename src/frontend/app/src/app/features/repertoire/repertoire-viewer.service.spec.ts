import { RepertoireViewerService } from './repertoire-viewer.service';

const SAMPLE_PGN = `[Event "Sicilian"]
[White "Kasparov"]
[Black "Karpov"]
[Result "1-0"]
[Opening "Sicilian Defense"]

1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 1-0

[Event "French"]
[White "Fischer"]
[Black "Petrosian"]
[Result "0-1"]
[ECO "C00"]

1. e4 e6 2. d4 d5 0-1`;

describe('RepertoireViewerService', () => {
  let service: RepertoireViewerService;

  beforeEach(() => {
    service = new RepertoireViewerService();
  });

  describe('loadPgn', () => {
    it('should parse games and build lines', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.games.length).toBe(2);
      expect(service.lines.length).toBe(2);
    });

    it('should extract line metadata', () => {
      service.loadPgn(SAMPLE_PGN);
      const line = service.lines[0];
      expect(line.white).toBe('Kasparov');
      expect(line.black).toBe('Karpov');
      expect(line.result).toBe('1-0');
      expect(line.opening).toBe('Sicilian Defense');
      expect(line.moveCount).toBe(4);
    });

    it('should use ECO as fallback for opening', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.lines[1].opening).toBe('C00');
    });

    it('should build summary from first moves', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.lines[0].summary).toContain('1. e4 c5');
      expect(service.lines[0].summary).toContain('2. Nf3');
    });

    it('should reset state on reload', () => {
      service.loadPgn(SAMPLE_PGN);
      service.selectLine(0);
      service.goToEnd();
      service.loadPgn(SAMPLE_PGN);
      expect(service.selectedLineIndex).toBe(-1);
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('should handle empty PGN', () => {
      service.loadPgn('');
      expect(service.lines.length).toBe(0);
      expect(service.games.length).toBe(0);
    });
  });

  describe('selectLine / deselectLine', () => {
    beforeEach(() => service.loadPgn(SAMPLE_PGN));

    it('should select a line', () => {
      service.selectLine(0);
      expect(service.selectedLineIndex).toBe(0);
      expect(service.selectedGame).toBeTruthy();
      expect(service.selectedGame!.headers['White']).toBe('Kasparov');
    });

    it('should reset move index on line select', () => {
      service.selectLine(0);
      service.goToEnd();
      service.selectLine(1);
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('should deselect line', () => {
      service.selectLine(0);
      service.deselectLine();
      expect(service.selectedLineIndex).toBe(-1);
      expect(service.selectedGame).toBeNull();
    });

    it('should ignore invalid line index', () => {
      service.selectLine(99);
      expect(service.selectedLineIndex).toBe(-1);
    });
  });

  describe('navigation', () => {
    beforeEach(() => {
      service.loadPgn(SAMPLE_PGN);
      service.selectLine(0); // 8 half-moves
    });

    it('should return start FEN initially', () => {
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
    });

    it('should navigate forward', () => {
      service.goForward();
      expect(service.currentMoveIndex).toBe(0);
      expect(service.currentFen).not.toContain('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP');
    });

    it('should not go past last move', () => {
      service.goToEnd();
      const idx = service.currentMoveIndex;
      service.goForward();
      expect(service.currentMoveIndex).toBe(idx);
    });

    it('should navigate back', () => {
      service.goForward();
      service.goForward();
      service.goBack();
      expect(service.currentMoveIndex).toBe(0);
    });

    it('should not go before start', () => {
      service.goBack();
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('should go to end', () => {
      service.goToEnd();
      expect(service.currentMoveIndex).toBe(7);
    });

    it('should go to start', () => {
      service.goToEnd();
      service.goToStart();
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('should go to specific move', () => {
      service.goToMove(3);
      expect(service.currentMoveIndex).toBe(3);
    });

    it('should return lastMove after navigating', () => {
      expect(service.lastMove).toBeUndefined();
      service.goForward(); // e4
      expect(service.lastMove).toEqual(['e2', 'e4']);
    });

    it('should return currentMoves for selected line', () => {
      expect(service.currentMoves.length).toBe(8);
      expect(service.currentMoves[0].san).toBe('e4');
    });
  });

  describe('no line selected', () => {
    it('should return start FEN when no line selected', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
    });

    it('should return undefined lastMove when no line selected', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.lastMove).toBeUndefined();
    });

    it('should return empty moves when no line selected', () => {
      service.loadPgn(SAMPLE_PGN);
      expect(service.currentMoves.length).toBe(0);
    });
  });
});
