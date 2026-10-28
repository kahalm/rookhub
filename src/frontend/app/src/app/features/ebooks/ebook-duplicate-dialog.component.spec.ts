import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { EbookDuplicateDialogComponent, EbookDuplicateDialogData, EbookDuplicateDialogResult } from './ebook-duplicate-dialog.component';
import { EbookEditDialogComponent } from './ebook-edit-dialog.component';
import { Ebook, EbookDuplicate, duplicateBooksOf } from './ebook.service';

const EXISTING: EbookDuplicate = { id: 41, title: 'The Woodpecker Method', author: 'Axel Smith', year: 2018, formats: ['pdf', 'epub'], reason: 'isbn', similarity: 1 };

describe('EbookDuplicateDialogComponent', () => {
  function make(data: EbookDuplicateDialogData) {
    const closed: (EbookDuplicateDialogResult | undefined)[] = [];
    TestBed.configureTestingModule({
      imports: [EbookDuplicateDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (r: EbookDuplicateDialogResult) => closed.push(r) } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    const fixture = TestBed.createComponent(EbookDuplicateDialogComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, closed };
  }

  it('sicher: zeigt Titel, Autor, Jahr, Formate und Grund; „Zum vorhandenen Buch“ und „Trotzdem neu“', () => {
    const { el, closed } = make({ mode: 'certain', books: [EXISTING], uploadedTitle: 'Woodpecker scan' });
    expect(el.textContent).toContain('ebooks.duplicate.titleCertain');
    expect(el.textContent).toContain('The Woodpecker Method');
    expect(el.textContent).toContain('Axel Smith');
    expect(el.textContent).toContain('2018');
    expect(el.textContent).toContain('PDF · EPUB');
    expect(el.textContent).toContain('ebooks.duplicate.reasonIsbn');
    const buttons = Array.from(el.querySelectorAll('button'));
    buttons.find(b => b.textContent!.includes('ebooks.duplicate.open'))!.click();
    buttons.find(b => b.textContent!.includes('ebooks.duplicate.uploadAnyway'))!.click();
    expect(closed).toEqual([{ action: 'open', id: 41 }, { action: 'force' }]);
  });

  it('gleiche Datei: kein „Trotzdem neu“', () => {
    const { el } = make({ mode: 'certain', books: [{ ...EXISTING, reason: 'file' }], allowForce: false });
    expect(el.textContent).not.toContain('ebooks.duplicate.uploadAnyway');
    expect(el.textContent).toContain('ebooks.duplicate.reasonFile');
  });

  it('wahrscheinlich: „Ja, das vorhandene nehmen“ / „Nein, behalten“', () => {
    const { el, closed } = make({ mode: 'probable', books: [{ ...EXISTING, reason: 'text', similarity: 0.62 }] });
    expect(el.textContent).toContain('ebooks.duplicate.titleProbable');
    const buttons = Array.from(el.querySelectorAll('button'));
    buttons.find(b => b.textContent!.includes('ebooks.duplicate.takeExisting'))!.click();
    buttons.find(b => b.textContent!.includes('ebooks.duplicate.keep'))!.click();
    expect(closed).toEqual([{ action: 'open', id: 41 }, { action: 'keep' }]);
  });

  it('duplicateBooksOf liest nur 409 mit Dubletten-Code', () => {
    expect(duplicateBooksOf({ status: 409, error: { code: 'ebook_duplicate_book', details: { books: [EXISTING] } } }))
      .toEqual({ code: 'ebook_duplicate_book', books: [EXISTING] });
    expect(duplicateBooksOf({ status: 409, error: { code: 'ebook_duplicate_file' } })).toEqual({ code: 'ebook_duplicate_file', books: [] });
    expect(duplicateBooksOf({ status: 400, error: { code: 'ebook_duplicate_book' } })).toBeNull();
    expect(duplicateBooksOf({ status: 409, error: { code: 'friendship_exists' } })).toBeNull();
  });
});

describe('EbookEditDialogComponent — Dubletten beim Hochladen (0.863.0)', () => {
  let answer: EbookDuplicateDialogResult | undefined;
  let opened: EbookDuplicateDialogData[];
  let closed: (Ebook | null)[];

  function make() {
    opened = [];
    closed = [];
    TestBed.configureTestingModule({
      imports: [EbookEditDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideRouter([]),
        { provide: MatDialogRef, useValue: { close: (r: Ebook | null) => closed.push(r) } },
        { provide: MAT_DIALOG_DATA, useValue: { ebook: null, groups: [], maxFileBytes: 10_000 } },
      ],
    });
    // MatDialogModule bringt MatDialog in den Imports der Komponente mit — ein providers-Eintrag käme nicht an.
    TestBed.overrideProvider(MatDialog, {
      useValue: { open: (_: unknown, cfg: { data: EbookDuplicateDialogData }) => { opened.push(cfg.data); return { afterClosed: () => of(answer) }; } },
    });
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    const c = fixture.componentInstance;
    c.file = new File([new Uint8Array(4)], 'woodpecker.pdf');
    c.title = 'Woodpecker';
    const router = TestBed.inject(Router);
    const nav = spyOn(router, 'navigate').and.resolveTo(true);
    return { c, http: TestBed.inject(HttpTestingController), nav };
  }

  const book = (extra: Partial<Ebook> = {}) => ({ id: 99, title: 'Woodpecker', possibleDuplicates: null, ...extra }) as Ebook;

  it('409 „Buch schon da“ → Dialog; „Zum vorhandenen Buch“ navigiert dorthin', () => {
    answer = { action: 'open', id: 41 };
    const { c, http, nav } = make();
    c.save();
    http.expectOne('/api/ebooks').flush({ message: 'x', code: 'ebook_duplicate_book', details: { books: [EXISTING] } }, { status: 409, statusText: 'Conflict' });
    expect(opened[0].mode).toBe('certain');
    expect(opened[0].books[0].id).toBe(41);
    expect(nav).toHaveBeenCalledWith(['/ebooks', 41]);
    expect(closed).toEqual([null]);
  });

  it('409 → „Trotzdem neu“ schickt forceNew=true und schließt mit dem neuen Buch', () => {
    answer = { action: 'force' };
    const { c, http } = make();
    c.save();
    http.expectOne('/api/ebooks').flush({ code: 'ebook_duplicate_book', details: { books: [EXISTING] } }, { status: 409, statusText: 'Conflict' });
    const retry = http.expectOne('/api/ebooks');
    expect((retry.request.body as FormData).get('forceNew')).toBe('true');
    retry.flush(book());
    expect(closed[0]?.id).toBe(99);
  });

  it('wahrscheinliche Dublette → „Ja, das vorhandene nehmen“ löscht den Upload und öffnet das vorhandene', () => {
    answer = { action: 'open', id: 41 };
    const { c, http, nav } = make();
    c.save();
    http.expectOne('/api/ebooks').flush(book({ possibleDuplicates: [{ ...EXISTING, reason: 'title', similarity: 0 }] }));
    expect(opened[0].mode).toBe('probable');
    const del = http.expectOne('/api/ebooks/99');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    expect(nav).toHaveBeenCalledWith(['/ebooks', 41]);
    expect(closed).toEqual([null]);
  });

  it('wahrscheinliche Dublette → „Nein, behalten“ schließt mit dem neuen Buch, ohne zu löschen', () => {
    answer = { action: 'keep' };
    const { c, http } = make();
    c.save();
    http.expectOne('/api/ebooks').flush(book({ possibleDuplicates: [EXISTING] }));
    http.expectNone('/api/ebooks/99');
    expect(closed[0]?.id).toBe(99);
  });
});
