import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  BoardFullscreenButtonComponent,
} from '../../shared/fullscreen/board-fullscreen-button.component';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { Key } from 'chessground/types';

/**
 * Der Ghost-Tap-Schutz und die Positionierung liegen seit der Zusammenführung in
 * PromotionPickerComponent (shared/promotion-picker/) und sind dort getestet — inklusive
 * Guard für choose UND dismiss. Hier bleibt nur, was das Brett selbst verantwortet:
 * die Auswahl als Zug zu melden und beim Abbruch die von chessground bereits ausgeführte
 * Bewegung optisch zurückzunehmen.
 */
describe('PuzzleBoardComponent Promotion-Anbindung', () => {
  function create(): PuzzleBoardComponent {
    const comp = new PuzzleBoardComponent();
    comp.pendingPromotion = { orig: 'a7' as Key, dest: 'a8' as Key };
    return comp;
  }

  it('meldet die gewählte Figur als Zug und schließt den Dialog', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');

    comp.selectPromotion('n');

    expect(emit).toHaveBeenCalledWith({ orig: 'a7' as Key, dest: 'a8' as Key, promotion: 'n' });
    expect(comp.pendingPromotion).toBeNull();
  });

  it('schließt den Dialog beim Abbruch, ohne einen Zug zu melden', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');

    comp.cancelPromotion();

    expect(emit).not.toHaveBeenCalled();
    expect(comp.pendingPromotion).toBeNull();
  });

  it('tut nichts, wenn gar keine Umwandlung offen ist', () => {
    const comp = new PuzzleBoardComponent();
    const emit = spyOn(comp.moveMade, 'emit');

    comp.selectPromotion('q');
    comp.cancelPromotion();

    expect(emit).not.toHaveBeenCalled();
  });

  it('autoQueen: meldet sofort eine Dame, ohne Auswahl zu zeigen (Kinderseite)', () => {
    const comp = new PuzzleBoardComponent();
    comp.autoQueen = true;
    const emit = spyOn(comp.moveMade, 'emit');

    (comp as any).showPromotionDialog('a7' as Key, 'a8' as Key);

    expect(emit).toHaveBeenCalledWith({ orig: 'a7' as Key, dest: 'a8' as Key, promotion: 'q' });
    expect(comp.pendingPromotion).toBeNull();
  });

  it('ohne autoQueen bleibt es bei der Auswahl', () => {
    const comp = new PuzzleBoardComponent();
    const emit = spyOn(comp.moveMade, 'emit');

    (comp as any).showPromotionDialog('a7' as Key, 'a8' as Key);

    expect(emit).not.toHaveBeenCalled();
    expect(comp.pendingPromotion).toEqual({ orig: 'a7' as Key, dest: 'a8' as Key });
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

/**
 * Umwandlungs-Erkennung muss auch für einen noch NICHT ausgeführten Premove greifen: dort steht der
 * Bauer noch auf orig (dest ist leer/wird geschlagen). Ohne die orig-Erkennung bekam eine premovte
 * Umwandlung keinen Figuren-Auswahldialog und wurde ohne Umwandlungsfigur gemeldet.
 */
describe('PuzzleBoardComponent Promotion-Erkennung (Premove)', () => {
  type Priv = { isPromotion(orig: Key, dest: Key, fromOrigin?: boolean): boolean };

  function withPieces(entries: [Key, { role: string; color?: string }][]): PuzzleBoardComponent {
    const comp = new PuzzleBoardComponent();
    (comp as unknown as { ground: unknown }).ground = { state: { pieces: new Map(entries) } };
    return comp;
  }

  it('erkennt einen ausgeführten Umwandlungszug am Bauern auf dest', () => {
    const p = withPieces([['f1' as Key, { role: 'pawn', color: 'black' }]]) as unknown as Priv;
    expect(p.isPromotion('f2' as Key, 'f1' as Key)).toBe(true);
  });

  it('erkennt einen premovten Umwandlungszug am Bauern auf orig (dest noch leer)', () => {
    const comp = withPieces([['f2' as Key, { role: 'pawn', color: 'black' }]]);
    const p = comp as unknown as Priv;
    expect(p.isPromotion('f2' as Key, 'f1' as Key)).toBe(false);        // ohne Flag: dest leer
    expect(p.isPromotion('f2' as Key, 'f1' as Key, true)).toBe(true);   // Premove: orig-Bauer
  });

  it('ist kein Umwandlungszug, wenn das Zielfeld nicht auf Grundreihe liegt', () => {
    const p = withPieces([['e2' as Key, { role: 'pawn', color: 'white' }]]) as unknown as Priv;
    expect(p.isPromotion('e2' as Key, 'e4' as Key, true)).toBe(false);
  });
});

/**
 * Der Vollbild-Knopf sitzt IM Brett-Wrapper (= dem Element, das ins Vollbild geht) — nur dort
 * bleibt er im Vollbild sichtbar, weil der Browser ausschließlich diesen Teilbaum rendert.
 */
describe('PuzzleBoardComponent Vollbild-Knopf', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleBoardComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  it('rendert ihn ÜBER dem Brett (in der Vollbild-Hülle, nicht als Overlay im Wrapper)', () => {
    // Als Overlay in der Brett-Ecke verdeckte er das Eckfeld — jetzt eine Zeile über dem Brett.
    const fixture = TestBed.createComponent(PuzzleBoardComponent);
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement.querySelector('.board-fs-host');
    expect(host.querySelector('.board-fs-btn')).not.toBeNull();
    // NICHT mehr im Brett-Wrapper (dort läge er über dem Eckfeld) …
    expect(fixture.nativeElement.querySelector('.board-wrapper .board-fs-btn')).toBeNull();
    // … sondern VOR ihm (Zeile oberhalb des Bretts).
    const button = host.querySelector('app-board-fullscreen-button')!;
    const wrapper = host.querySelector('.board-wrapper')!;
    expect(button.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('schickt die ÄUSSERE Hülle ins Vollbild, nicht die Brettfläche', () => {
    // Die Größe des Vollbild-Elements erzwingt der Browser (UA-!important) — deshalb geht die
    // Hülle ins Vollbild und das Brett wird darin als Quadrat der kleineren Bildschirmseite
    // zentriert. Wäre der Wrapper selbst das Ziel, füllte das Brett die Breite und liefe unten
    // aus dem Bild (Regression 0.322.0).
    const fixture = TestBed.createComponent(PuzzleBoardComponent);
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.directive(BoardFullscreenButtonComponent));
    const target: HTMLElement = button.componentInstance.target;
    expect(target.classList).toContain('board-fs-host');
    expect(target.querySelector('.board-wrapper')).not.toBeNull();
  });

  it('lässt sich abschalten (allowFullscreen = false)', () => {
    const fixture = TestBed.createComponent(PuzzleBoardComponent);
    fixture.componentInstance.allowFullscreen = false;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-board-fullscreen-button')).toBeNull();
  });
});

describe('PuzzleBoardComponent Vollbild-Projektion', () => {
  it('projiziert Consumer-Inhalte (z. B. den Kalkulations-Timer) in die Vollbild-Hülle', async () => {
    // Nur was INNERHALB der Hülle liegt, ist im Vollbild sichtbar — der Browser rendert dort
    // ausschließlich diesen Teilbaum.
    @Component({
      standalone: true,
      imports: [PuzzleBoardComponent],
      template: '<app-puzzle-board><span id="probe" data-fs-only>0:00</span></app-puzzle-board>',
    })
    class HostComponent {}

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement.querySelector('.board-fs-host');
    expect(host.querySelector('#probe')).not.toBeNull();
  });
});

/**
 * Codereview F2-004: Das Brett nahm Züge nur über Maus/Touch an — kein Löser war ohne Zeigegerät lösbar (WCAG 2.1.1).
 * Jetzt gibt es eine Zug-Eingabe per Tastatur, die denselben Weg geht wie ein gezogener Zug.
 */
describe('PuzzleBoardComponent Zug-Eingabe per Tastatur (F2-004)', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  const PROMO = '8/P6k/8/8/8/8/7K/8 w - - 0 1';

  function create(fen = START, dests: [string, string[]][] = [['g1', ['f3', 'h3']], ['e2', ['e3', 'e4']]]) {
    const comp = new PuzzleBoardComponent();
    comp.fen = fen;
    comp.orientation = 'white';
    comp.turnColor = 'white';
    comp.dests = new Map(dests) as unknown as Map<Key, Key[]>;
    return comp;
  }

  it('ein getippter legaler Zug wird gemeldet wie ein gezogener', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');
    expect(comp.keyboardMove('Nf3')).toBeTrue();
    expect(emit).toHaveBeenCalledWith({ orig: 'g1' as Key, dest: 'f3' as Key });
    expect(comp.kbdFeedback).toBeNull();
  });

  it('nicht am Zug oder Brett gesperrt: kein Zug, eine Rückmeldung', () => {
    const notMine = create();
    notMine.turnColor = 'black';
    const emit1 = spyOn(notMine.moveMade, 'emit');
    expect(notMine.keyboardMove('Nf3')).toBeFalse();
    expect(emit1).not.toHaveBeenCalled();
    expect(notMine.kbdFeedback).toEqual({ key: 'puzzles.keyboardMove.notYourTurn' });

    const locked = create();
    locked.viewOnly = true;
    const emit2 = spyOn(locked.moveMade, 'emit');
    expect(locked.keyboardMove('Nf3')).toBeFalse();
    expect(emit2).not.toHaveBeenCalled();
  });

  it('illegal, unlesbar oder vom Brett gerade nicht erlaubt (dests): Rückmeldung mit dem Getippten', () => {
    const comp = create();
    const emit = spyOn(comp.moveMade, 'emit');
    expect(comp.keyboardMove('e5')).toBeFalse();
    expect(comp.kbdFeedback).toEqual({ key: 'puzzles.keyboardMove.invalid', params: { move: 'e5' } });
    expect(comp.keyboardMove('Nc3')).toBeFalse();          // legal, aber das Brett bietet b1 gerade nicht an
    expect(emit).not.toHaveBeenCalled();
  });

  it('Umwandlung ohne Figur öffnet die Auswahl, mit Figur geht sie gleich durch, autoQueen nimmt die Dame', () => {
    const ask = create(PROMO, [['a7', ['a8']]]);
    const emitAsk = spyOn(ask.moveMade, 'emit');
    expect(ask.keyboardMove('a8')).toBeTrue();
    expect(emitAsk).not.toHaveBeenCalled();
    expect(ask.pendingPromotion).toEqual({ orig: 'a7' as Key, dest: 'a8' as Key });
    expect(ask.promotionColor).toBe('w');

    const named = create(PROMO, [['a7', ['a8']]]);
    const emitNamed = spyOn(named.moveMade, 'emit');
    named.keyboardMove('a8=N');
    expect(emitNamed).toHaveBeenCalledWith({ orig: 'a7' as Key, dest: 'a8' as Key, promotion: 'n' });

    const kids = create(PROMO, [['a7', ['a8']]]);
    kids.autoQueen = true;
    const emitKids = spyOn(kids.moveMade, 'emit');
    kids.keyboardMove('a7a8');
    expect(emitKids).toHaveBeenCalledWith({ orig: 'a7' as Key, dest: 'a8' as Key, promotion: 'q' });
  });

  it('Viz-Modus: es zählt die tatsächliche Stellung, nicht das eingefrorene Brett', () => {
    const comp = create(START, []);                        // eingefrorenes Brett, chessground-dests leer
    comp.visualization = 2;
    comp.actualFen = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
    const emit = spyOn(comp.moveMade, 'emit');
    expect(comp.keyboardMove('Qh5')).toBeTrue();           // auf dem eingefrorenen Brett ginge Dh5 nicht
    expect(emit).toHaveBeenCalledWith({ orig: 'd1' as Key, dest: 'h5' as Key });
  });

  it('Viz-Modus nimmt wie das Antippen auch Züge der Gegenseite (Kalkulation: Varianten für beide Seiten)', () => {
    const comp = create(START, []);
    comp.visualization = 1;
    comp.actualFen = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';   // Schwarz am Zug
    const emit = spyOn(comp.moveMade, 'emit');
    expect(comp.keyboardMove('e5')).toBeTrue();
    expect(emit).toHaveBeenCalledWith({ orig: 'e7' as Key, dest: 'e5' as Key });
  });

  describe('gerendert', () => {
    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [PuzzleBoardComponent],
        providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
      }).compileComponents();
    });

    function render() {
      const fixture = TestBed.createComponent(PuzzleBoardComponent);
      fixture.componentInstance.dests = new Map([['g1', ['f3', 'h3']]]) as unknown as Map<Key, Key[]>;
      fixture.detectChanges();
      return fixture;
    }

    it('das Eingabefeld ist per Tab erreichbar, aber erst mit Fokus sichtbar; Enter zieht und leert das Feld', () => {
      const fixture = render();
      const el = fixture.nativeElement as HTMLElement;
      const box = el.querySelector('.kbd-move') as HTMLElement;
      const input = el.querySelector('input.kbd-input') as HTMLInputElement;
      expect(input).withContext('Zug-Eingabefeld').not.toBeNull();
      expect(input.tabIndex).toBe(0);
      expect(el.querySelector(`label[for="${input.id}"]`)!.textContent!.trim()).toBe('puzzles.keyboardMove.label');
      expect(box.getBoundingClientRect().width).withContext('ohne Fokus unsichtbar').toBeLessThanOrEqual(1);

      input.focus();
      expect(box.getBoundingClientRect().width).withContext('mit Fokus sichtbar').toBeGreaterThan(1);

      const emit = spyOn(fixture.componentInstance.moveMade, 'emit');
      input.value = 'g1f3';
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      expect(emit).toHaveBeenCalledWith({ orig: 'g1' as Key, dest: 'f3' as Key });
      expect(input.value).toBe('');

      input.value = 'Nc3';
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      fixture.detectChanges();
      const status = el.querySelector(`#${input.id}-status`) as HTMLElement;
      expect(status.getAttribute('role')).toBe('status');
      expect(input.getAttribute('aria-describedby')).toBe(status.id);
      expect(status.textContent!.trim()).toBe('puzzles.keyboardMove.invalid');
      expect(input.value).withContext('ein abgelehnter Zug bleibt zum Korrigieren stehen').toBe('Nc3');
    });

    it('das Brett hat einen Namen samt Seite am Zug, der letzte Zug steht in einer Live-Region', () => {
      const fixture = render();
      fixture.componentInstance.lastMove = ['e2' as Key, 'e4' as Key];
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      const board = el.querySelector('.cg-wrap') as HTMLElement;
      expect(board.getAttribute('role')).toBe('img');
      expect(board.getAttribute('aria-label')).toBe('puzzles.keyboardMove.board, common.whiteToMove');
      const live = el.querySelector('[aria-live="polite"]') as HTMLElement;
      expect(live.textContent!.trim()).toBe('puzzles.keyboardMove.lastMove');
    });
  });
});
