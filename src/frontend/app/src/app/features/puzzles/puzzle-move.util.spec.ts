import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { parseUci, applyUci, tryFreeMove, calcDests, formatSanList, formatSanListHtml, tryLoadFen, fenSideToMove } from './puzzle-move.util';

describe('puzzle-move.util', () => {
  describe('parseUci', () => {
    it('zerlegt einen einfachen Zug ohne Promotion', () => {
      expect(parseUci('e2e4')).toEqual({ from: 'e2', to: 'e4', promotion: undefined } as any);
    });
    it('liest die Promotion-Figur aus dem 5. Zeichen', () => {
      expect(parseUci('e7e8q')).toEqual({ from: 'e7', to: 'e8', promotion: 'q' } as any);
    });
  });

  describe('applyUci', () => {
    it('wendet den Zug an und liefert den SAN', () => {
      const c = new Chess();
      const mv = applyUci(c, 'e2e4');
      expect(mv.san).toBe('e4');
      expect(c.fen()).toContain(' b '); // nach 1.e4 ist Schwarz am Zug
    });
    it('wendet eine Promotion an', () => {
      const c = new Chess('8/P7/8/8/8/8/8/k6K w - - 0 1');
      const mv = applyUci(c, 'a7a8q');
      expect(mv.promotion).toBe('q');
    });
  });

  describe('tryFreeMove', () => {
    it('spielt einen legalen Zug und gibt den Move zurück', () => {
      const c = new Chess();
      const mv = tryFreeMove(c, 'e2' as Key, 'e4' as Key);
      expect(mv).not.toBeNull();
      expect(mv!.san).toBe('e4');
    });
    it('gibt null bei illegalem Zug zurück (Stellung unverändert)', () => {
      const c = new Chess();
      const fenBefore = c.fen();
      const mv = tryFreeMove(c, 'e2' as Key, 'e5' as Key);
      expect(mv).toBeNull();
      expect(c.fen()).toBe(fenBefore);
    });
    it('promoviert per Default zur Dame, wenn keine Figur angegeben ist', () => {
      const c = new Chess('8/P7/8/8/8/8/8/k6K w - - 0 1');
      const mv = tryFreeMove(c, 'a7' as Key, 'a8' as Key);
      expect(mv).not.toBeNull();
      expect(mv!.promotion).toBe('q');
    });
  });

  describe('calcDests', () => {
    it('liefert für die Grundstellung 16 Figuren mit Zielen (a2..h2 + Springer)', () => {
      const dests = calcDests(new Chess());
      // 8 Bauern + 2 Springer = 10 Felder mit Zielen
      expect(dests.size).toBe(10);
      expect(dests.get('e2' as Key)).toContain('e3' as Key);
      expect(dests.get('e2' as Key)).toContain('e4' as Key);
      expect(dests.get('g1' as Key)).toContain('f3' as Key);
    });

    it('enPassantForced: nur der En-passant-Schlag bleibt erlaubt, wenn einer möglich ist', () => {
      // Weiß Bauer e5, Schwarz gerade d7-d5 → en passant auf d6 möglich.
      const fen = 'rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3';
      const all = calcDests(new Chess(fen));
      const forced = calcDests(new Chess(fen), true);
      expect(all.size).toBeGreaterThan(1);          // normal viele Züge
      expect(forced.size).toBe(1);                  // erzwungen: nur e5
      expect(forced.get('e5' as Key)).toEqual(['d6' as Key]);
    });

    it('enPassantForced ohne verfügbares En passant lässt alle Züge zu', () => {
      expect(calcDests(new Chess(), true).size).toBe(10);
    });
  });

  describe('formatSanList', () => {
    it('nummeriert ab Weiß korrekt', () => {
      expect(formatSanList(['e4', 'e5', 'Nf3'], true, 1)).toBe('1. e4 e5 2. Nf3');
    });
    it('beginnt mit „n..." wenn Schwarz am Zug ist', () => {
      expect(formatSanList(['e5', 'Nf3'], false, 1)).toBe('1... e5 2. Nf3');
    });
    it('gibt leeren String für keine Züge zurück', () => {
      expect(formatSanList([], true, 5)).toBe('');
    });
  });

  describe('formatSanListHtml', () => {
    it('hebt Gegnerzüge (ungerade Indizes) mit <strong> hervor', () => {
      // Index 0 = User (e4), Index 1 = Gegner (e5, fett)
      expect(formatSanListHtml(['e4', 'e5'], true, 1)).toBe('1. e4 <strong>e5</strong>');
    });
  });

  describe('tryLoadFen', () => {
    it('lädt eine legale FEN', () => {
      expect(tryLoadFen('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1')).not.toBeNull();
    });
    it('gibt null für eine illegale Chessable-Diagramm-FEN zurück (kein König)', () => {
      // Chessable-Muster-Diagramm ohne Könige — chess.js verwirft, chessground zeigt es trotzdem.
      expect(tryLoadFen('8/2p1n3/3b1n2/3pp3/3PP3/2P5/1P3P2/8 w - - 0 1')).toBeNull();
    });
  });

  describe('fenSideToMove', () => {
    it('liest die Farbe am Zug aus dem 2. Feld', () => {
      expect(fenSideToMove('8/2p1n3/3b1n2/3pp3/3PP3/2P5/1P3P2/8 b - - 0 1')).toBe('b');
      expect(fenSideToMove('r6r/4bpk1/4p3/7Q/8/6R1/8/8 b - - 0 1')).toBe('b');
    });
    it('fällt ohne Farb-Feld auf Weiß zurück', () => {
      expect(fenSideToMove('8/8/8/8/8/8/8/8')).toBe('w');
    });
  });
});
