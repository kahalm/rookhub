import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { AddLinesDialogComponent, AddLinesDialogData } from './add-lines-dialog.component';
import { AddCourseLinesResult, CourseService } from './course.service';

const FEN = 'r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18';

function make(data: Partial<AddLinesDialogData>, result?: AddCourseLinesResult | 'error') {
  const sent: { chapter: string | null; text: string }[] = [];
  const closed: unknown[] = [];
  const courses = {
    addLines: (_bookId: number, chapter: string | null, text: string) => {
      sent.push({ chapter, text });
      if (result === 'error') return throwError(() => ({ error: { message: 'kaputt' } }));
      return of(result ?? { added: 1, chapter, issues: [], totalLines: 1 });
    },
  };
  const component = new AddLinesDialogComponent(
    { close: (v: unknown) => closed.push(v) } as unknown as MatDialogRef<AddLinesDialogComponent, boolean>,
    courses as unknown as CourseService,
    { bookId: 58, displayName: 'TestNoel', chapterLocked: false, ...data } as AddLinesDialogData,
  );
  return { component, sent, closed };
}

describe('AddLinesDialogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [AddLinesDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: { bookId: 1, displayName: 'X', chapterLocked: false } as AddLinesDialogData,
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AddLinesDialogComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('AddLinesDialogComponent Verhalten', () => {
  it('sendet den eingegebenen Kapitelnamen mit', () => {
    const { component, sent } = make({ chapterLocked: false });
    component.chapter = ' Kalkulation 1 ';
    component.text = `1: ${FEN}`;
    component.save();
    expect(sent).toEqual([{ chapter: 'Kalkulation 1', text: `1: ${FEN}` }]);
  });

  it('leerer Kapitelname bedeutet „ohne Kapitel"', () => {
    const { component, sent } = make({ chapterLocked: false });
    component.chapter = '   ';
    component.text = FEN;
    component.save();
    expect(sent[0].chapter).toBeNull();
  });

  it('bei festem Kapitel wird dessen Name verwendet, nicht das Eingabefeld', () => {
    const { component, sent } = make({ chapterLocked: true, chapter: 'Bestehend' });
    expect(component.chapter).toBe('Bestehend');
    component.chapter = 'ignoriert';
    component.text = FEN;
    component.save();
    expect(sent[0].chapter).toBe('Bestehend');
  });

  it('schickt nichts ab, solange das Memo leer ist', () => {
    const { component, sent } = make({});
    component.text = '   ';
    component.save();
    expect(sent).toEqual([]);
  });

  it('zeigt das Ergebnis, leert das Memo und meldet die Änderung beim Schließen', () => {
    const { component, closed } = make({}, { added: 2, chapter: 'K', issues: [], totalLines: 2 });
    component.text = `${FEN}\n${FEN}`;
    component.save();

    expect(component.result?.added).toBe(2);
    expect(component.text).toBe('');            // nicht versehentlich doppelt einfügen
    expect(component.saving).toBeFalse();

    component.close();
    expect(closed).toEqual([true]);
  });

  it('behält den Text, wenn keine Zeile übernommen wurde, und meldet keine Änderung', () => {
    const { component, closed } = make({}, {
      added: 0, chapter: null, totalLines: 0,
      issues: [{ lineNumber: 1, text: 'Unsinn', reason: 'invalid_fen' }],
    });
    component.text = 'Unsinn';
    component.save();

    expect(component.result?.issues.length).toBe(1);
    expect(component.text).toBe('Unsinn');
    component.close();
    expect(closed).toEqual([false]);
  });

  it('zeigt eine Server-Fehlermeldung an', () => {
    const { component } = make({}, 'error');
    component.text = FEN;
    component.save();
    expect(component.error).toBe('kaputt');
    expect(component.saving).toBeFalse();
  });

  it('beschriftet ein fehlendes Kapitel mit einem Strich', () => {
    expect(make({ chapterLocked: true, chapter: null }).component.chapterLabel).toBe('—');
    expect(make({ chapterLocked: true, chapter: 'A' }).component.chapterLabel).toBe('A');
  });
});
