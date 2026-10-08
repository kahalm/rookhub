import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { START_PLACEMENT, boardFromPlacement } from '../core/position-setup';
import { LONG_PRESS_MS, SetupBoardComponent } from './setup-board.component';

describe('SetupBoardComponent', () => {
  let fixture: ComponentFixture<SetupBoardComponent>;
  let c: SetupBoardComponent;
  let host: HTMLElement;
  const ev = { click: [] as number[], alt: [] as number[], moved: [] as { from: number; to: number }[],
    dropped: [] as { piece: string; to: number }[], removed: [] as { from: number }[] };

  function render(flipped = false, altPlace = false): void {
    fixture.componentRef.setInput('board', boardFromPlacement(START_PLACEMENT)!);
    fixture.componentRef.setInput('flipped', flipped);
    fixture.componentRef.setInput('altPlace', altPlace);
    fixture.detectChanges();
  }

  const root = (): HTMLElement => host.querySelector('.sb') as HTMLElement;
  const sq = (index: number): HTMLElement => host.querySelector(`[data-i="${index}"]`) as HTMLElement;
  /** Mitte eines Feldes auf dem Bildschirm. */
  function center(index: number): { x: number; y: number } {
    const r = sq(index).getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }
  function pointer(type: string, target: EventTarget, x: number, y: number, extra: PointerEventInit = {}): void {
    target.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true, pointerId: 7, clientX: x, clientY: y,
      button: 0, pointerType: 'mouse', ...extra }));
  }
  /** down auf `from` → move (in Schritten) → up an (x, y); danach der Klick, den der Browser schickt. */
  function dragFromSquare(from: number, x: number, y: number, extra: PointerEventInit = {}): void {
    const s = center(from);
    pointer('pointerdown', sq(from), s.x, s.y, extra);
    pointer('pointermove', window, s.x + 3, s.y + 3, extra);
    pointer('pointermove', window, x, y, extra);
    fixture.detectChanges();
    pointer('pointerup', window, x, y, extra);
    sq(from).click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [SetupBoardComponent] });
    fixture = TestBed.createComponent(SetupBoardComponent);
    c = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = '320px';
    ev.click = []; ev.alt = []; ev.moved = []; ev.dropped = []; ev.removed = [];
    c.squareClick.subscribe(i => ev.click.push(i));
    c.squareAltClick.subscribe(i => ev.alt.push(i));
    c.pieceMoved.subscribe(e => ev.moved.push(e));
    c.pieceDropped.subscribe(e => ev.dropped.push(e));
    c.pieceRemoved.subscribe(e => ev.removed.push(e));
  });

  it('Ziehen auf ein anderes Feld meldet pieceMoved, der Klick danach wird verschluckt', () => {
    render();
    const to = center(36);   // e4
    const s = center(52);
    pointer('pointerdown', sq(52), s.x, s.y);
    pointer('pointermove', window, to.x, to.y);
    fixture.detectChanges();
    // Während des Ziehens: Geisterfigur, Ausgangsfeld abgedunkelt, Ziel hervorgehoben
    expect(host.querySelector('.sb-ghost')).not.toBeNull();
    expect(sq(52).classList).toContain('from');
    expect(sq(36).classList).toContain('over');
    pointer('pointerup', window, to.x, to.y);
    sq(52).click();
    fixture.detectChanges();
    expect(ev.moved).toEqual([{ from: 52, to: 36 }]);
    expect(ev.click).toEqual([]);
    expect(host.querySelector('.sb-ghost')).toBeNull();
  });

  it('Loslassen außerhalb des Bretts entfernt, zurück aufs Ausgangsfeld bricht ab', () => {
    render();
    const r = root().getBoundingClientRect();
    dragFromSquare(52, r.right + 50, r.top + 10);
    expect(ev.removed).toEqual([{ from: 52 }]);
    const s = center(52), away = center(36);
    pointer('pointerdown', sq(52), s.x, s.y);
    pointer('pointermove', window, away.x, away.y);   // weg (über der Schwelle) …
    pointer('pointermove', window, s.x + 1, s.y + 1); // … und wieder zurück
    pointer('pointerup', window, s.x + 1, s.y + 1);
    sq(52).click();
    expect(ev.moved).toEqual([]);
    expect(ev.removed.length).toBe(1);
    expect(ev.click).toEqual([]);
  });

  it('Escape bricht das Ziehen ab, ohne den Dialog zu schließen', () => {
    render();
    let reachedDocument = false;
    const spy = (): void => { reachedDocument = true; };
    document.addEventListener('keydown', spy);
    const s = center(52), to = center(36);
    pointer('pointerdown', sq(52), s.x, s.y);
    pointer('pointermove', window, to.x, to.y);
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    pointer('pointerup', window, to.x, to.y);
    document.removeEventListener('keydown', spy);
    document.body.click();   // den verschluckten Klick verbrauchen
    fixture.detectChanges();
    expect(ev.moved).toEqual([]);
    expect(reachedDocument).toBeFalse();
    expect(c.drag()).toBeNull();
  });

  it('kurzer Tipp ohne Bewegung bleibt ein Klick', () => {
    render();
    const s = center(52);
    pointer('pointerdown', sq(52), s.x, s.y);
    pointer('pointermove', window, s.x + 3, s.y + 2);   // unter der Schwelle
    pointer('pointerup', window, s.x + 3, s.y + 2);
    sq(52).click();
    expect(ev.click).toEqual([52]);
    expect(ev.moved).toEqual([]);
    sq(27).click();   // leeres Feld
    expect(ev.click).toEqual([52, 27]);
  });

  it('aus der Palette aufs Brett ziehen meldet pieceDropped; daneben loslassen tut nichts', () => {
    render();
    const r = root().getBoundingClientRect();
    const to = center(27);
    c.paletteDown('q', new PointerEvent('pointerdown', { pointerId: 7, clientX: r.left, clientY: r.bottom + 40, button: 0, pointerType: 'mouse' }));
    pointer('pointermove', window, to.x, to.y);
    pointer('pointerup', window, to.x, to.y);
    expect(ev.dropped).toEqual([{ piece: 'q', to: 27 }]);
    document.body.click();
    c.paletteDown('N', new PointerEvent('pointerdown', { pointerId: 7, clientX: r.left, clientY: r.bottom + 40, button: 0, pointerType: 'mouse' }));
    pointer('pointermove', window, r.left + 20, r.bottom + 80);
    pointer('pointerup', window, r.left + 20, r.bottom + 80);
    expect(ev.dropped.length).toBe(1);
    document.body.click();
  });

  it('gedrehtes Brett: Feld unter dem Zeiger stimmt (oben links = h1)', () => {
    render(true);
    const r = root().getBoundingClientRect();
    expect(c.indexAt(r.left + 2, r.top + 2)).toBe(63);
    expect(c.indexAt(r.right - 2, r.bottom - 2)).toBe(0);
    dragFromSquare(52, center(36).x, center(36).y);   // e2 → e4, auch gedreht
    expect(ev.moved).toEqual([{ from: 52, to: 36 }]);
    expect(c.indexAt(r.left - 5, r.top + 5)).toBeNull();
  });

  it('Rechtsklick meldet squareAltClick und unterdrückt das Kontextmenü', () => {
    render(false, true);
    const s = center(27);
    pointer('pointerdown', sq(27), s.x, s.y, { button: 2 });
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: s.x, clientY: s.y, button: 2 });
    sq(27).dispatchEvent(menu);
    expect(menu.defaultPrevented).toBeTrue();
    expect(ev.alt).toEqual([27]);
    expect(ev.click).toEqual([]);
  });

  it('langer Druck (Touch) ohne Bewegung meldet squareAltClick, der Tipp danach zählt nicht', fakeAsync(() => {
    render(false, true);
    const s = center(27);
    pointer('pointerdown', sq(27), s.x, s.y, { pointerType: 'touch' });
    tick(LONG_PRESS_MS + 10);
    expect(ev.alt).toEqual([27]);
    // Das Kontextmenü, das der Browser auf langen Druck schickt, löst nicht ein zweites Mal aus
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: s.x, clientY: s.y });
    sq(27).dispatchEvent(menu);
    expect(menu.defaultPrevented).toBeTrue();
    pointer('pointerup', window, s.x, s.y, { pointerType: 'touch' });
    sq(27).click();
    expect(ev.alt).toEqual([27]);
    expect(ev.click).toEqual([]);
    tick(600);
  }));

  it('langer Druck auf einer Figur, aber vorher bewegt = Ziehen statt Schwarz setzen', fakeAsync(() => {
    render(false, true);
    dragFromSquare(52, center(36).x, center(36).y, { pointerType: 'touch' });
    tick(LONG_PRESS_MS + 10);
    expect(ev.moved).toEqual([{ from: 52, to: 36 }]);
    expect(ev.alt).toEqual([]);
    tick(600);
  }));

  it('ohne gewählte Figur (altPlace aus) kein langer Druck', fakeAsync(() => {
    render(false, false);
    const s = center(27);
    pointer('pointerdown', sq(27), s.x, s.y, { pointerType: 'touch' });
    tick(LONG_PRESS_MS + 10);
    pointer('pointerup', window, s.x, s.y, { pointerType: 'touch' });
    expect(ev.alt).toEqual([]);
  }));
});
