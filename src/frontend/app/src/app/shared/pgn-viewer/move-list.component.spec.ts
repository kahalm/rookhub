import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Chess } from 'chess.js';
import { MoveListComponent } from './move-list.component';

function build(component: MoveListComponent, fen: string, sans: string[]) {
  const chess = new Chess(fen);
  sans.forEach(s => chess.move(s));
  component.moves = chess.history({ verbose: true });
  (component as unknown as { buildPairs: () => void }).buildPairs();
}

describe('MoveListComponent numbering', () => {
  let component: MoveListComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [MoveListComponent] });
    component = TestBed.createComponent(MoveListComponent).componentInstance;
  });

  it('numbers a normal white-to-move game as N. white black', () => {
    build(component, 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', ['e4', 'e5', 'Nf3']);
    expect(component.movePairs[0]).toEqual(jasmine.objectContaining({ number: 1, white: 'e4', black: 'e5' }));
    expect(component.movePairs[1]).toEqual(jasmine.objectContaining({ number: 2, white: 'Nf3' }));
    expect(component.movePairs[1].black).toBeUndefined();
  });

  it('numbers a black-to-move start as "N... black" (not white)', () => {
    // FEN: Schwarz am Zug, Vollzug 1
    build(component, 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', ['e5', 'Nf3']);
    expect(component.movePairs[0]).toEqual(jasmine.objectContaining({ number: 1, black: 'e5' }));
    expect(component.movePairs[0].white).toBeUndefined();
    expect(component.movePairs[1]).toEqual(jasmine.objectContaining({ number: 2, white: 'Nf3' }));
  });
});

describe('MoveListComponent Kommentare', () => {
  const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  function render(comments: { [i: number]: string }, segments: any = null) {
    TestBed.configureTestingModule({ imports: [MoveListComponent] });
    const fixture = TestBed.createComponent(MoveListComponent);
    const chess = new Chess(START_FEN);
    ['e4', 'e6', 'Nf3', 'd5'].forEach(s => chess.move(s));
    fixture.componentRef.setInput('moves', chess.history({ verbose: true }));
    fixture.componentRef.setInput('comments', comments);
    fixture.componentRef.setInput('commentSegments', segments);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const rowText = (r: Element) => r.classList.contains('move-row')
      ? Array.from(r.children).map(c => c.textContent!.trim()).filter(t => t).join(' ')
      : r.textContent!.replace(/\s+/g, ' ').trim();
    return { fixture, el, rows: () => Array.from(el.querySelectorAll('.move-row, .comment-row')).map(rowText) };
  }

  it('zeigt die Einleitung vor dem ersten Zug', () => {
    const { rows } = render({ [-1]: 'Willkommen' });
    expect(rows()[0]).toBe('Willkommen');
  });

  it('zeigt beide Kommentare eines Zugpaars — Schwarz rückt in eine eigene Zeile', () => {
    const { rows } = render({ 0: 'Weiß', 1: 'Schwarz' });
    expect(rows()).toEqual(['1. e4', 'Weiß', '1. e6', 'Schwarz', '2. Nf3 d5']);
  });

  it('macht Züge anklickbar, wenn Stücke übergeben werden, und meldet den Klick', () => {
    const seg = { move: '2.d4', fen: 'x', from: 'd2', to: 'd4' };
    const { fixture, el } = render({ [-1]: 'dass er 2.d4 spielt' },
      { [-1]: [{ text: 'dass er ' }, seg, { text: ' spielt' }] });
    const clicked: unknown[] = [];
    fixture.componentInstance.commentMoveClicked.subscribe(s => clicked.push(s));

    const chip = el.querySelector('.comment-row .cmt-move') as HTMLButtonElement;
    expect(chip.textContent!.trim()).toBe('2.d4');
    chip.click();

    expect(clicked).toEqual([seg]);
  });
});

describe('MoveListComponent: aktiver Zug scrollt nur die Zugliste, nie die Seite', () => {
  const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  // 40 Halbzüge — genug Zeilen, dass ein 60-px-Kasten überläuft.
  const SANS = ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'a6', 'Ba4', 'Nf6', 'O-O', 'Be7', 'Re1', 'b5', 'Bb3', 'd6', 'c3', 'O-O',
    'h3', 'Nb8', 'd4', 'Nbd7', 'Nbd2', 'Bb7', 'Bc2', 'Re8', 'Nf1', 'Bf8', 'Ng3', 'g6', 'a4', 'c5', 'd5', 'c4', 'Bg5', 'h6',
    'Be3', 'Nc5', 'Qd2', 'h5', 'Bg5', 'Be7'];
  let wrapper: HTMLElement;
  let filler: HTMLElement;
  let scrolled: Element[];

  function render(wrapperStyle: string) {
    TestBed.configureTestingModule({ imports: [MoveListComponent] });
    const fixture = TestBed.createComponent(MoveListComponent);
    // Die Seite ist lang und die Zugliste steht weit unten — so wie am Handy unter dem Brett.
    filler = document.createElement('div');
    filler.style.height = '3000px';
    wrapper = document.createElement('div');
    wrapper.setAttribute('style', wrapperStyle);
    wrapper.appendChild(fixture.nativeElement);
    document.body.appendChild(filler);
    document.body.appendChild(wrapper);
    const chess = new Chess(START_FEN);
    SANS.forEach(s => chess.move(s));
    fixture.componentRef.setInput('moves', chess.history({ verbose: true }));
    fixture.componentRef.setInput('currentMoveIndex', -1);
    fixture.detectChanges();
    tick();   // der Scroll-Timer des Startzustands (kein aktiver Zug) läuft ab, bevor gezählt wird
    return fixture;
  }

  beforeEach(() => {
    window.scrollTo(0, 0);
    scrolled = [];
    // Der Ersatz scrollt wirklich (ohne „smooth"), damit ein zweiter Aufruf nichts mehr zu tun hätte.
    spyOn(Element.prototype, 'scrollTo').and.callFake(function (this: Element, opts?: ScrollToOptions | number) {
      scrolled.push(this);
      if (typeof opts === 'object' && opts?.top != null) this.scrollTop = opts.top;
    });
    spyOn(Element.prototype, 'scrollIntoView');
  });

  afterEach(() => { wrapper?.remove(); filler?.remove(); });

  it('scrollt den umgebenden Kasten zum aktiven Zug — ohne scrollIntoView, die Seite bleibt stehen', fakeAsync(() => {
    const fixture = render('height: 60px; overflow-y: auto');
    fixture.componentRef.setInput('currentMoveIndex', SANS.length - 1);
    fixture.detectChanges();
    tick();

    expect(Element.prototype.scrollIntoView).not.toHaveBeenCalled();
    expect(scrolled.length).toBe(1);
    expect(wrapper.contains(scrolled[0])).toBeTrue();
    expect(scrolled[0]).not.toBe(document.documentElement);
    expect((scrolled[0] as HTMLElement).scrollTop).toBeGreaterThan(0);
    expect(window.scrollY).toBe(0);
  }));

  it('ohne scrollbaren Kasten (nur die Seite könnte scrollen) passiert gar nichts', fakeAsync(() => {
    const fixture = render('');
    fixture.componentRef.setInput('currentMoveIndex', SANS.length - 1);
    fixture.detectChanges();
    tick();

    expect(Element.prototype.scrollIntoView).not.toHaveBeenCalled();
    expect(scrolled.length).toBe(0);
    expect(window.scrollY).toBe(0);
  }));
});
