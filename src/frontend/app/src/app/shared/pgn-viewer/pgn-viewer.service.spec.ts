import { PgnViewerService } from './pgn-viewer.service';

const SINGLE_GAME_PGN = `[Event "Test"]
[White "Kasparov"]
[Black "Karpov"]
[Result "1-0"]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0`;

const MULTI_GAME_PGN = `[Event "Game 1"]
[White "Player A"]
[Black "Player B"]
[Result "1-0"]

1. e4 e5 1-0

[Event "Game 2"]
[White "Player C"]
[Black "Player D"]
[Result "0-1"]

1. d4 d5 2. c4 e6 0-1`;

const INVALID_PGN = 'this is not valid pgn at all %%%';

describe('PgnViewerService', () => {
  let service: PgnViewerService;

  beforeEach(() => {
    service = new PgnViewerService();
  });

  describe('loadPgn', () => {
    it('should parse a single game PGN', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      expect(service.games.length).toBe(1);
      expect(service.games[0].headers['White']).toBe('Kasparov');
      expect(service.games[0].headers['Black']).toBe('Karpov');
      expect(service.games[0].headers['Result']).toBe('1-0');
    });

    it('should parse moves correctly', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      const moves = service.games[0].moves;
      expect(moves.length).toBe(6);
      expect(moves[0].san).toBe('e4');
      expect(moves[1].san).toBe('e5');
      expect(moves[2].san).toBe('Nf3');
    });

    it('should precompute FEN positions (startpos + one per move)', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      const fens = service.games[0].fens;
      expect(fens.length).toBe(7); // startpos + 6 moves
      expect(fens[0]).toContain('rnbqkbnr/pppppppp');
    });

    it('should parse multiple games', () => {
      service.loadPgn(MULTI_GAME_PGN);
      expect(service.games.length).toBe(2);
      expect(service.games[0].headers['White']).toBe('Player A');
      expect(service.games[1].headers['White']).toBe('Player C');
    });

    it('should handle invalid PGN gracefully', () => {
      service.loadPgn(INVALID_PGN);
      expect(service.games.length).toBe(0);
    });

    it('should handle empty string', () => {
      service.loadPgn('');
      expect(service.games.length).toBe(0);
    });

    it('should reset state on new load', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      service.goToEnd();
      service.loadPgn(MULTI_GAME_PGN);
      expect(service.currentGameIndex).toBe(0);
      expect(service.currentMoveIndex).toBe(-1);
    });
  });

  describe('currentGame', () => {
    it('should return null when no games loaded', () => {
      expect(service.currentGame).toBeNull();
    });

    it('should return first game after load', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      expect(service.currentGame).toBeTruthy();
      expect(service.currentGame!.headers['White']).toBe('Kasparov');
    });
  });

  describe('currentFen', () => {
    it('should return start position when no games loaded', () => {
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
    });

    it('should return start position at move index -1', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      expect(service.currentFen).toContain('rnbqkbnr/pppppppp');
    });

    it('should return position after first move', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      service.goForward();
      expect(service.currentFen).not.toContain('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP');
      expect(service.currentFen).toContain('4'); // e4 opens e2-e4
    });
  });

  describe('lastMove', () => {
    it('should return undefined at start position', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      expect(service.lastMove).toBeUndefined();
    });

    it('should return from/to squares after navigating', () => {
      service.loadPgn(SINGLE_GAME_PGN);
      service.goForward(); // 1. e4
      expect(service.lastMove).toEqual(['e2', 'e4']);
    });
  });

  describe('navigation', () => {
    beforeEach(() => {
      service.loadPgn(SINGLE_GAME_PGN); // 6 moves
    });

    it('goForward should advance one move', () => {
      service.goForward();
      expect(service.currentMoveIndex).toBe(0);
      service.goForward();
      expect(service.currentMoveIndex).toBe(1);
    });

    it('goForward should not go past last move', () => {
      service.goToEnd();
      const endIndex = service.currentMoveIndex;
      service.goForward();
      expect(service.currentMoveIndex).toBe(endIndex);
    });

    it('goBack should go back one move', () => {
      service.goForward();
      service.goForward();
      service.goBack();
      expect(service.currentMoveIndex).toBe(0);
    });

    it('goBack should not go before start', () => {
      service.goBack();
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('goToEnd should jump to last move', () => {
      service.goToEnd();
      expect(service.currentMoveIndex).toBe(5); // 6 moves, index 5
    });

    it('goToStart should reset to before first move', () => {
      service.goToEnd();
      service.goToStart();
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('goToMove should jump to specific move', () => {
      service.goToMove(3);
      expect(service.currentMoveIndex).toBe(3);
    });

    it('goToMove should reject out-of-range index', () => {
      service.goToMove(100);
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('goToMove(-1) should go to start position', () => {
      service.goForward();
      service.goToMove(-1);
      expect(service.currentMoveIndex).toBe(-1);
    });
  });

  describe('selectGame', () => {
    beforeEach(() => {
      service.loadPgn(MULTI_GAME_PGN);
    });

    it('should switch to second game', () => {
      service.selectGame(1);
      expect(service.currentGameIndex).toBe(1);
      expect(service.currentGame!.headers['White']).toBe('Player C');
    });

    it('should reset move index on game switch', () => {
      service.goToEnd();
      service.selectGame(1);
      expect(service.currentMoveIndex).toBe(-1);
    });

    it('should ignore invalid game index', () => {
      service.selectGame(99);
      expect(service.currentGameIndex).toBe(0);
    });

    it('should ignore negative game index', () => {
      service.selectGame(-1);
      expect(service.currentGameIndex).toBe(0);
    });
  });
});
