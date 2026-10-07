import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { CommentDialogComponent, commentLength, LONG_COMMENT_CHARS } from './comment-dialog.component';
import { CommentSegment } from './comment-variation.util';

/** Langer Kurs-Kommentar im Fenster (Wunsch 2026-10-07). */
describe('CommentDialogComponent', () => {
  // Der Beispieltext des Wunsches, bis „thus" — ab dieser Länge gilt ein Kommentar als lang.
  const FOREWORD = 'Dear Reader, When my friend and former pupil Martin asked me to write a foreword to his book, '
    + 'I did not expect that I would learn so much from reading this work, but that is what happened. The first '
    + 'reason is that, in addition to some classic examples, he has found tactical examples that I did not know, thus';

  it('zählt Text und Zug-Chips aller Absätze; der Beispieltext des Wunsches ist lang', () => {
    expect(commentLength([[{ text: 'Nach ' }, { move: '1.e4' }], [{ text: 'ab' }]])).toBe(11);
    expect(commentLength([[{ text: FOREWORD }]])).toBeGreaterThanOrEqual(LONG_COMMENT_CHARS);
    expect(commentLength([[{ text: 'Kurz und gut.' }]])).toBeLessThan(LONG_COMMENT_CHARS);
  });

  it('ein Zug im Text schließt das Fenster und gibt den Zug zurück', () => {
    const move: CommentSegment = { move: '1.e4', fen: 'x', from: 'e2', to: 'e4' };
    const close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [CommentDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { title: 'Foreword', subtitle: 'Intro', blocks: [[{ text: 'Nach ' }, move]] } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(CommentDialogComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Foreword');
    expect(fixture.componentInstance.hasMoves).toBeTrue();
    (el.querySelector('.cmt-move') as HTMLButtonElement).click();
    expect(close).toHaveBeenCalledWith(move);
  });
});
