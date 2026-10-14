import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EbookListComponent } from './ebook-list.component';
import { Ebook, EbookList, EbookService } from './ebook.service';

const book = (over: Partial<Ebook> = {}): Ebook => ({
  id: 1, title: 'Buch', author: 'Autor', year: 2000, tags: [], description: null, targetMinElo: null, source: 'upload',
  pageCount: 100, hasCover: false, diagramState: 'unsupported', diagramCount: 0, ownerUserId: 1, ownerName: 'owner', isOwner: true, canManage: true, groupIds: [],
  files: [{ id: 1, format: 'pdf', fileName: 'b.pdf', sizeBytes: 1000 }], primaryFormat: 'pdf', progress: null,
  createdAt: '', updatedAt: '2026-10-13T00:00:00Z', ...over,
});

const list = (items: Ebook[], over: Partial<EbookList> = {}): EbookList => ({
  items, total: items.length, page: 1, pageSize: 24, tags: [{ tag: 'Endspiel', count: 2 }], uploadEnabled: true,
  quotaUsedBytes: 0, quotaLimitBytes: null, maxFileBytes: 1000, canManageAll: false, canImportLibrary: false, ...over,
});

describe('EbookListComponent', () => {
  let calls: unknown[];
  let service: jasmine.SpyObj<EbookService>;

  function make(items: Ebook[], over: Partial<EbookList> = {}): EbookListComponent {
    calls = [];
    service = jasmine.createSpyObj<EbookService>('EbookService', ['list', 'accessToken', 'coverUrl', 'fileUrl', 'groupOptions', 'delete', 'setGroups', 'importLibrary']);
    service.list.and.callFake(q => { calls.push(q); return of(list(items, over)); });
    service.accessToken.and.returnValue(of('tok'));
    service.coverUrl.and.callFake((id, t, v) => `/api/ebooks/${id}/cover?t=${t}&v=${v}`);
    service.fileUrl.and.callFake((id, t, o) => `/api/ebooks/${id}/file?t=${t}${o?.download ? '&download=true' : ''}`);
    service.groupOptions.and.returnValue(of([{ id: 5, name: 'Everyone', isEveryone: true }, { id: 7, name: 'Verein', isEveryone: false }]));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(null) }) } },
        { provide: SnackbarService, useValue: { warn: () => {}, success: () => {}, quick: () => {}, info: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(true) } },
        { provide: TranslateService, useValue: { instant: (k: string) => k, currentLang: () => 'en' } },
        { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
      ],
    });
    const c = TestBed.runInInjectionContext(() => new EbookListComponent());
    c.ngOnInit();
    return c;
  }

  it('lädt die erste Seite mit der Titel-Sortierung und ohne Filter', () => {
    const c = make([book()]);
    expect(c.loading).toBeFalse();
    expect(c.items.length).toBe(1);
    expect(calls[0]).toEqual(jasmine.objectContaining({ page: 1, sort: 'title', mine: false, tag: null, search: undefined }));
  });

  it('ein Schlagwort filtert, der zweite Tipp hebt es auf — und springt auf Seite 1', () => {
    const c = make([book()]);
    c.page = 3;
    c.setTag('Endspiel');
    expect(c.tag).toBe('Endspiel');
    expect(c.page).toBe(1);
    expect(calls[1]).toEqual(jasmine.objectContaining({ tag: 'Endspiel', page: 1 }));
    c.setTag('Endspiel');
    expect(c.tag).toBeNull();
    expect(c.filtered).toBeFalse();
  });

  it('Titelbild und Download-Link tragen den Ausweis; ohne Titelbild gibt es keinen Bild-Link', () => {
    const c = make([book({ hasCover: true }), book({ id: 2, hasCover: false })]);
    expect(c.coverUrl(c.items[0])).toBe('/api/ebooks/1/cover?t=tok&v=2026-10-13T00:00:00Z');
    expect(c.coverUrl(c.items[1])).toBeNull();
    expect(c.downloadUrl(c.items[0])).toBe('/api/ebooks/1/file?t=tok&download=true');
  });

  it('Lesestand in Prozent: aus Seite/Seitenzahl, nie unter 1, nie ohne Seitenzahl', () => {
    const c = make([]);
    expect(c.progressPercent(book({ progress: null }))).toBeNull();
    expect(c.progressPercent(book({ progress: { page: 1, pageCount: 1000, updatedAt: '' } }))).toBe(1);
    expect(c.progressPercent(book({ progress: { page: 50, pageCount: null, updatedAt: '' }, pageCount: 200 }))).toBe(25);
    expect(c.progressPercent(book({ progress: { page: 50, pageCount: null, updatedAt: '' }, pageCount: null }))).toBeNull();
  });

  it('nur PDF ist lesbar, alles andere wird heruntergeladen', () => {
    const c = make([]);
    expect(c.readable(book({ primaryFormat: 'pdf' }))).toBeTrue();
    expect(c.readable(book({ primaryFormat: 'epub' }))).toBeFalse();
    expect(c.readable(book({ primaryFormat: null }))).toBeFalse();
  });

  it('die Auswahl kennt nur verwaltbare Bücher und wird beim Verlassen des Modus geleert', () => {
    const c = make([book({ id: 1, canManage: true }), book({ id: 2, canManage: false }), book({ id: 3, canManage: true })]);
    c.toggleSelecting();
    c.selectAllOnPage();
    expect([...c.selected()]).toEqual([1, 3]);
    c.toggleSelected(c.items[0], false);
    expect([...c.selected()]).toEqual([3]);
    c.toggleSelecting();
    expect(c.selecting()).toBeFalse();
    expect(c.selected().size).toBe(0);
  });

  it('die Sichtbarkeits-Kurzform unterscheidet nur ich / alle / Gruppen', () => {
    const c = make([book()]);
    c.setVisibility([c.items[0]]);   // lädt die Gruppen (Everyone = 5)
    expect(c.visibilityKey(book({ groupIds: [] }))).toBe('onlyMe');
    expect(c.visibilityKey(book({ groupIds: [5, 7] }))).toBe('everyone');
    expect(c.visibilityKey(book({ groupIds: [7] }))).toBe('groups');
  });

  it('Hochladen ohne eingerichteten Speicher öffnet keinen Dialog, sondern erklärt es', () => {
    const c = make([book()], { uploadEnabled: false });
    const dialog = TestBed.inject(MatDialog) as unknown as { open: jasmine.Spy };
    spyOn(dialog, 'open').and.callThrough();
    const snackbar = TestBed.inject(SnackbarService);
    const warn = spyOn(snackbar, 'warn');
    c.openUpload();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalledWith('apiErrors.ebooks_not_configured');
  });
});
