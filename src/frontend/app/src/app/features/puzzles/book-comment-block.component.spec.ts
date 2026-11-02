import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { BookCommentBlockComponent } from './book-comment-block.component';
import { CommentSegment } from './comment-variation.util';

describe('BookCommentBlockComponent (Zerlegung Z18)', () => {
  function make(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [BookCommentBlockComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(BookCommentBlockComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }
  const seg: CommentSegment = { move: 'Nf3', fen: 'x', from: 'g1', to: 'f3' } as CommentSegment;

  it('rendert Absätze mit Zug-Chip; der Chip meldet die Variante', () => {
    const { el, c } = make({ hasLines: true, blocks: [[{ text: 'Gut ist ' }, seg]] });
    const picked: CommentSegment[] = [];
    c.movePicked.subscribe(s => picked.push(s));
    (el.querySelector('.cmt-move') as HTMLButtonElement).click();
    expect(picked).toEqual([seg]);
    expect(el.querySelector('.ctx-comment-expand')).toBeNull();
  });

  it('langer Kommentar: gekürzt mit „Ganz lesen“; nur Offline-Hinweis ohne Zeilen', () => {
    const { el, c } = make({ hasLines: true, blocks: [[{ text: 'lang' }]], long: true });
    let opened = 0;
    c.dialogOpened.subscribe(() => opened++);
    expect(el.querySelector('.ctx-comment')?.classList).toContain('ctx-comment-long');
    (el.querySelector('.ctx-comment-expand') as HTMLButtonElement).click();
    expect(opened).toBe(1);
    TestBed.resetTestingModule();
    const only = make({ hasLines: false, offlineLangStale: true });
    expect(only.el.querySelector('.ctx-comment')).toBeNull();
    expect(only.el.querySelector('.ctx-offline-lang')).not.toBeNull();
  });

  it('langer Kommentar scrollt statt abzuschneiden — untere Züge bleiben anklickbar (0.881.2)', () => {
    const many = Array.from({ length: 30 }, (_, i) => [{ text: `Zeile ${i} ` }, { ...seg, move: `M${i}` } as CommentSegment]);
    const { el, c } = make({ hasLines: true, blocks: many, long: true });
    document.body.appendChild(el);
    try {
      const text = el.querySelector('.ctx-comment-text') as HTMLElement;
      expect(getComputedStyle(text).overflowY).toBe('auto');
      expect(text.scrollHeight).toBeGreaterThan(text.clientHeight);
      const last = el.querySelectorAll('.cmt-move')[29] as HTMLButtonElement;
      last.scrollIntoView();
      const r = last.getBoundingClientRect();
      const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      expect(hit).toBe(last);
      const picked: string[] = [];
      c.movePicked.subscribe(s => picked.push(s.move!));
      last.click();
      expect(picked).toEqual(['M29']);
    } finally { el.remove(); }
  });
});
