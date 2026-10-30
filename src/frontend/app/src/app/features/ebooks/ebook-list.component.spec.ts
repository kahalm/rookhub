import { ebookAuthorNames } from './ebook.service';
import { ChangeDetectorRef, Signal, Type, signal } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { firstValueFrom, of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { EbookListComponent, ebookFormats, eloParam } from './ebook-list.component';
import { Ebook, EbookList, EbookService, EbookTranslationItem } from './ebook.service';

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

  function make(items: Ebook[], over: Partial<EbookList> = {}, loggedIn = true): EbookListComponent {
    calls = [];
    service = jasmine.createSpyObj<EbookService>('EbookService', ['list', 'accessToken', 'coverUrl', 'fileUrl', 'groupOptions', 'delete', 'setGroups', 'importLibrary', 'enqueueTranslation']);
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
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: TranslateService, useValue: { instant: (k: string) => k, currentLang: () => 'en' } },
        { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
      ],
    });
    const c = TestBed.runInInjectionContext(() => new EbookListComponent());
    c.ngOnInit();
    return c;
  }

  it('Sammlung (0.787.0): Ordner-Karte öffnet die Sammlung (Seite 1), Zurück lädt wieder die Hauptliste; 60 je Seite', () => {
    const c = make([book({ id: 3, collectionStub: true, collection: { id: 9, name: 'British Chess Magazine', bookCount: 6 } })]);
    expect(calls[0]).toEqual(jasmine.objectContaining({ pageSize: 60, collection: null }));
    c.page = 3;
    c.openCollection(9);
    expect(calls.at(-1)).toEqual(jasmine.objectContaining({ collection: 9, page: 1 }));
    c.closeCollection();
    expect(calls.at(-1)).toEqual(jasmine.objectContaining({ collection: null, page: 1 }));
  });

  it('Übersetzung (0.876.0): Buch vorziehen reiht es fürs Englische ein und meldet es', () => {
    const c = make([book({ id: 4, language: 'de' })], { canManageAll: true });
    service.enqueueTranslation.and.returnValue(of({} as EbookTranslationItem));
    const quick = spyOn(TestBed.inject(SnackbarService), 'quick');
    c.translateBook(c.items[0]);
    expect(service.enqueueTranslation).toHaveBeenCalledWith(4, 'en');
    expect(quick).toHaveBeenCalledWith('ebooks.translation.enqueued');
  });

  it('lädt die erste Seite mit der Titel-Sortierung und ohne Filter', () => {
    const c = make([book()]);
    expect(c.loading).toBeFalse();
    expect(c.items.length).toBe(1);
    expect(calls[0]).toEqual(jasmine.objectContaining({ page: 1, sort: 'newest', mine: false, tag: null, search: undefined }));
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
    expect(c.visibilityKey(book({ groupIds: [7] }))).toBe('groupsOne');   // d9-rk-ebooks-gruppen-mehrzahl
    expect(c.visibilityKey(book({ groupIds: [7, 8] }))).toBe('groups');
  });

  it('ohne Anmeldung: keine Freigabe-Gruppen vom Server, Hochladen-Dialog mit Hinweis statt Sichtbarkeit', () => {
    const c = make([book()], {}, false);
    expect(c.anonymous).toBeTrue();
    const dialog = TestBed.inject(MatDialog) as unknown as { open: jasmine.Spy };
    spyOn(dialog, 'open').and.callThrough();
    c.openUpload();
    expect(service.groupOptions).not.toHaveBeenCalled();
    expect(dialog.open.calls.mostRecent().args[1].data).toEqual(jasmine.objectContaining({ anonymous: true, groups: [] }));
    expect(c.loginQuery).toEqual({ returnUrl: '/ebooks' });
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

  it('Elo von–bis filtert nach kurzer Pause und springt auf Seite 1; Unsinn = keine Grenze', fakeAsync(() => {
    const c = make([book()]);
    c.page = 2;
    c.setElo('min', '1400');
    c.setElo('max', 1800);
    expect(calls.length).toBe(1);   // erst nach der Pause, nicht je Ziffer
    tick(400);
    expect(calls[1]).toEqual(jasmine.objectContaining({ minElo: 1400, maxElo: 1800, page: 1 }));
    expect(c.filtered).toBeTrue();
    expect(c.activeFilterCount).toBe(1);
    c.setElo('max', 'abc');
    tick(400);
    expect(c.maxElo).toBeNull();
    c.clearElo();
    expect([c.minElo, c.maxElo]).toEqual([null, null]);
    expect(calls[calls.length - 1]).toEqual(jasmine.objectContaining({ minElo: null, maxElo: null }));
  }));

  it('eloParam: ganze Zahl 0–3500, sonst null', () => {
    expect([eloParam('1600'), eloParam(' '), eloParam(null), eloParam('-5'), eloParam('4000'), eloParam('0')]).toEqual([1600, null, null, null, null, 0]);
  });

  it('merkt sich Suche und Filter in der Sitzung und stellt sie beim nächsten Öffnen wieder her', () => {
    sessionStorage.removeItem('rh.ebooks.listState');
    const c = make([book()]);
    c.onSearch('woodpecker');
    c.setTag('Taktik');
    c.minElo = 1500;
    c.setLanguage('unknown');
    c.setTranslation('en');
    c.load();   // speichert den Stand mit Elo
    const again = make([book()]);
    expect(again.tag).toBe('Taktik');
    expect([again.language, again.translation]).toEqual(['unknown', 'en']);
    expect(again.minElo).toBe(1500);
    expect(again.search).toBe('woodpecker');
    expect(calls[0]).toEqual(jasmine.objectContaining({ tag: 'Taktik', minElo: 1500, search: 'woodpecker', language: 'unknown', translation: 'en' }));
    // Derselbe Wert noch einmal hebt den Filter auf; „Filter zurücksetzen“ leert beide.
    again.setLanguage('unknown');
    expect(again.language).toBeNull();
    again.resetFilters();
    expect(again.translation).toBeNull();
    sessionStorage.removeItem('rh.ebooks.listState');
  });

  // d6-rk-ebook-format-chip-doppelt
  it('zeigt je Format EIN Abzeichen (2× PDF + DJVU → „PDF“, „DJVU“), mehrere Dateien im Tooltip', () => {
    expect(ebookFormats([{ format: 'pdf' }, { format: 'PDF' }, { format: 'djvu' }]))
      .toEqual([{ label: 'PDF', n: 2 }, { label: 'DJVU', n: 1 }]);
    const fixture = render([book({ files: [
      { id: 1, format: 'pdf', fileName: 'scan.pdf', sizeBytes: 1 },
      { id: 2, format: 'pdf', fileName: 'text.pdf', sizeBytes: 1 },
      { id: 3, format: 'djvu', fileName: 'x.djvu', sizeBytes: 1 },
    ] })]);
    const chips = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.eb-badges .eb-format'));
    expect(chips.map(c => c.textContent?.trim())).toEqual(['PDF', 'DJVU']);
  });

  // ── UI-Sweep c1 ─────────────────────────────────────────────────────────────────────────────

  /** Echte Darstellung (Vorlage + Übersetzungs-Pipe) statt nur der Klasse. */
  function render(items: Ebook[], over: Partial<EbookList> = {}, state: Record<string, unknown> | null = null, loggedIn?: boolean) {
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
        ...(loggedIn === undefined ? [] : [{ provide: AuthService, useValue: { isLoggedIn: loggedIn } }]),
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

  it('0.877.0: Schlagwörter, Sprache und Elo nur hinter „Filter ▾“ (jede Breite), Zahl der aktiven Filter, Chips zugeklappt', () => {
    const fixture = render([book()], { tags: manyTags, languages: [{ language: 'de', count: 1 }] });
    const el: HTMLElement = fixture.nativeElement;
    // Zugeklappt: keine Schlagwort-Leiste mehr offen.
    expect(el.querySelector('app-ebook-filter-panel')).toBeNull();
    expect(el.querySelector('.eb-tags')).toBeNull();
    const toggle = el.querySelector<HTMLButtonElement>('.eb-filter-toggle')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    const c = fixture.componentInstance;
    c.toggleMine();
    c.minElo = 1400;
    c.setLanguage('de');
    expect(c.activeFilterCount).toBe(3);
    fixture.detectChanges();
    expect(el.querySelectorAll('.eb-active .eb-chip').length).toBe(3);
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('.eb-active')).toBeNull();
    const panel = el.querySelector('app-ebook-filter-panel')!;
    expect(panel.querySelectorAll('.eb-tags .eb-chip:not(.eb-tags-more)').length).toBe(8);
    expect(panel.querySelector('.eb-elo')).not.toBeNull();
    expect(panel.querySelector('[data-lang="de"]')!.getAttribute('aria-pressed')).toBe('true');
    // 0.877.2: „Filter ▾“ auf jeder Breite, Sortierung und „Meine Uploads“ auf jeder Breite dahinter.
    const css = compactCss(EbookListComponent);
    expect(css).toMatch(/\.eb-filter-more(\[[^\]]*\])?\{display:none/);
    expect(css).toMatch(/\.eb-filter-more--open(\[[^\]]*\])?\{display:flex/);
    expect(css).not.toMatch(/\.eb-filter-toggle(\[[^\]]*\])?\{display:none/);
    expect(css).not.toMatch(/\.eb-active-mine(\[[^\]]*\])?\{display:none/);
    fixture.destroy();
  });

  it('0.877.0: Chip zeigt das übersetzte Schlagwort, der Filter nimmt das gespeicherte', async () => {
    const fixture = render([book()], { tags: [{ tag: 'Eröffnung', count: 3 }, { tag: 'Top 100', count: 1 }] });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ebooks: { tags: { opening: 'Opening' } } });
    await firstValueFrom(translate.use('en'));
    const el: HTMLElement = fixture.nativeElement;
    el.querySelector<HTMLButtonElement>('.eb-filter-toggle')!.click();
    fixture.detectChanges();
    const chip = el.querySelector<HTMLButtonElement>('app-ebook-filter-panel [data-tag="Eröffnung"]')!;
    expect(chip.textContent).toContain('Opening');
    expect(chip.textContent).not.toContain('Eröffnung');
    expect(el.querySelector('app-ebook-filter-panel [data-tag="Top 100"]')!.textContent).toContain('Top 100');
    chip.click();
    expect(service.list.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ tag: 'Eröffnung', page: 1 }));
    expect(fixture.componentInstance.tag).toBe('Eröffnung');
    // Zugeklappt steht der aktive Filter übersetzt als Chip da.
    el.querySelector<HTMLButtonElement>('.eb-filter-toggle')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.eb-active')!.textContent).toContain('Opening');
    fixture.destroy();
  });

  it('0.877.0: Gesamtzeile zählt Bücher (Sammlungen einzeln) und Dateien je Format', () => {
    const fixture = render([book()], { total: 2, bookCount: 1234, fileCounts: [{ format: 'pdf', count: 2000 }, { format: 'epub', count: 34 }] });
    const text = (fixture.nativeElement as HTMLElement).querySelector('.eb-totals')!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('ebooks.totals.books');
    expect(text).toContain('ebooks.totals.files');
    expect(text).toContain('PDF 2,000 · EPUB 34');
    expect(fixture.componentInstance.fileTotal).toBe(2034);
    fixture.destroy();
  });

  // Falz-Runde 16.10.: Regal am Handy 16–17 Bildschirme → kompakte Zeilen, Einleitung im ⓘ am Titel
  it('am Handy: Buch als kompakte Zeile (Titelbild 64 px | Angaben | Symbolknöpfe), Einleitung nur im ⓘ am Titel', () => {
    const fixture = render([book()], {});
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('h1 app-help-hint.eb-title-hint')).not.toBeNull();
    // Der Lesen-Knopf trägt seinen Namen immer im aria-label (am Handy steht nur das Symbol).
    expect(el.querySelector('.eb-act')!.getAttribute('aria-label')).toBe('ebooks.read');
    const css = compactCss(EbookListComponent);
    const base = css.slice(0, css.indexOf('@media(max-width:599.98px)'));
    expect(base).toMatch(/\.eb-title-hint(\[[^\]]*\])?\{display:none/);
    const narrow = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(narrow).toMatch(/\.intro(\[[^\]]*\])?\{display:none/);
    expect(narrow).toMatch(/\.eb-grid(\[[^\]]*\])?\{grid-template-columns:minmax\(0,1fr\)/);
    expect(narrow).toMatch(/\.eb-card(\[[^\]]*\])?\{display:grid;grid-template-columns:64pxminmax\(0,1fr\)auto;grid-template-areas:"coverbodyactions"/);
    expect(narrow).toMatch(/\.eb-act(\[[^\]]*\])?\.eb-act-label(\[[^\]]*\])?\{display:none/);
    expect(narrow).toMatch(/\.eb-act(\[[^\]]*\])?\{[^}]*min-width:44px;width:44px;height:44px/);
    fixture.destroy();
  });

  it('c1-eb-filter-empty-no-reset: gefiltert ohne Treffer → „Filter zurücksetzen“ leert alle Filter (Sortierung bleibt)', () => {
    const fixture = render([], {}, { search: '', tag: 'Taktik', sort: 'newest', mine: true, minElo: 1200, maxElo: 1600, page: 2 });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('ebooks.emptyFiltered');
    const reset = el.querySelector<HTMLButtonElement>('.eb-reset')!;
    expect(reset).not.toBeNull();
    reset.click();
    const c = fixture.componentInstance;
    expect([c.tag, c.mine, c.minElo, c.maxElo, c.page, c.sort]).toEqual([null, false, null, null, 1, 'newest']);
    expect(service.list.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ tag: null, mine: false, minElo: null, maxElo: null, page: 1 }));
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

  // d4-rk-ebooks-upload-knopf-aussermittig: unter 600 px war der Knopf eine Pille mit Symbol links, ohne zugänglichen Namen.
  it('d4: Hochladen trägt seinen Namen in aria-label + title; unter 600 px runder 44-px-Symbolknopf mit mittigem Symbol', () => {
    const fixture = render([book()]);
    const btn = (fixture.nativeElement as HTMLElement).querySelector('.eb-upload') as HTMLElement;
    expect(btn.getAttribute('aria-label')).toBe('ebooks.upload');
    expect(btn.getAttribute('title')).toBe('ebooks.upload');
    const css = compactCss(EbookListComponent);
    const mobile = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(mobile).toMatch(/\.eb-upload(\[[^\]]*\])?\{min-width:44px;width:44px;height:44px;padding:0;border-radius:50%/);
    expect(mobile).toMatch(/\.eb-upload(\[[^\]]*\])?mat-icon(\[[^\]]*\])?\{margin:0/);
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
  // ── UI-Sweep c2 ─────────────────────────────────────────────────────────────────────────────

  it('c2-eb-plural-1-seiten: „1 Seite“/„1 Diagramm“ über *One-Schlüssel, sonst Mehrzahl', () => {
    const fixture = render([book({ pageCount: 1, diagramCount: 1 }), book({ id: 2, pageCount: 3, diagramCount: 5 })]);
    const badges = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.eb-badges')).map(b => b.textContent ?? '');
    expect(badges[0]).toContain('ebooks.pagesOne');
    expect(badges[0]).toContain('ebooks.diagramsOne');
    expect(badges[1]).toContain('ebooks.pages');
    expect(badges[1]).not.toContain('ebooks.pagesOne');
    expect(badges[1]).not.toContain('ebooks.diagramsOne');
    fixture.destroy();
  });

  it('c2-eb-search-placeholder-truncated: am Handy kurzer Platzhalter, aria-label behält den langen Text', () => {
    const fixture = render([book()]);
    const input = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.eb-search input')!;
    expect(input().placeholder).toBe('ebooks.search');
    (fixture.componentInstance as unknown as { narrow: Signal<boolean> }).narrow = signal(true);
    fixture.detectChanges();
    expect(input().placeholder).toBe('ebooks.searchShort');
    expect(input().getAttribute('aria-label')).toBe('ebooks.search');
    fixture.destroy();
  });

  it('c2-eb-sort-select-truncated: Sortierung so breit wie ihre Voreinstellung, Suchfeld höchstens 640 px (am Handy frei)', () => {
    const css = compactCss(EbookListComponent);
    expect(css).toMatch(/\.eb-sort(\[[^\]]*\])?\{flex:00320px;max-width:100%/);
    expect(css).toMatch(/\.eb-search(\[[^\]]*\])?\{flex:11360px;max-width:640px/);   // Basis 360 px seit d3 (Tablet)
    const mobile = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(mobile).toMatch(/\.eb-search(\[[^\]]*\])?\{flex:110;min-width:0;max-width:none/);
  });
  it('c2-eb-sort-select-truncated: „Neueste zuerst (Erscheinungsjahr)“ passt ungekürzt in die Auswahl (de + en)', async () => {
    for (const [lang, text] of [['de', 'Neueste zuerst (Erscheinungsjahr)'], ['en', 'Newest first (publication year)']]) {
      const fixture = render([book()]);
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation(lang, { ebooks: { sort: { label: 'Sortierung', newest: text } } });
      translate.use(lang);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      host.style.display = 'block';
      host.style.width = '1300px';
      const value = host.querySelector<HTMLElement>('.eb-sort .mat-mdc-select-value-text')!;
      expect(value.textContent?.trim()).withContext(lang).toBe(text);
      const inner = value.querySelector<HTMLElement>('.mat-mdc-select-min-line') ?? value;
      expect(inner.scrollWidth).withContext(lang).toBeLessThanOrEqual(inner.clientWidth);
      fixture.destroy();
    }
  });

  it('filters by an author clicked below a book (0.796.0)', () => {
    expect(ebookAuthorNames('Lev Alburt, Sam Palatnik')).toEqual(['Lev Alburt', 'Sam Palatnik']);
    expect(ebookAuthorNames('Reinfeld & Chernev')).toEqual(['Reinfeld', 'Chernev']);
    expect(ebookAuthorNames('')).toEqual([]);
  });

  // ── Design-Runde d3 ─────────────────────────────────────────────────────────────────────────
  it('d3-rk-ebooks-anon-vorlauf-zu-lang: ohne Anmeldung kein Einleitungssatz über dem Regal (Banner + ⓘ am Titel), angemeldet unverändert', () => {
    const anon = render([book()], {}, null, false);
    let el: HTMLElement = anon.nativeElement;
    expect(el.querySelector('.intro')).toBeNull();
    expect(el.querySelector('.eb-anon')).not.toBeNull();
    const hint = el.querySelector<HTMLElement>('h1 app-help-hint.eb-title-hint')!;
    expect(el.querySelector('.eb-page')!.classList).toContain('eb-page--anon');
    expect(getComputedStyle(hint).display).not.toBe('none');   // im Flex-Titel wird inline-flex zu flex
    anon.destroy();

    const user = render([book()], {}, null, true);
    el = user.nativeElement;
    expect(el.querySelector('.intro')).not.toBeNull();
    expect(el.querySelector('.eb-anon')).toBeNull();
    expect(el.querySelector('.eb-page')!.classList).not.toContain('eb-page--anon');
    if (window.innerWidth >= 600) expect(getComputedStyle(el.querySelector('h1 .eb-title-hint')!).display).toBe('none');
    user.destroy();
  });

  it('d3-rk-ebooks-suchfeld-platzhalter-abgeschnitten: Suchfeld mit Basis 360 px — am Tablet bricht der Rest um, statt die Suche zu drücken', () => {
    const css = compactCss(EbookListComponent);
    const base = css.slice(0, css.indexOf('@media(max-width:599.98px)'));
    expect(base).toMatch(/\.eb-search(\[[^\]]*\])?\{flex:11360px;max-width:640px/);
    expect(base).toMatch(/\.eb-sort(\[[^\]]*\])?\{flex:00320px/);
    // Nachgestellt bei 820 px (788 px Inhalt): Suche + Sortierung in Zeile 1, Suche ≥ 440 px breit.
    const row = document.createElement('div');
    row.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px;width:788px';
    row.innerHTML = '<div style="flex:1 1 360px;max-width:640px"></div><div style="flex:0 0 320px"></div><div style="flex:none;width:150px"></div>';
    document.body.appendChild(row);
    const [search, sort, mine] = Array.from(row.children) as HTMLElement[];
    expect(search.getBoundingClientRect().width).toBeGreaterThanOrEqual(440);
    expect(sort.getBoundingClientRect().top).toBe(search.getBoundingClientRect().top);
    expect(mine.getBoundingClientRect().top).toBeGreaterThan(search.getBoundingClientRect().top);
    row.remove();
  });

  // ── Design-Runde d9 ─────────────────────────────────────────────────────────────────────────
  it('d9-rk-ebooks-gruppen-mehrzahl: eine Gruppe → „1 Gruppe“ (groupsOne), mehrere → groups', async () => {
    for (const [lang, one, many] of [['de', '1 Gruppe', '2 Gruppen'], ['en', '1 group', '2 groups']]) {
      const res = await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`));
      const vis = (await res.json()).ebooks.visibility;
      expect(vis.groupsOne.replace('{{n}}', '1')).withContext(lang).toBe(one);
      expect(vis.groups.replace('{{n}}', '2')).withContext(lang).toBe(many);
    }
    const fixture = render([book({ groupIds: [7] })]);
    expect((fixture.nativeElement as HTMLElement).querySelector('.eb-vis')!.textContent).toContain('ebooks.visibility.groupsOne');
    fixture.destroy();
  });

  it('d9-rk-ebook-chip-unbekannt-ohne-kontext: aktiver Sprachfilter-Chip mit Präfix „Sprache:“', async () => {
    const fixture = render([book()], { languages: [{ language: 'unknown', count: 1 }] }, { language: 'unknown' });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { ebooks: { filter: { languageChip: 'Sprache: {{lang}}', languageUnknown: 'Unbekannt' } } });
    translate.use('de');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const chips = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.eb-active .eb-chip')).map(c => c.textContent!.replace(/\s+/g, ' '));
    expect(chips.some(t => t.includes('Sprache: Unbekannt'))).withContext(chips.join(' | ')).toBeTrue();
    sessionStorage.removeItem('rh.ebooks.listState');
    fixture.destroy();
  });

  it('d9-rk-ebook-anon-handy-banner-vorlauf: Gast-Banner ein Satz, am Handy einzeilig gebaut (Knopf rechts in derselben Zeile)', async () => {
    for (const lang of ['de', 'en', 'hr']) {
      const res = await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`));
      const banner: string = (await res.json()).ebooks.anonymous.banner;
      expect(banner.split(/[.!?](\s|$)/).filter(x => x && x.trim()).length).withContext(lang).toBe(1);
    }
    const css = compactCss(EbookListComponent);
    const mobile = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(mobile).toMatch(/\.eb-anon(\[[^\]]*\])?\{flex-wrap:nowrap;padding-block:4px;font-size:0\.875rem/);
    expect(mobile).toMatch(/\.eb-anon(\[[^\]]*\])?>span(\[[^\]]*\])?\{flex:110;min-width:0/);
    expect(mobile).toMatch(/\.eb-anon(\[[^\]]*\])?>a(\[[^\]]*\])?\{flex:none/);
  });

  it('d9-rk-ebook-cover-beschnitten: Titelbild nie beschnitten (contain auf getönter Fläche)', () => {
    const css = compactCss(EbookListComponent);
    expect(css).toMatch(/\.eb-cover(\[[^\]]*\])?img(\[[^\]]*\])?\{width:100%;height:100%;object-fit:contain/);
    expect(css).not.toContain('object-fit:cover');
  });
});
