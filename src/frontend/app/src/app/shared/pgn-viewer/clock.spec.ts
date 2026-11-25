import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ClockChartComponent } from './clock-chart.component';
import { clockOf, formatClock, parseClockInput, shortClock, stripClock, withClock } from './clock.util';
import { parsePgnText } from './pgn-parser';

/** Restzeiten auf der Uhr (1.015.0). Die Lese-Fälle sind dieselben wie in `ScoresheetClockTests` (Backend). */
describe('clock.util', () => {
  it('liest die üblichen notierten Formen', () => {
    const cases: [string, number][] = [
      ['1:12', 72 * 60], ['1.12', 72 * 60], ['1,12', 72 * 60], ['1h12', 72 * 60], [" 1:12' ", 72 * 60],
      ['0:05:30', 5 * 60 + 30], ['1:30:00', 90 * 60], ['72', 72 * 60], ["72'", 72 * 60], ['72’', 72 * 60],
      ['72min', 72 * 60], ["72'30", 72 * 60 + 30], ["72'30''", 72 * 60 + 30], ['45:30', 45 * 60 + 30], ['0', 0],
    ];
    for (const [raw, seconds] of cases) expect(parseClockInput(raw)).withContext(raw).toBe(seconds);
  });

  it('erkennt, was keine Uhrzeit ist', () => {
    for (const raw of [null, '', '  ', 'Sf3', '1:75', "72'75", '999', '12.05.2026', '?']) {
      expect(parseClockInput(raw)).withContext(String(raw)).toBeNull();
    }
  });

  it('schreibt und liest [%clk]', () => {
    expect(formatClock(307)).toBe('0:05:07');
    expect(shortClock(72 * 60)).toBe('1:12');
    expect(shortClock(330)).toBe('0:05:30');
    expect(clockOf('gut [%clk 1:12:00] gespielt')).toBe(4320);
    expect(clockOf('nichts')).toBeNull();
    expect(stripClock('[%clk 1:12:00] Idee')).toBe('Idee');
    expect(stripClock('[%clk 1:12:00]')).toBeNull();
    expect(withClock('[%clk 1:12:00] Idee', 600)).toBe('[%clk 0:10:00] Idee');
    expect(withClock('Idee', null)).toBe('Idee');
    expect(withClock(null, 600)).toBe('[%clk 0:10:00]');
  });

  it('der PGN-Leser sammelt die Uhren je Halbzug, der Kommentartext bleibt ohne sie', () => {
    const [g] = parsePgnText('[White "a"]\n[Black "b"]\n\n1. e4 {[%clk 1:29:00]} e5 {[%clk 1:28:30] Idee} 2. Nf3 *');
    expect(g.clocks).toEqual({ 0: 5340, 1: 5310 });
    expect(g.comments[0]).toBeUndefined();
    expect(g.comments[1]).toBe('Idee');
    const [plain] = parsePgnText('[White "a"]\n[Black "b"]\n\n1. e4 e5 *');
    expect(plain.clocks).toBeUndefined();
  });
});

describe('ClockChartComponent', () => {
  function make(clocks: Record<number, number> | null, plies = 10) {
    TestBed.configureTestingModule({ imports: [ClockChartComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(ClockChartComponent);
    f.componentRef.setInput('clocks', clocks);
    f.componentRef.setInput('plies', plies);
    f.detectChanges();
    return f;
  }

  it('erscheint erst ab vier Uhrenständen', () => {
    expect(make({ 0: 5400, 1: 5400, 2: 5300 }).nativeElement.querySelector('.plot')).toBeNull();
  });

  it('zeichnet je Seite eine Linie und springt per Klick zum nächsten Punkt', () => {
    const f = make({ 0: 5400, 1: 5390, 2: 5000, 3: 5300, 8: 1200 });
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelectorAll('.dot.white').length).toBe(3);
    expect(el.querySelectorAll('.dot.black').length).toBe(2);
    expect(el.querySelector('polyline.white')!.getAttribute('points')!.split(' ').length).toBe(3);
    expect(el.querySelector('.axis.top')!.textContent!.trim()).toBe('1:30');
    expect(el.querySelector('.dot')!.getAttribute('title')).toBe('1. 1:30');

    const plot = el.querySelector<HTMLElement>('.plot')!;
    plot.style.width = '900px';
    const emitted: number[] = [];
    f.componentInstance.moveClicked.subscribe(i => emitted.push(i));
    const r = plot.getBoundingClientRect();
    plot.dispatchEvent(new MouseEvent('click', { clientX: r.right - 1, clientY: r.top + 10, bubbles: true }));
    expect(emitted).toEqual([8]);
  });

  it('eine Partie ab einer Stellung mit Schwarz am Zug tauscht die Seiten', () => {
    TestBed.configureTestingModule({ imports: [ClockChartComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(ClockChartComponent);
    f.componentRef.setInput('clocks', { 0: 600, 1: 590, 2: 580, 3: 570 });
    f.componentRef.setInput('plies', 4);
    f.componentRef.setInput('firstColor', 'b');
    f.detectChanges();
    const first = (f.nativeElement as HTMLElement).querySelector('.dot')!;
    expect(first.classList).toContain('black');
    expect(first.getAttribute('title')).toBe('1… 0:10');
  });
});
