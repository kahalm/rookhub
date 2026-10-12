import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ClampMoreDirective } from './clamp-more.directive';

@Component({
  standalone: true,
  imports: [ClampMoreDirective],
  template: `
    <p id="t" [style.width.px]="width()" style="margin:0;line-height:20px;font-size:14px"
       [appClampMore]="text()" [clampLines]="lines()" [clampExpanded]="open()"><span class="clamp-text"></span><button
       type="button" class="clamp-more">Mehr</button></p>
  `,
})
class HostComponent {
  readonly text = signal('');
  readonly width = signal(300);
  readonly lines = signal(3);
  readonly open = signal(false);
}

const WORDS = Array.from({ length: 80 }, (_, i) => `Wort${i}`).join(' ');

describe('ClampMoreDirective', () => {
  function setup(text: string, width = 300, lines = 3) {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.componentInstance.text.set(text);
    fixture.componentInstance.width.set(width);
    fixture.componentInstance.lines.set(lines);
    fixture.detectChanges();
    const p = fixture.nativeElement.querySelector('p') as HTMLElement;
    const textEl = p.querySelector('.clamp-text') as HTMLElement;
    const more = p.querySelector('.clamp-more') as HTMLElement;
    return { fixture, p, textEl, more, done: () => (fixture.nativeElement as HTMLElement).remove() };
  }

  it('kürzt am Wortende auf höchstens N Zeilen, „Mehr" steht dahinter', () => {
    for (const [width, lines] of [[200, 3], [300, 3], [500, 2]] as const) {
      const { p, textEl, more, done } = setup(WORDS, width, lines);
      try {
        expect(Math.round(p.getBoundingClientRect().height / 20)).withContext(`${width}px`).toBeLessThanOrEqual(lines);
        expect(textEl.textContent!.endsWith(' …')).toBeTrue();
        expect(WORDS.startsWith(textEl.textContent!.slice(0, -2))).toBeTrue();
        expect(more.hidden).toBeFalse();
        // so lang wie möglich: noch ein Wort mehr passte nicht
        expect(textEl.textContent!.length).toBeGreaterThan(width / 10 * (lines - 1));
      } finally {
        done();
        TestBed.resetTestingModule();
      }
    }
  });

  it('kurzer Text bleibt ganz, ohne Knopf', () => {
    const { textEl, more, done } = setup('Kurz.');
    try {
      expect(textEl.textContent).toBe('Kurz.');
      expect(more.hidden).toBeTrue();
    } finally { done(); }
  });

  it('aufgeklappt: ganzer Text, Knopf sichtbar; zugeklappt wieder gekürzt', () => {
    const { fixture, textEl, more, done } = setup(WORDS);
    try {
      fixture.componentInstance.open.set(true);
      fixture.detectChanges();
      expect(textEl.textContent).toBe(WORDS);
      expect(more.hidden).toBeFalse();
      fixture.componentInstance.open.set(false);
      fixture.detectChanges();
      expect(textEl.textContent!.endsWith(' …')).toBeTrue();
    } finally { done(); }
  });

  it('ohne Breite (verborgen) steht der ganze Text, ohne Knopf', () => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    (fixture.nativeElement as HTMLElement).style.display = 'none';
    fixture.componentInstance.text.set(WORDS);
    fixture.detectChanges();
    const p = fixture.nativeElement.querySelector('p') as HTMLElement;
    expect(p.querySelector('.clamp-text')!.textContent).toBe(WORDS);
    expect((p.querySelector('.clamp-more') as HTMLElement).hidden).toBeTrue();
  });
});
