import { of } from 'rxjs';
import { BookSettingsController, BookSettingsHost } from './book-settings';

describe('BookSettingsController (Zerlegung Z18)', () => {
  function host(over: Record<string, unknown> = {}): BookSettingsHost & { setups: number } {
    const prefs: any = {
      boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', bookStockfishDepth: 14, visualization: 2, vizArrow: false,
      offPathWarnMoves: 3, enPassantForced: false,
      setVisualization: jasmine.createSpy('setVisualization'), setVizArrow: () => {}, setBookStockfishDepth: () => {},
      setBoardTheme: () => {}, setPieceSet: () => {}, setThemeMode: () => {}, setOffPathWarnMoves: () => {}, setEnPassantForced: () => {},
    };
    const h: any = {
      prefs, dialog: { open: () => ({ afterClosed: () => of(null) }) }, puzzle: { id: 1 }, isSolving: false, anarchyForcedByUrl: false,
      solveModes: { followLevel: jasmine.createSpy('followLevel') },
      boardTheme: '', pieceSet: '', stockfishDepth: 0, themeMode: 'fixed', visualizationMode: 0, vizArrowEnabled: true,
      enPassantForced: false, setups: 0, setupPuzzle() { h.setups++; }, clearVizOpponentArrow: jasmine.createSpy('drop'), updateBoard: () => {},
      ...over,
    };
    return h;
  }

  it('übernimmt die Startwerte aus den Präferenzen', () => {
    const h = host();
    new BookSettingsController(h).load();
    expect([h.boardTheme, h.pieceSet, h.stockfishDepth, h.visualizationMode, h.vizArrowEnabled]).toEqual(['green', 'cburnett', 14, 2, false]);
  });

  it('Stufe setzt nur während des Lösens neu auf und zieht die Spielweise mit', () => {
    const h = host();
    const s = new BookSettingsController(h);
    s.setVisualizationLevel(1);
    expect(h.setups).toBe(0);
    expect(h.solveModes.followLevel).toHaveBeenCalledWith(1);
    (h as any).isSolving = true;
    s.setVisualizationLevel(2);
    expect(h.setups).toBe(1);
  });

  it('?anarchy=max erzwingt en passant beim Aufsetzen; Pfeil aus entfernt den Gegnerpfeil', () => {
    const h = host({ anarchyForcedByUrl: true });
    const s = new BookSettingsController(h);
    s.applyThemeForSetup();
    expect(h.enPassantForced).toBeTrue();
    s.setVizArrowEnabled(false);
    expect(h.clearVizOpponentArrow).toHaveBeenCalled();
  });
});
