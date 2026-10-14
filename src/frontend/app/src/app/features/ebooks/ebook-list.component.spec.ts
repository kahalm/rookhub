import { ChangeDetectorRef, Type } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EBOOK_TAGS_COLLAPSED, EbookListComponent } from './ebook-list.component';
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

  // Die Listen-Ansicht liegt in der Sitzung — jeder Test beginnt ohne gemerkte Filter.
  beforeEach(() => sessionStorage.removeItem('rh.ebooks.listState'));
  afterEach(() => sessionStorage.removeItem('rh.ebooks.listState'));

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

  it('PDF und EPUB sind lesbar, alles andere wird heruntergeladen', () => {
    const c = make([]);
    expect(c.readable(book({ primaryFormat: 'pdf' }))).toBeTrue();
    expect(c.readable(book({ primaryFormat: 'epub' }))).toBeTrue();
    expect(c.readable(book({ primaryFormat: 'djvu' }))).toBeFalse();
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

  it('„Mit Diagrammen" filtert und springt auf Seite 1', () => {
    const c = make([book()]);
    c.page = 2;
    c.toggleWithDiagrams();
    expect(calls[1]).toEqual(jasmine.objectContaining({ withDiagrams: true, page: 1 }));
    expect(c.filtered).toBeTrue();
  });

  it('merkt sich Suche und Filter in der Sitzung und stellt sie beim nächsten Öffnen wieder her', () => {
    sessionStorage.removeItem('rh.ebooks.listState');
    const c = make([book()]);
    c.onSearch('woodpecker');
    c.setTag('Taktik');
    c.toggleWithDiagrams();
    const again = make([book()]);
    expect(again.tag).toBe('Taktik');
    expect(again.withDiagrams).toBeTrue();
    expect(again.search).toBe('woodpecker');
    expect(calls[0]).toEqual(jasmine.objectContaining({ tag: 'Taktik', withDiagrams: true, search: 'woodpecker' }));
    sessionStorage.removeItem('rh.ebooks.listState');
  });

  // ── UI-Sweep c1 ─────────────────────────────────────────────────────────────────────────────

  /** Echte Darstellung (Vorlage + Übersetzungs-Pipe) statt nur der Klasse. */
  function render(items: Ebook[], over: Partial<EbookList> = {}, state: Record<string, unknown> | null = null) {
    if (state) sessionStorage.setItem('rh.ebooks.listState', JSON.stringify(state));
    service = jasmine.createSpyObj<EbookService>('EbookService', ['list', 'accessToken', 'coverUrl', 'fileUrl', 'groupOptions']);
    service.list.and.returnValue(of(list(items, over)));
    service.accessToken.and.returnValue(of('tok'));
    service.coverUrl.and.returnValue('');
    service.fileUrl.and.returnValue('/f');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [EbookListComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(null) }) } },
        { provide: SnackbarService, useValue: { warn: () => {}, success: () => {}, quick: () => {}, info: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(true) } },
      ],
    });
    const fixture = TestBed.createComponent(EbookListComponent);
    fixture.detectChanges();
    return fixture;
  }

  function compactCss(cmp: Type<unknown>): string {
    return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
  }

  const manyTags = Array.from({ length: 20 }, (_, i) => ({ tag: `T${i}`, count: 40 - i }));

  it('c1-eb-tag-filter-lead: zugeklappt nur die häufigsten Schlagwörter + das aktive, aufgeklappt alle', () => {
    const c = make([book()], { tags: manyTags });
    expect(c.visibleTags.map(t => t.tag)).toEqual(manyTags.slice(0, EBOOK_TAGS_COLLAPSED).map(t => t.tag));
    c.setTag('T15');
    expect(c.visibleTags.map(t => t.tag)).toContain('T15');
    expect(c.visibleTags.length).toBe(EBOOK_TAGS_COLLAPSED + 1);
    c.tagsOpen.set(true);
    expect(c.visibleTags.length).toBe(20);
    // Wenige Schlagwörter: alle, ohne Umschalter.
    const few = make([book()], { tags: manyTags.slice(0, 5) });
    expect(few.visibleTags.length).toBe(5);
  });

  it('c1-eb-tag-filter-lead: „Alle Schlagwörter (n)“ klappt auf; am Handy „Filter ▾“ mit Zahl der aktiven Filter', () => {
    const fixture = render([book()], { tags: manyTags });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('.eb-tags .eb-chip:not(.eb-tags-more)').length).toBe(EBOOK_TAGS_COLLAPSED);
    const more = el.querySelector<HTMLButtonElement>('.eb-tags-more')!;
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.eb-tags .eb-chip:not(.eb-tags-more)').length).toBe(20);
    const toggle = el.querySelector<HTMLButtonElement>('.eb-filter-toggle')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    const c = fixture.componentInstance;
    c.toggleMine();
    c.toggleWithDiagrams();
    expect(c.activeFilterCount).toBe(2);
    fixture.detectChanges();
    // Zugeklappt (Handy): die aktiven Filter stehen als Chips über der Liste.
    expect(el.querySelectorAll('.eb-active .eb-chip').length).toBe(2);
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('.eb-active')).toBeNull();
    // Unter 600 px: Rest zugeklappt, Schalter sichtbar; darüber bleibt die Zeile wie bisher (Hülle display: contents).
    const css = compactCss(EbookListComponent);
    const base = css.slice(0, css.indexOf('@media(max-width:599.98px)'));
    expect(base).toMatch(/\.eb-filter-more(\[[^\]]*\])?\{display:contents/);
    expect(base).toMatch(/\.eb-filter-toggle(\[[^\]]*\])?\{display:none/);
    const narrow = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(narrow).toMatch(/\.eb-filter-more(\[[^\]]*\])?\{display:none/);
    expect(narrow).toMatch(/\.eb-tags(\[[^\]]*\])?\{display:none/);
    expect(narrow).toMatch(/\.eb-filter-toggle(\[[^\]]*\])?\{display:inline-flex/);
    fixture.destroy();
  });

  it('c1-eb-filter-empty-no-reset: gefiltert ohne Treffer → „Filter zurücksetzen“ leert alle Filter (Sortierung bleibt)', () => {
    const fixture = render([], {}, { search: '', tag: 'Taktik', sort: 'newest', mine: true, withDiagrams: true, page: 2 });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('ebooks.emptyFiltered');
    const reset = el.querySelector<HTMLButtonElement>('.eb-reset')!;
    expect(reset).not.toBeNull();
    reset.click();
    const c = fixture.componentInstance;
    expect([c.tag, c.mine, c.withDiagrams, c.page, c.sort]).toEqual([null, false, false, 1, 'newest']);
    expect(service.list.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ tag: null, mine: false, withDiagrams: false, page: 1 }));
    fixture.destroy();
    sessionStorage.removeItem('rh.ebooks.listState');
  });

  it('c1-eb-filter-empty-no-reset: eine Suche wird über den Such-Strom zurückgesetzt (derselbe Begriff lädt später wieder)', fakeAsync(() => {
    const c = make([]);
    c.onSearch('nimzo');
    tick(300);
    const before = calls.length;
    c.resetFilters();
    tick(300);
    expect(c.search).toBe('');
    expect(calls.length).toBe(before + 1);
    expect(calls.at(-1)).toEqual(jasmine.objectContaining({ search: undefined }));
    c.onSearch('nimzo');
    tick(300);
    expect(calls.length).toBe(before + 2);
    c.ngOnDestroy();
  }));

  it('c1-eb-empty-lead-mobile: leere Bibliothek ohne Filter zeigt keine Filterzeile, nur Leerhinweis + Hochladen', () => {
    const fixture = render([], { total: 0, tags: [] });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.eb-filters')).toBeNull();
    expect(el.querySelector('.eb-empty button')).not.toBeNull();
    expect(el.querySelector('.eb-reset')).toBeNull();
    fixture.destroy();
  });

  it('c1-eb-empty-upload-icon: das große Leerzustands-Symbol trifft nur das direkte Kind, nicht das im Knopf', () => {
    const css = compactCss(EbookListComponent);
    expect(css).toMatch(/\.eb-empty(\[[^\]]*\])?>mat-icon(\[[^\]]*\])?\{font-size:40px/);
    expect(css).not.toMatch(/\.eb-empty(\[[^\]]*\])?mat-icon/);
  });

  it('c1-eb-card-action-button: Hauptknopf füllt die Zeile, einzeilig; „Weiter“ kurz, die Seite im Namen', () => {
    const fixture = render([book({ progress: { page: 7, pageCount: 100, updatedAt: '' } }),
      book({ id: 2, primaryFormat: 'djvu', files: [{ id: 3, format: 'djvu', fileName: 'x.djvu', sizeBytes: 1 }] })]);
    const el: HTMLElement = fixture.nativeElement;
    const [read, dl] = Array.from(el.querySelectorAll<HTMLElement>('.eb-act'));
    expect(read.querySelector('.eb-act-label')!.textContent).toBe('ebooks.continueShort');
    expect(read.getAttribute('aria-label')).toBe('ebooks.continue');
    expect(dl.classList).toContain('eb-act--download');
    expect(dl.getAttribute('aria-label')).toBe('ebooks.download');
    const css = compactCss(EbookListComponent);
    expect(css).toMatch(/\.eb-act(\[[^\]]*\])?\{flex:11auto;min-width:0;white-space:nowrap/);
    expect(css).toMatch(/\.eb-act(\[[^\]]*\])?\.mdc-button__label\{min-width:0;overflow:hidden;text-overflow:ellipsis/);
    fixture.destroy();
  });

  it('c1-eb-ultrawide-not-wide: Regal ab 2400 px in --page-max-width-wide, Leerzustand bleibt schmal', () => {
    const css = compactCss(EbookListComponent);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    expect(css.slice(at)).toMatch(/\.eb-empty(\[[^\]]*\])?\{max-width:var\(--page-max-width,1240px\)/);
    expect(css.slice(0, at)).not.toContain('--page-max-width-wide');
  });
});
