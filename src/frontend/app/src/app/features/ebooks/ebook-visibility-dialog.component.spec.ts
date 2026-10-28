import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { EbookVisibilityDialogComponent } from './ebook-visibility-dialog.component';

describe('EbookVisibilityDialogComponent', () => {
  it('schickt die angekreuzten Gruppen für alle gewählten Bücher und schließt mit der Zahl', async () => {
    const closed: unknown[] = [];
    await TestBed.configureTestingModule({
      imports: [EbookVisibilityDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (v: unknown) => closed.push(v) } },
        { provide: MAT_DIALOG_DATA, useValue: { ebookIds: [1, 2], groups: [{ id: 5, name: 'E', isEveryone: true }], selected: [] } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookVisibilityDialogComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.toggle(5, true);
    c.save();
    const http = TestBed.inject(HttpTestingController);
    const req = http.expectOne('/api/ebooks/groups');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ ebookIds: [1, 2], groupIds: [5] });
    req.flush({ updated: 2 });
    expect(closed).toEqual([2]);
  });

  // d7-rk-gilt-fuer-ebooks-mehrzahlklammer: Schlüsselpaar statt „E-Book(s)“.
  for (const [ids, key] of [[[1], 'ebooks.bulk.forCountOne'], [[1, 2], 'ebooks.bulk.forCount']] as const) {
    it(`${ids.length} Buch/Bücher → ${key}`, async () => {
      await TestBed.configureTestingModule({
        imports: [EbookVisibilityDialogComponent],
        providers: [
          provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
          { provide: MatDialogRef, useValue: { close: () => {} } },
          { provide: MAT_DIALOG_DATA, useValue: { ebookIds: ids, groups: [], selected: [] } },
        ],
      }).compileComponents();
      const fixture = TestBed.createComponent(EbookVisibilityDialogComponent);
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.hint')!.textContent!.trim()).toBe(key);
    });
  }
});

describe('ebooks.bulk ohne Klammer-Mehrzahl (d7)', () => {
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(lang, async () => {
      const d = await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json();
      for (const k of ['forCount', 'forCountOne', 'done', 'doneOne']) {
        expect(d.ebooks.bulk[k]).withContext(k).not.toMatch(/\(s\)|\/e-/);
      }
      expect(d.ebooks.bulk.forCountOne).not.toContain('{{n}}');
    });
  }
});
