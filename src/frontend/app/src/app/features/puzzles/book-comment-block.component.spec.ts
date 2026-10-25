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
});
