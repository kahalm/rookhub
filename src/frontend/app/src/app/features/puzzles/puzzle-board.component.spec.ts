import { PuzzleBoardComponent } from './puzzle-board.component';
import { Key } from 'chessground/types';

/**
 * Regressionsschutz gegen die ungewollte Damenumwandlung auf Mobilgeräten:
 * Der Promotion-Dialog erscheint direkt unter dem Finger (auf dem Zielfeld). Ohne Schutz
 * fiel der gerade ausgelöste Zug-Tap/-Klick sofort auf die oberste Auswahl (Dame) durch.
 * Ein kurzes Guard-Fenster muss diesen Ghost-Tap verwerfen, eine spätere Auswahl aber zulassen.
 */
describe('PuzzleBoardComponent Promotion-Guard', () => {
  function create(): PuzzleBoardComponent {
    const comp = new PuzzleBoardComponent();
    // pendingPromotion simulieren (sonst kehren beide Methoden sofort zurück)
    (comp as unknown as { pendingPromotion: { orig: Key; dest: Key } }).pendingPromotion =
      { orig: 'a7' as Key, dest: 'a8' as Key };
    return comp;
  }

  function setGuard(comp: PuzzleBoardComponent, untilMs: number): void {
    (comp as unknown as { promotionGuardUntil: number }).promotionGuardUntil = untilMs;
  }

  it('selectPromotion verwirft den Ghost-Tap innerhalb des Guard-Fensters', () => {
    const comp = create();
    setGuard(comp, Date.now() + 10_000);
    const emit = spyOn(comp.moveMade, 'emit');

    comp.selectPromotion('q');

    expect(emit).not.toHaveBeenCalled();
    expect(comp.showPromotionOverlay).toBeFalse(); // bleibt unverändert (Dialog war ohnehin nicht offen gesetzt)
  });

  it('selectPromotion emittiert die gewählte Figur nach Ablauf des Guard-Fensters', () => {
    const comp = create();
    setGuard(comp, Date.now() - 1);
    const emit = spyOn(comp.moveMade, 'emit');

    comp.selectPromotion('n');

    expect(emit).toHaveBeenCalledWith({ orig: 'a7' as Key, dest: 'a8' as Key, promotion: 'n' });
  });

  it('cancelPromotion ignoriert den durchfallenden Tap innerhalb des Guard-Fensters', () => {
    const comp = create();
    setGuard(comp, Date.now() + 10_000);

    comp.cancelPromotion();

    // pendingPromotion bleibt bestehen -> Dialog wurde nicht abgebrochen
    expect((comp as unknown as { pendingPromotion: unknown }).pendingPromotion).not.toBeNull();
  });

  it('cancelPromotion bricht nach Ablauf des Guard-Fensters ab', () => {
    const comp = create();
    setGuard(comp, Date.now() - 1);

    comp.cancelPromotion();

    expect((comp as unknown as { pendingPromotion: unknown }).pendingPromotion).toBeNull();
    expect(comp.showPromotionOverlay).toBeFalse();
  });
});
