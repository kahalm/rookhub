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

/**
 * Viz-Modus: Figuren ziehen funktioniert genauso wie Antippen. Ein Ziehen (Start→Ziel)
 * wird über dieselbe Legalitäts-/Promotion-Prüfung wie der 2. Tap zu einem Zug.
 */
describe('PuzzleBoardComponent Viz-Drag', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  function create(): PuzzleBoardComponent {
    const comp = new PuzzleBoardComponent();
    comp.actualFen = START;
    comp.orientation = 'white';
    return comp;
  }

  type Privates = {
    handleVizDrag(orig: Key, dest: Key): void;
    handleVizTap(key: Key): void;
    vizFrom?: Key;
  };

  it('legale Ziehgeste emittiert den Zug Start→Ziel', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');

    (comp as unknown as Privates).handleVizDrag('e2' as Key, 'e4' as Key);

    expect(emit).toHaveBeenCalledWith({ orig: 'e2' as Key, dest: 'e4' as Key });
  });

  it('illegale Ziehgeste wählt stattdessen das Startfeld aus (kein Zug)', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');

    (comp as unknown as Privates).handleVizDrag('e2' as Key, 'e5' as Key);

    expect(emit).not.toHaveBeenCalled();
    expect((comp as unknown as Privates).vizFrom).toBe('e2' as Key);
    expect(comp.vizSelectedSquare).toBe('e2' as Key);
  });

  it('Zwei-Tap-Auswahl emittiert beim zweiten (legalen) Tap', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');

    (comp as unknown as Privates).handleVizTap('e2' as Key);   // 1. Tap: Auswahl
    expect(emit).not.toHaveBeenCalled();
    expect(comp.vizSelectedSquare).toBe('e2' as Key);

    (comp as unknown as Privates).handleVizTap('e4' as Key);   // 2. Tap: Zug
    expect(emit).toHaveBeenCalledWith({ orig: 'e2' as Key, dest: 'e4' as Key });
  });
});

/**
 * Viz-Gesten-Verwaltung (Pointer-Ebene): Multi-Touch-Festigkeit, pointercancel-Reset,
 * an die Feldgröße skalierte Drag-Schwelle und Randfeld-Clamp.
 */
describe('PuzzleBoardComponent Viz-Gesten', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  type GestPriv = {
    onVizPointerDown(ev: PointerEvent): void;
    onVizPointerUp(ev: PointerEvent): void;
    onVizPointerCancel(ev: PointerEvent): void;
    vizDragThresholdPx(): number;
    keyFromPointer(ev: PointerEvent, clamp?: boolean): Key | null;
    vizPointerId?: number;
    vizPointerStartKey?: Key;
  };

  // 800px-Brett → Feldbreite 100. Weiß: col=floor(x/100), rankIdx=7-floor(y/100).
  function mounted(): { comp: PuzzleBoardComponent; captures: number[] } {
    const comp = new PuzzleBoardComponent();
    comp.actualFen = START;
    comp.orientation = 'white';
    comp.visualization = 2;
    const captures: number[] = [];
    (comp as unknown as { boardEl: unknown }).boardEl = {
      nativeElement: {
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 800 }),
        setPointerCapture: (id: number) => captures.push(id),
      },
    };
    (comp as unknown as { ground: unknown }).ground = {
      setShapes: () => {}, selectSquare: () => {}, setAutoShapes: () => {},
    };
    return { comp, captures };
  }

  function ptr(pointerId: number, clientX: number, clientY: number): PointerEvent {
    return { pointerId, clientX, clientY, preventDefault: () => {}, stopPropagation: () => {} } as unknown as PointerEvent;
  }

  it('Multi-Touch: ein zweiter Pointer überschreibt die laufende Geste nicht', () => {
    const { comp, captures } = mounted();
    const p = comp as unknown as GestPriv;
    p.onVizPointerDown(ptr(1, 50, 750));    // a1, Geste startet
    expect(p.vizPointerId).toBe(1);
    expect(p.vizPointerStartKey).toBe('a1' as Key);
    p.onVizPointerDown(ptr(2, 250, 550));   // 2. Finger → ignoriert
    expect(p.vizPointerId).toBe(1);
    expect(p.vizPointerStartKey).toBe('a1' as Key);
    expect(captures).toEqual([1]);          // kein zweiter setPointerCapture
  });

  it('pointercancel setzt die Geste zurück → folgendes pointerup emittiert nichts', () => {
    const { comp } = mounted();
    const p = comp as unknown as GestPriv;
    const emit = spyOn(comp.moveMade, 'emit');
    p.onVizPointerDown(ptr(1, 50, 750));
    p.onVizPointerCancel(ptr(1, 50, 750));
    expect(p.vizPointerId).toBeUndefined();
    p.onVizPointerUp(ptr(1, 250, 550));     // keine aktive Geste mehr
    expect(emit).not.toHaveBeenCalled();
  });

  it('Drag-Schwelle skaliert mit der Brettgröße (~35% einer Feldbreite)', () => {
    const { comp } = mounted();             // 800/8=100 → 35
    expect((comp as unknown as GestPriv).vizDragThresholdPx()).toBeCloseTo(35, 5);
  });

  it('keyFromPointer: Release knapp außerhalb → null ohne, Randfeld mit Clamp', () => {
    const p = mounted().comp as unknown as GestPriv;
    expect(p.keyFromPointer(ptr(1, 820, 10))).toBeNull();
    expect(p.keyFromPointer(ptr(1, 820, 10), true)).toBe('h8' as Key);
  });

  it('echte Ziehgeste über Pointer-Events emittiert den Zug a2→a4', () => {
    const { comp } = mounted();
    const p = comp as unknown as GestPriv;
    const emit = spyOn(comp.moveMade, 'emit');
    p.onVizPointerDown(ptr(1, 50, 650));    // a2
    p.onVizPointerUp(ptr(1, 50, 450));      // a4 (200px bewegt > 35) → Drag
    expect(emit).toHaveBeenCalledWith({ orig: 'a2' as Key, dest: 'a4' as Key });
  });

  it('Rechtsklick wird durchgereicht: keine Viz-Geste, kein preventDefault (Pfeil-Zeichnen)', () => {
    const { comp, captures } = mounted();
    const p = comp as unknown as GestPriv;
    const prevented = jasmine.createSpy('preventDefault');
    const stopped = jasmine.createSpy('stopPropagation');
    const rightClick = {
      pointerId: 1, clientX: 50, clientY: 750, button: 2,
      preventDefault: prevented, stopPropagation: stopped,
    } as unknown as PointerEvent;
    p.onVizPointerDown(rightClick);
    expect(p.vizPointerId).toBeUndefined();   // keine Geste gestartet
    expect(prevented).not.toHaveBeenCalled(); // Chessground bekommt den mousedown
    expect(stopped).not.toHaveBeenCalled();
    expect(captures).toEqual([]);             // kein Pointer-Capture
    p.onVizPointerUp(rightClick);             // Loslassen ebenfalls durchgereicht
    expect(prevented).not.toHaveBeenCalled();
  });
});
