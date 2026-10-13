import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookReaderComponent } from './ebook-reader.component';
import { Ebook, EbookService } from './ebook.service';
import { PdfEngineLoader } from './pdf-engine';

const book = (over: Partial<Ebook> = {}): Ebook => ({
  id: 9, title: 'Buch', author: 'Autor', year: null, tags: [], description: null, targetMinElo: null, source: 'upload',
  pageCount: 120, hasCover: false, ownerUserId: 1, ownerName: null, isOwner: true, canManage: true, groupIds: [],
  files: [{ id: 1, format: 'pdf', fileName: 'b.pdf', sizeBytes: 1000 }], primaryFormat: 'pdf',
  progress: { page: 42, pageCount: 120, updatedAt: '' }, createdAt: '', updatedAt: '', ...over,
});

describe('EbookReaderComponent', () => {
  let service: jasmine.SpyObj<EbookService>;

  async function make(b: Ebook) {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['get', 'accessToken', 'fileUrl', 'setProgress', 'forgetToken']);
    service.get.and.returnValue(of(b));
    service.accessToken.and.returnValue(of('tok'));
    service.fileUrl.and.callFake((id, t, o) => `/api/ebooks/${id}/file?t=${t}${o?.download ? '&download=true' : ''}${o?.fileId ? '&fileId=' + o.fileId : ''}`);
    service.setProgress.and.returnValue(of({ page: 1, pageCount: null, updatedAt: '' }));
    await TestBed.configureTestingModule({
      imports: [EbookReaderComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '9']]) } } },
        { provide: SnackbarService, useValue: { warn: () => {} } },
        // pdf.js läuft im Test nicht — der Leser bekommt einen Lader, der nie fertig wird.
        { provide: PdfEngineLoader, useValue: { load: () => new Promise(() => {}), documentParams: (url: string) => ({ url }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookReaderComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('öffnet ein PDF an der gemerkten Seite und speichert den Lesestand verzögert', fakeAsync(async () => {
    const fixture = await make(book());
    const c = fixture.componentInstance;
    expect(c.readable).toBeTrue();
    expect(c.fileUrl).toBe('/api/ebooks/9/file?t=tok');
    expect(c.initialPage).toBe(42);
    expect(c.pageCount()).toBe(120);

    c.onPageChange(43);
    c.onPageChange(44);
    tick(1000);
    expect(service.setProgress).not.toHaveBeenCalled();
    tick(600);
    expect(service.setProgress).toHaveBeenCalledOnceWith(9, 44, 120);
    fixture.destroy();
  }));

  it('EPUB bekommt keine Leseansicht, sondern den Download', async () => {
    const fixture = await make(book({ primaryFormat: 'epub', files: [{ id: 2, format: 'epub', fileName: 'b.epub', sizeBytes: 5 }], progress: null }));
    const c = fixture.componentInstance;
    expect(c.readable).toBeFalse();
    expect(c.downloadUrl(2)).toBe('/api/ebooks/9/file?t=tok&download=true&fileId=2');
    expect(fixture.nativeElement.textContent).toContain('ebooks.reader.notViewable');
  });

  it('Zoom geht stufenweise vom angewandten Faktor aus; „Breite" setzt ihn zurück', async () => {
    const fixture = await make(book());
    const c = fixture.componentInstance;
    c.appliedZoom.set(1.1);
    c.zoomBy(1);
    expect(c.zoom()).toBe(1.25);
    c.fitWidth();
    expect(c.zoom()).toBeNull();
  });

  it('ein gescheiterter Leser vergisst den Ausweis, damit der nächste Versuch einen frischen holt', async () => {
    const fixture = await make(book());
    fixture.componentInstance.onViewerFailed();
    expect(service.forgetToken).toHaveBeenCalled();
    expect(fixture.componentInstance.viewerFailed()).toBeTrue();
  });

  it('zeigt den Ladefehler, wenn das Buch nicht kommt', async () => {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['get', 'accessToken']);
    service.get.and.returnValue(throwError(() => new Error('x')));
    await TestBed.configureTestingModule({
      imports: [EbookReaderComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '9']]) } } },
        { provide: SnackbarService, useValue: { warn: () => {} } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookReaderComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.error).toBeTrue();
  });
});
