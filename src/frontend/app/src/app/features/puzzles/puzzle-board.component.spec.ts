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
