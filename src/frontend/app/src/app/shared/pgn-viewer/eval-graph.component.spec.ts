import { TestBed } from '@angular/core/testing';
import { EvalGraphComponent, EvalGraphMark } from './eval-graph.component';

describe('EvalGraphComponent', () => {
  function setup(series: (number | null)[], opts: { marks?: EvalGraphMark[]; currentIndex?: number } = {}) {
    TestBed.configureTestingModule({ imports: [EvalGraphComponent] });
    const fixture = TestBed.createComponent(EvalGraphComponent);
    fixture.componentRef.setInput('series', series);
    if (opts.marks) fixture.componentRef.setInput('marks', opts.marks);
    if (opts.currentIndex !== undefined) fixture.componentRef.setInput('currentIndex', opts.currentIndex);
    (fixture.nativeElement as HTMLElement).style.width = '400px';
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const lines = () => Array.from(el.querySelectorAll('polyline.line'))
      .map(l => (l.getAttribute('points') ?? '').split(' ').filter(Boolean));
    return { fixture, el, lines };
  }

  it('zeichnet je gerechneter Stellung einen Punkt, Start bis Endstellung über die ganze Breite', () => {
    const { lines } = setup([50, 60, 40, 55]);
    expect(lines().length).toBe(1);
    expect(lines()[0]).toEqual(['0,50', '333.33,40', '666.67,60', '1000,45']);
  });

  it('eine Lücke bricht die Linie, statt sie zu überbrücken', () => {
    const { lines } = setup([50, 60, null, 55, 45]);
    expect(lines().length).toBe(2);
    expect(lines()[0]).toEqual(['0,50', '250,40']);
    expect(lines()[1]).toEqual(['750,45', '1000,55']);
  });

  it('ein einzelner Punkt zwischen zwei Lücken wird als Punkt gezeigt, nicht als Linie', () => {
    const { el, lines } = setup([null, 60, null]);
    expect(lines().length).toBe(0);
    expect(el.querySelectorAll('.dot.lone').length).toBe(1);
  });

  // Wie chess.com (Vergleich 2026-09-24): weiß ist alles UNTER der Kurve, darüber der dunkle Grund.
  it('Fläche: weiß vom unteren Rand bis zur Kurve, je zusammenhängendem Lauf', () => {
    const { el } = setup([50, 70, 30]);
    const polygons = el.querySelectorAll('polygon.area-white');
    expect(polygons.length).toBe(1);
    expect(polygons[0].getAttribute('points')).toBe('0,100 0,50 500,30 1000,70 1000,100');
    expect(el.querySelector('polygon.area-black')).toBeNull();
  });

  it('eine nicht gerechnete Strecke bekommt ein neutrales Band — sonst sähe sie wie ein schwarzer Sieg aus', () => {
    const { el } = setup([50, 60, null, 55, 45]);
    const gaps = Array.from(el.querySelectorAll('rect.gap')).map(r => [r.getAttribute('x'), r.getAttribute('width')]);
    expect(gaps).toEqual([['250', '500']]);   // von Stellung 1 bis Stellung 3
    expect(el.querySelectorAll('polygon.area-white').length).toBe(2);
  });

  it('läuft die Analyse noch, reicht das Band bis zum rechten Rand', () => {
    const { el } = setup([50, 60, null, null]);
    const gaps = Array.from(el.querySelectorAll('rect.gap')).map(r => [r.getAttribute('x'), r.getAttribute('width')]);
    expect(gaps).toEqual([['333.33', '666.67']]);
  });

  it('Fehler und grobe Fehler als Punkt auf der Stellung NACH dem Zug', () => {
    const { el } = setup([50, 60, 20, 25], {
      marks: [{ ply: 1, kind: 'blunder', color: '#ca3431' }, { ply: 2, kind: 'mistake', color: '#e58f2a' }],
    });
    const blunder = el.querySelector('.dot.blunder') as HTMLElement;
    expect(blunder).not.toBeNull();
    expect(parseFloat(blunder.style.left)).toBeCloseTo(66.667, 2);   // Stellung 2 von 3
    expect(parseFloat(blunder.style.top)).toBe(80);                    // 100 − 20 %
    expect(el.querySelectorAll('.dot.mistake').length).toBe(1);
  });

  it('kein Punkt auf einer Lücke', () => {
    const { el } = setup([50, 60, null], { marks: [{ ply: 1, kind: 'blunder', color: '#ca3431' }] });
    expect(el.querySelector('.dot.blunder')).toBeNull();
  });

  it('Brilliant, Great und Miss bekommen ihren Punkt — in der Farbe, die der Aufrufer mitgibt', () => {
    const { el } = setup([50, 60, 40, 55, 45], {
      marks: [
        { ply: 0, kind: 'brilliant', color: '#26c2a3' },
        { ply: 1, kind: 'great', color: '#5b8fd6' },
        { ply: 2, kind: 'miss', color: '#ee6b55' },
      ],
    });
    expect((el.querySelector('.dot.brilliant') as HTMLElement).style.backgroundColor).toBe('rgb(38, 194, 163)');
    expect((el.querySelector('.dot.great') as HTMLElement).style.backgroundColor).toBe('rgb(91, 143, 214)');
    expect((el.querySelector('.dot.miss') as HTMLElement).style.backgroundColor).toBe('rgb(238, 107, 85)');
    expect(parseFloat((el.querySelector('.dot.great') as HTMLElement).style.left)).toBe(50);   // Stellung 2 von 4
  });

  it('Marke am aktuellen Zug (−1 = Startstellung ganz links)', () => {
    const { fixture, el } = setup([50, 60, 40, 55, 45], { currentIndex: 1 });
    expect(el.querySelector('line.cursor')!.getAttribute('x1')).toBe('500');
    fixture.componentRef.setInput('currentIndex', -1);
    fixture.detectChanges();
    expect(el.querySelector('line.cursor')!.getAttribute('x1')).toBe('0');
  });

  it('Klick springt zum nächstgelegenen Zug; links der ersten Marke ist die Startstellung (−1)', () => {
    const { fixture, el } = setup([50, 60, 40, 55, 45]);   // 4 Züge
    const emitted: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(i => emitted.push(i));
    const graph = el.querySelector('.graph') as HTMLElement;
    const rect = el.getBoundingClientRect();
    const clickAt = (fraction: number) =>
      graph.dispatchEvent(new MouseEvent('click', { clientX: rect.left + fraction * rect.width, bubbles: true }));

    clickAt(0.05);   // nahe Stellung 0
    clickAt(0.3);    // Stellung 1 = nach Zug 0
    clickAt(1);      // Endstellung = nach Zug 3
    expect(emitted).toEqual([-1, 0, 3]);
  });

  it('ohne Züge: keine Marke, Klick tut nichts', () => {
    const { fixture, el } = setup([50]);
    const emitted: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(i => emitted.push(i));
    (el.querySelector('.graph') as HTMLElement).click();
    expect(el.querySelector('line.cursor')).toBeNull();
    expect(emitted).toEqual([]);
  });
});
