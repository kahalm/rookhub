import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ChessBoardComponent } from './chess-board.component';
import {
  BoardFullscreenButtonComponent,
} from '../fullscreen/board-fullscreen-button.component';

/**
 * Testet die Aufräum-Garantie der Breite-0-Retry-Schleife (requestAnimationFrame):
 * Wird die Komponente während der Retries zerstört, darf kein weiterer initBoard-Lauf
 * Chessground/ResizeObserver auf dem abgekoppelten Element aufbauen.
 */
describe('ChessBoardComponent RAF cleanup', () => {
  let rafCb: FrameRequestCallback | null;
  let rafSpy: jasmine.Spy;
  let cancelSpy: jasmine.Spy;

  beforeEach(() => {
    rafCb = null;
    rafSpy = spyOn(window, 'requestAnimationFrame').and.callFake((cb: FrameRequestCallback) => { rafCb = cb; return 42; });
    cancelSpy = spyOn(window, 'cancelAnimationFrame');
  });

  function make(): any {
    const c = new ChessBoardComponent();
    // parentElement mit Breite 0 → erzwingt den Retry-Pfad (requestAnimationFrame).
    c.boardEl = { nativeElement: { parentElement: { clientWidth: 0 }, clientWidth: 0, style: {} } } as any;
    return c;
  }

  it('cancels the pending RAF on destroy and the late callback is a no-op', () => {
    const c = make();
    c.ngAfterViewInit();                 // hostWidth 0 → schedules a retry
    expect(rafSpy).toHaveBeenCalledTimes(1);

    c.ngOnDestroy();                     // destroyed → cancel the pending frame
    expect(cancelSpy).toHaveBeenCalledWith(42);

    // Die noch zugestellte RAF-Callback darf nichts mehr aufbauen.
    rafCb!(0);
    expect(rafSpy).toHaveBeenCalledTimes(1);   // kein erneutes Scheduling
    expect((c as any).ground).toBeUndefined();
    expect((c as any).resizeObserver).toBeUndefined();
  });
});

/**
 * Testet, dass das reine Anzeige-Brett interaktiv (nicht viewOnly) initialisiert wird,
 * damit Chessground die Rechtsklick-Zeichen-Listener bindet — aber jegliche
 * Figuren-Interaktion (Ziehen/Zug) ausgeschaltet bleibt.
 */
describe('ChessBoardComponent right-click drawing', () => {
  it('boots interactive with drawing enabled but no piece movement', () => {
    const host = document.createElement('div');
    host.style.width = '320px';
    document.body.appendChild(host);
    const inner = document.createElement('div');
    host.appendChild(inner);

    const c: any = new ChessBoardComponent();
    c.boardEl = { nativeElement: inner };
    c.ngAfterViewInit();

    const state = c.ground.state;
    expect(state.viewOnly).toBe(false);        // sonst würden die Listener nicht gebunden
    expect(state.drawable.enabled).toBe(true); // Pfeile/Kreise per Rechtsklick
    expect(state.movable.color).toBeUndefined();
    expect(state.draggable.enabled).toBe(false);

    c.ngOnDestroy();
    document.body.removeChild(host);
  });
});

describe('ChessBoardComponent Pfeile des Aufrufers (bester Zug)', () => {
  it('legt die Pfeile als autoShapes an, beim Ändern neu, und lässt selbst gezogene Pfeile in Ruhe', () => {
    const host = document.createElement('div');
    host.style.width = '320px';
    document.body.appendChild(host);
    const inner = document.createElement('div');
    host.appendChild(inner);

    const c: any = new ChessBoardComponent();
    c.boardEl = { nativeElement: inner };
    c.arrows = [{ from: 'e2', to: 'e4' }];
    c.ngAfterViewInit();
    expect(c.ground.state.drawable.autoShapes).toEqual([{ orig: 'e2', dest: 'e4', brush: 'green' }]);

    c.ground.state.drawable.shapes = [{ orig: 'a1', dest: 'a8', brush: 'red' }];   // per Rechtsklick gezogen
    c.arrows = [];
    c.ngOnChanges({ arrows: {} as never });
    expect(c.ground.state.drawable.autoShapes).toEqual([]);
    expect(c.ground.state.drawable.shapes.length).toBe(1);

    c.ngOnDestroy();
    document.body.removeChild(host);
  });
});

describe('ChessBoardComponent Symbol am Feld (Zug-Klasse)', () => {
  it('zeichnet das Symbol als customSvg neben den Pfeilen und nimmt es beim Leeren wieder weg', () => {
    const host = document.createElement('div');
    host.style.width = '320px';
    document.body.appendChild(host);
    const inner = document.createElement('div');
    host.appendChild(inner);

    const c: any = new ChessBoardComponent();
    c.boardEl = { nativeElement: inner };
    c.arrows = [{ from: 'e2', to: 'e4' }];
    c.badge = { square: 'e4', svg: '<circle r="10"/>' };
    c.ngAfterViewInit();
    expect(c.ground.state.drawable.autoShapes).toEqual([
      { orig: 'e2', dest: 'e4', brush: 'green' },
      { orig: 'e4', customSvg: { html: '<circle r="10"/>' } },
    ]);

    c.badge = null;
    c.ngOnChanges({ badge: {} as never });
    expect(c.ground.state.drawable.autoShapes).toEqual([{ orig: 'e2', dest: 'e4', brush: 'green' }]);

    c.ngOnDestroy();
    document.body.removeChild(host);
  });
});

describe('ChessBoardComponent Symbol erst nach der Fahrt der Figur', () => {
  it('neue Stellung: altes Symbol sofort weg, neues erst, wenn die Figur angekommen ist', fakeAsync(() => {
    const host = document.createElement('div');
    host.style.width = '320px';
    document.body.appendChild(host);
    const inner = document.createElement('div');
    host.appendChild(inner);
    const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
    const AFTER_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';

    const c: any = new ChessBoardComponent();
    c.boardEl = { nativeElement: inner };
    c.fen = AFTER_E4;
    c.badge = { square: 'e4', svg: '<circle r="1"/>' };
    c.ngAfterViewInit();
    expect(c.ground.state.drawable.autoShapes.length).toBe(1);        // erster Aufbau: sofort

    c.fen = AFTER_E5;                                                   // → : die Figur fährt …
    c.ngOnChanges({ fen: { firstChange: false } as never });
    expect(c.ground.state.drawable.autoShapes).toEqual([]);            // … das alte Symbol ist sofort weg
    c.badge = { square: 'e5', svg: '<circle r="2"/>' };                 // der Rückblick meldet das neue
    c.ngOnChanges({ badge: {} as never });
    expect(c.ground.state.drawable.autoShapes).toEqual([]);            // … und es wartet
    tick(ChessBoardComponent.AnimationMs + ChessBoardComponent.BadgeSettleMs);
    expect(c.ground.state.drawable.autoShapes).toEqual([{ orig: 'e5', customSvg: { html: '<circle r="2"/>' } }]);

    c.fen = START;                                                      // an den Anfang: kein Symbol mehr
    c.ngOnChanges({ fen: { firstChange: false } as never });
    c.badge = null;
    c.ngOnChanges({ badge: {} as never });
    tick(500);
    expect(c.ground.state.drawable.autoShapes).toEqual([]);

    c.ngOnDestroy();
    document.body.removeChild(host);
  }));
});

describe('ChessBoardComponent Vollbild', () => {
  it('schickt die äußere Hülle ins Vollbild und hängt den Knopf ans Brett', async () => {
    await TestBed.configureTestingModule({
      imports: [ChessBoardComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ChessBoardComponent);
    fixture.detectChanges();

    const target: HTMLElement =
      fixture.debugElement.query(By.directive(BoardFullscreenButtonComponent)).componentInstance.target;
    // Die Größe des Vollbild-Elements erzwingt der Browser — daher Hülle ins Vollbild, Brett darin
    // als zentriertes Quadrat der kleineren Bildschirmseite.
    expect(target.classList).toContain('cb-fs-host');
    expect(fixture.nativeElement.querySelector('.cb-fs-host .board-fs-btn')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.cb-wrap .board-fs-btn')).toBeNull();
  });
});

/**
 * Zugfolge wie in der Punktepartie: Der Aufrufer setzt den gemeldeten Zug NICHT selbst um,
 * sondern sperrt das Brett (playable=false) und bindet nach der Serverantwort die naechste
 * Stellung zurueck. Danach muss das Brett wieder bespielbar sein — mit den Zielfeldern der
 * NEUEN Stellung, nicht denen der alten.
 */
describe('ChessBoardComponent Zugfolge (Punktepartie)', () => {
  const FEN8 = 'rn1qkbnr/ppp2ppp/3p4/4P3/4P3/5b2/PPP2PPP/RNBQKB1R w KQkq - 0 5';
  const FEN10 = 'rn1qkbnr/ppp2ppp/8/4p3/4P3/5Q2/PPP2PPP/RNB1KB1R w KQkq - 0 6';

  function make(fen: string) {
    const host = document.createElement('div');
    host.style.width = '400px';
    document.body.appendChild(host);
    const el = document.createElement('div');
    host.appendChild(el);
    const c = new ChessBoardComponent();
    (c as any).boardEl = { nativeElement: el };
    c.fen = fen;
    c.playable = true;
    c.ngAfterViewInit();
    return { c, host };
  }

  it('bindet die naechste Stellung zurueck und ist wieder bespielbar', () => {
    const { c, host } = make(FEN8);
    const ground = (c as any).ground;
    expect(ground).withContext('Chessground initialisiert').toBeDefined();

    let emitted: any = null;
    c.userMove.subscribe((m: any) => (emitted = m));
    (c as any).onBoardMove('d1', 'f3');
    expect(emitted.san).toBe('Qxf3');

    // Das macht Chessground bei einem echten Nutzerzug SELBST (`baseUserMove`): es dreht seinen
    // eigenen `turnColor` um. Genau dieser Zustand wird von einer neu gesetzten FEN nicht
    // zurueckgestellt — deshalb hier nachstellen, sonst prueft der Test die halbe Wahrheit.
    ground.state.turnColor = 'black';
    ground.state.movable.dests = undefined;

    // Elternteil: busy -> gesperrt, FEN noch die alte
    c.playable = false;
    c.ngOnChanges({ playable: {} as any });

    // Serverantwort: naechste Stellung, wieder frei
    c.fen = FEN10;
    c.playable = true;
    c.ngOnChanges({ fen: {} as any, playable: {} as any });

    const st = (c as any).ground.state;
    expect(st.pieces.get('f3')?.role).withContext('weisse Dame steht auf f3').toBe('queen');
    expect(st.pieces.get('d6')).withContext('d6 ist leer').toBeUndefined();
    expect(st.movable.color).withContext('Weiss ist am Zug').toBe('white');
    const dests = st.movable.dests as Map<string, string[]> | undefined;
    expect(dests).withContext('Zielfelder vorhanden').toBeTruthy();
    expect(Array.from(dests!.get('f1') ?? [])).withContext('Lf1 kann nach c4').toContain('c4');
    // Der eigentliche Fund: ohne `turnColor` bleibt er auf Schwarz stehen, `isMovable` weist dann
    // JEDEN weiteren Zug ab — das Brett zeigt noch die Zielfelder, fuehrt den Zug aber nicht aus.
    expect(st.turnColor).withContext('Seite am Zug passt zur FEN').toBe('white');

    c.ngOnDestroy();
    host.remove();
  });
});
