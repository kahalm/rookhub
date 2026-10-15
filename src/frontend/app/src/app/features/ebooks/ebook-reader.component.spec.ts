import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { Type } from '@angular/core';
import { PdfViewerComponent } from './pdf-viewer.component';
import { SnackbarService } from '../../core/snackbar.service';
import { By } from '@angular/platform-browser';
import { EbookReaderComponent } from './ebook-reader.component';
import { EbookBoardComponent } from './ebook-board.component';
import { Ebook, EbookService } from './ebook.service';
import { PdfEngineLoader } from './pdf-engine';
import { EpubViewerComponent } from './epub-viewer.component';

const book = (over: Partial<Ebook> = {}): Ebook => ({
  id: 9, title: 'Buch', author: 'Autor', year: null, tags: [], description: null, targetMinElo: null, source: 'upload',
  pageCount: 120, hasCover: false, diagramState: 'unsupported', diagramCount: 0, ownerUserId: 1, ownerName: null, isOwner: true, canManage: true, groupIds: [],
  files: [{ id: 1, format: 'pdf', fileName: 'b.pdf', sizeBytes: 1000 }], primaryFormat: 'pdf',
  progress: { page: 42, pageCount: 120, updatedAt: '' }, createdAt: '', updatedAt: '', ...over,
});

const DIAGRAMS = [
  { id: 11, page: 33, x: 0.1, y: 0.1, w: 0.3, h: 0.2, fen: 'r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b - - 0 17', label: '1', caption: 'Hamppe – Steinitz', sideKnown: true, reportedByMe: false, reportCount: 0, solutionUci: 'h8h2 h1h2 a8h8', solutionSan: 'Rxh2+ Kxh2 Rh8+', solutionText: '17...Rxh2+! 18.Kxh2 Rh8+', solutionPage: 230 },
  { id: 12, page: 33, x: 0.1, y: 0.4, w: 0.3, h: 0.2, fen: 'rnb3kr/ppp4p/3b3B/3Pp2n/2BP4/3K1Rp1/PPP3q1/RN1Q4 w - - 0 1', label: '2', caption: null, sideKnown: true, reportedByMe: false, reportCount: 0 },
  { id: 13, page: 40, x: 0.5, y: 0.1, w: 0.3, h: 0.2, fen: '8/8/8/8/8/8/8/K6k w - - 0 1', label: null, caption: null, sideKnown: false, reportedByMe: false, reportCount: 0 },
];

describe('EbookReaderComponent', () => {
  let service: jasmine.SpyObj<EbookService>;

  async function make(b: Ebook) {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['get', 'accessToken', 'fileUrl', 'epubBaseUrl', 'setProgress', 'forgetToken', 'diagrams', 'reportDiagram']);
    service.diagrams.and.returnValue(of({ state: 'done', items: DIAGRAMS }));
    service.get.and.returnValue(of(b));
    service.accessToken.and.returnValue(of('tok'));
    service.fileUrl.and.callFake((id, t, o) => `/api/ebooks/${id}/file?t=${t}${o?.download ? '&download=true' : ''}${o?.fileId ? '&fileId=' + o.fileId : ''}`);
    service.epubBaseUrl.and.callFake((id, t) => `/api/ebooks/${id}/epub/${t}/`);
    service.setProgress.and.returnValue(of({ page: 1, pageCount: null, updatedAt: '' }));
    await TestBed.configureTestingModule({
      imports: [EbookReaderComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '9']]) } } },
        { provide: SnackbarService, useValue: { warn: () => {}, quick: () => {} } },
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
    expect(c.epubDirUrl).toBeNull();
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

  it('DJVU (noch nicht umgewandelt) bekommt keine Leseansicht, sondern den Download', async () => {
    const fixture = await make(book({ primaryFormat: 'djvu', files: [{ id: 2, format: 'djvu', fileName: 'b.djvu', sizeBytes: 5 }], progress: null }));
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
        { provide: SnackbarService, useValue: { warn: () => {}, quick: () => {} } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookReaderComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.error).toBeTrue();
  });

  it('ohne erkannte Diagramme wird keine Liste geholt und der Leser bleibt einspaltig', async () => {
    const fixture = await make(book());
    expect(service.diagrams).not.toHaveBeenCalled();
    expect(fixture.componentInstance.hasDiagrams).toBeFalse();
    expect(fixture.nativeElement.querySelector('.rd-panel')).toBeNull();
  });

  it('mit Diagrammen: Zonen je Diagramm, Doppelklick stellt auf, Schwarz am Zug dreht das Brett', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    expect(c.zones().map(z => z.id)).toEqual([11, 12, 13]);
    c.pick(11);
    expect(c.picked()!.id).toBe(11);
    expect(c.panelOpen()).toBeTrue();
    c.step(1);
    expect(c.picked()!.id).toBe(12);
    expect(c.pickedIndex).toBe(2);
    c.step(1);
    expect(c.picked()!.id).toBe(13);
    expect(c.sideKey(c.picked()!)).toBe('ebooks.reader.sideUnknown');
    c.step(-1);
    expect(c.picked()!.id).toBe(12);
  });

  it('› nach dem letzten Diagramm springt zum ersten, ‹ vor dem ersten zum letzten (im Kreis)', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.pick(13);
    c.step(1);
    expect(c.picked()!.id).toBe(11);
    c.step(-1);
    expect(c.picked()!.id).toBe(13);
  });

  it('⏮ / ⏭ springen zum ersten bzw. letzten Diagramm — Knöpfe in der Brett-Leiste', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.pick(12);
    c.jumpDiagram('last');
    expect(c.picked()!.id).toBe(13);
    c.jumpDiagram('first');
    expect(c.picked()!.id).toBe(11);
    fixture.detectChanges();
    const head = fixture.nativeElement.querySelector('.rd-panel-head') as HTMLElement;
    const icons = Array.from(head.querySelectorAll('button mat-icon')).map(i => i.textContent!.trim());
    expect(icons.slice(0, 4)).toEqual(['first_page', 'chevron_left', 'chevron_right', 'last_page']);
  });

  it('der erste Schritt ohne Auswahl beginnt beim ersten Diagramm ab der aktuellen Seite', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3, progress: { page: 35, pageCount: 120, updatedAt: '' } }));
    const c = fixture.componentInstance;
    c.step(1);
    expect(c.picked()!.id).toBe(13);
  });

  it('„Diagramm falsch" meldet, ein zweiter Klick nimmt zurück', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    service.reportDiagram.and.callFake((_e: number, _d: number, r: boolean) => of({ reported: r }));
    c.pick(12);
    c.toggleReport(c.picked()!);
    expect(service.reportDiagram).toHaveBeenCalledWith(9, 12, true);
    expect(c.picked()!.reportedByMe).toBeTrue();
    expect(c.diagrams().find(d => d.id === 12)!.reportedByMe).toBeTrue();
    c.toggleReport(c.picked()!);
    expect(service.reportDiagram).toHaveBeenCalledWith(9, 12, false);
    expect(c.picked()!.reportedByMe).toBeFalse();
  });

  it('Trenner: Breite bleibt zwischen 260 px und 70 % des Fensters und wird gemerkt', async () => {
    localStorage.removeItem('rh.ebooks.panelWidth');
    expect(EbookReaderComponent.clampPanel(100, 1600)).toBe(260);
    expect(EbookReaderComponent.clampPanel(5000, 1600)).toBe(1120);
    expect(EbookReaderComponent.clampPanel(500, 1600)).toBe(500);
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.setPanelWidth(420);
    expect(c.panelWidth()).toBe(420);
    expect(localStorage.getItem('rh.ebooks.panelWidth')).toBe('420');
    c.resetPanelWidth();
    expect(c.panelWidth()).toBeNull();
    localStorage.removeItem('rh.ebooks.panelWidth');
  });

  it('EPUB ohne PDF öffnet den EPUB-Leser und speichert Prozent + CFI', fakeAsync(async () => {
    // epub.js lädt im Ordner-Modus per XHR (0.780.0) — in fakeAsync verboten; das Öffnen selbst prüft epub-viewer.spec.
    spyOn(EpubViewerComponent.prototype, 'ngAfterViewInit').and.resolveTo();
    const fixture = await make(book({ primaryFormat: 'epub', files: [{ id: 2, format: 'epub', fileName: 'b.epub', sizeBytes: 5 }],
      progress: { page: 40, pageCount: 100, locator: 'epubcfi(/6/4!/4/2)', updatedAt: '' } }));
    const c = fixture.componentInstance;
    expect(c.readable).toBeTrue();
    expect(c.isEpub).toBeTrue();
    // Als Ordner (nur Container/OPF/Kapitel einzeln), das Archiv bleibt der Rückfall.
    expect(c.epubDirUrl).toBe('/api/ebooks/9/epub/tok/');
    expect(c.fileUrl).toBe('/api/ebooks/9/file?t=tok');
    expect(c.epubLocator).toBe('epubcfi(/6/4!/4/2)');
    c.onEpubLocated({ fraction: 0.523, cfi: 'epubcfi(/6/8!/4/2)' });
    expect(c.epubPercent()).toBe(52);
    tick(1600);
    expect(service.setProgress).toHaveBeenCalledWith(9, 52, 100, 'epubcfi(/6/8!/4/2)');
    fixture.destroy();
  }));

  it('EPUB-Diagramme werden Bildanker statt Seitenzonen; Doppelklick stellt auf', async () => {
    const epubDiagrams = [
      { ...DIAGRAMS[1], id: 21, page: 4, x: 0, y: 0, w: 0, h: 0, label: null, imageRef: 'OEBPS/images/d1.png' },
      { ...DIAGRAMS[2], id: 22, page: 7, x: 0, y: 0, w: 0, h: 0, imageRef: 'OEBPS/images/d2.png' },
    ];
    const fixture = await make(book({ primaryFormat: 'epub', diagramState: 'done', diagramCount: 2,
      files: [{ id: 2, format: 'epub', fileName: 'b.epub', sizeBytes: 5 }], progress: null }));
    service.diagrams.and.returnValue(of({ state: 'done', items: epubDiagrams }));
    const c = fixture.componentInstance;
    (c as unknown as { loadDiagrams(id: number): void }).loadDiagrams(9);
    expect(c.zones()).toEqual([]);
    expect(c.epubImages().map(i => [i.id, i.chapter, i.ref])).toEqual([[21, 4, 'OEBPS/images/d1.png'], [22, 7, 'OEBPS/images/d2.png']]);
    expect(c.hasDiagrams).toBeTrue();
    c.pick(22);
    expect(c.picked()?.id).toBe(22);
    expect(c.pickedIndex).toBe(2);
    fixture.destroy();
  });

  it('die Brett-Leiste bekommt das gewählte Diagramm (Modi siehe ebook-board.component.spec.ts)', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.pick(11);
    fixture.detectChanges();
    const board = fixture.debugElement.query(By.directive(EbookBoardComponent));
    expect(board).not.toBeNull();
    expect((board.componentInstance as EbookBoardComponent).diagram.id).toBe(11);
    fixture.destroy();
  });

  it('blättert per Tastatur auch, wenn der Fokus im Brett liegt — nicht aus Eingabefeldern heraus', async () => {
    const fixture = await make(book());
    const c = fixture.componentInstance;
    const viewer = { goTo: jasmine.createSpy('goTo'), scrollByPx: jasmine.createSpy('scrollByPx') };
    (c as unknown as { viewer: unknown }).viewer = viewer;
    c.pageCount.set(120);
    c.page.set(42);
    const key = (k: string, target: HTMLElement) => {
      const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
      Object.defineProperty(e, 'target', { value: target });
      c.onDocumentKey(e);
      return e;
    };
    const board = document.createElement('div');
    expect(key('ArrowRight', board).defaultPrevented).toBeTrue();
    expect(viewer.goTo).toHaveBeenCalledWith(43);
    key('PageUp', board);
    expect(viewer.goTo).toHaveBeenCalledWith(41);
    key('ArrowDown', board);
    expect(viewer.scrollByPx).toHaveBeenCalledWith(80);
    const input = document.createElement('input');
    viewer.goTo.calls.reset();
    expect(key('ArrowRight', input).defaultPrevented).toBeFalse();
    expect(viewer.goTo).not.toHaveBeenCalled();

    // EPUB: dieselben Tasten blättern Seiten.
    const epub = { next: jasmine.createSpy('next'), prev: jasmine.createSpy('prev') };
    (c as unknown as { epub: unknown }).epub = epub;
    c.ebook = { ...c.ebook!, primaryFormat: 'epub' };
    key('ArrowRight', board);
    key('ArrowLeft', board);
    expect(epub.next).toHaveBeenCalledTimes(1);
    expect(epub.prev).toHaveBeenCalledTimes(1);
    fixture.destroy();
  });

  it('Handy: Brett als Vollbild über dem Buch (fixed, ganze Fläche), Escape schließt es', async () => {
    const css = compactCss(EbookReaderComponent);
    const narrow = css.slice(css.indexOf('@media(max-width:899.98px)'));
    expect(narrow).toMatch(/\.rd-panel(\[[^\]]*\])?\{display:none;position:fixed;inset:0/);
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.pick(11);
    expect(c.panelOpen()).toBeTrue();
    const e = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    Object.defineProperty(e, 'target', { value: document.createElement('div') });
    c.onDocumentKey(e);
    expect(c.panelOpen()).toBeFalse();
    fixture.destroy();
  });

  it('Herkunft der Lösung und Engine-Bewertung werden benannt', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    const d = { ...DIAGRAMS[0], solutionCheck: 'doubtful' as const, engineEval: 312 };
    expect(c.solutionNoteKey(d)).toBe('ebooks.reader.solutionDoubtful');
    expect(c.solutionNoteKey({ ...d, solutionCheck: 'engine' })).toBe('ebooks.reader.solutionEngine');
    expect(c.solutionNoteKey({ ...d, solutionCheck: 'nopuzzle' })).toBeNull();
    expect(c.evalText(d)).toMatch(/^\+3[.,]1$/);
    expect(c.evalText({ ...d, engineEval: 9998 })).toBe('ebooks.reader.mateIn');
    expect(c.evalText({ ...d, engineEval: null })).toBeNull();
  });

  // ── UI-Sweep c1 ─────────────────────────────────────────────────────────────────────────────
  async function makeFailing(err: unknown) {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['get', 'accessToken']);
    service.get.and.returnValue(throwError(() => err));
    await TestBed.configureTestingModule({
      imports: [EbookReaderComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '9']]) } } },
        { provide: SnackbarService, useValue: { warn: () => {}, quick: () => {} } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookReaderComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** Kompiliertes CSS ohne Leerraum und ohne %NS%-Platzhalter (Muster page-width.spec.ts). */
  function compactCss(cmp: Type<unknown>): string {
    return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
  }

  it('c1-eb-reader-404-misleading: 404/403 = „gibt es nicht“ mit Weg zur Bibliothek, kein „Wiederholen“', async () => {
    for (const status of [404, 403]) {
      TestBed.resetTestingModule();
      const fixture = await makeFailing(new HttpErrorResponse({ status }));
      const c = fixture.componentInstance;
      expect(c.notFound).withContext(`Status ${status}`).toBeTrue();
      expect(c.error).toBeFalse();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.textContent).toContain('ebooks.reader.notFound');
      expect(el.querySelector('app-load-error')).toBeNull();
      expect(el.querySelector('a[href="/ebooks"]')).not.toBeNull();
      fixture.destroy();
    }
  });

  it('c1-eb-reader-404-misleading: Netz-/Serverfehler bleibt Ladefehler mit „Wiederholen“ und Zurück-Link', async () => {
    const fixture = await makeFailing(new HttpErrorResponse({ status: 500 }));
    expect(fixture.componentInstance.error).toBeTrue();
    expect(fixture.componentInstance.notFound).toBeFalse();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('a[href="/ebooks"]')).not.toBeNull();
    // Die Zeile steht in der Seitenbreite, nicht am Fensterrand.
    expect(compactCss(EbookReaderComponent)).toMatch(/\.rd-pad(\[[^\]]*\])?\{[^}]*max-width:min\(var\(--page-max-width,1240px\),96vw\)[^}]*margin:0auto/);
    fixture.destroy();
  });

  it('c2-eb-reader-ultrawide-kopfkante: geladener Leser markiert sich randlos (data-nav-edge="full"), Fehlerzustand nicht', async () => {
    const fixture = await make(book());
    expect((fixture.nativeElement as HTMLElement).querySelector('.rd-page')!.getAttribute('data-nav-edge')).toBe('full');
    fixture.destroy();
    TestBed.resetTestingModule();
    const failing = await makeFailing(new HttpErrorResponse({ status: 500 }));
    expect((failing.nativeElement as HTMLElement).querySelector('.rd-page')!.hasAttribute('data-nav-edge')).toBeFalse();
    failing.destroy();
  });

  it('c2-eb-reader-handy-streifen-unten: unter 600 px rechnet der Leser mit der 56-px-Kopfleiste, darüber mit 64 px', () => {
    const css = compactCss(EbookReaderComponent);
    expect(css).toMatch(/\.rd-page(\[[^\]]*\])?\{[^}]*height:calc\(100dvh-var\(--rh-navbar-height,64px\)\)/);
    const mobile = css.slice(css.indexOf('@media(max-width:599.98px)'));
    expect(mobile).toMatch(/^@media\(max-width:599\.98px\)\{\.rd-page(\[[^\]]*\])?\{height:calc\(100dvh-var\(--rh-navbar-height,56px\)\)/);
  });

  it('c1-eb-ebook-leser-seitenfeld-spinner: Seitenfeld ohne Drehknöpfe (auch WebKit) und mit Ziffern-Tastatur', async () => {
    const fixture = await make(book());
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.rd-page-input')!;
    expect(input.getAttribute('inputmode')).toBe('numeric');
    const css = compactCss(EbookReaderComponent);
    expect(css).toMatch(/\.rd-page-input(\[[^\]]*\])?\{[^}]*appearance:textfield/);
    expect(css).toMatch(/\.rd-page-input(\[[^\]]*\])?::-webkit-inner-spin-button[^{]*\{[^}]*appearance:none/);
    fixture.destroy();
  });

  it('c1-eb-ebook-leser-seite-scrollt-um-fusszeile: Body-Klasse blendet die Fußzeile aus, solange der Leser offen ist', async () => {
    const fixture = await make(book());
    expect(document.body.classList.contains('rd-reader-mode')).toBeTrue();
    fixture.destroy();
    expect(document.body.classList.contains('rd-reader-mode')).toBeFalse();
    // Die globale Regel dazu steht in styles.scss (Karma lädt sie).
    let found = false;
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (r instanceof CSSStyleRule && r.selectorText === 'body.rd-reader-mode app-footer' && r.style.display === 'none') found = true;
      }
    }
    expect(found).withContext('styles.scss: body.rd-reader-mode app-footer { display: none }').toBeTrue();
  });

  it('c1-eb-reader-mobile-diagrams-undiscoverable: Diagramm-Knopf in der Leiste klappt das Brett auf (nur < 900 px sichtbar)', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const btn = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.rd-diag-btn')!;
    expect(btn).not.toBeNull();
    expect(btn.textContent).toContain('3');
    expect(btn.getAttribute('aria-label')).toBe('ebooks.reader.openBoard');
    expect(c.panelOpen()).toBeFalse();
    btn.click();
    expect(c.panelOpen()).toBeTrue();
    const css = compactCss(EbookReaderComponent);
    expect(css).toMatch(/\.rd-diag-btn(\[[^\]]*\])?\{display:none/);
    const narrow = css.slice(css.indexOf('@media(max-width:899.98px)'));
    expect(narrow).toMatch(/\.rd-diag-btn(\[[^\]]*\])?\{display:inline-flex/);
    // Touch: Zonen dauerhaft sichtbar (kein Überfahren).
    const pv = compactCss(PdfViewerComponent);
    expect(pv.slice(pv.indexOf('@media(hover:none)'))).toMatch(/\.pv-zone\{border:2pxdashedvar\(--mat-sys-primary\)/);
    fixture.destroy();
  });

  it('ohne Diagramme kein Diagramm-Knopf', async () => {
    const fixture = await make(book());
    expect((fixture.nativeElement as HTMLElement).querySelector('.rd-diag-btn')).toBeNull();
    fixture.destroy();
  });

  it('c1-eb-reader-ultrawide-layout: ab 2400 px Seite/Zoom neben dem Titel, Brett-Leiste bis zur Fensterhöhe — darunter unverändert', () => {
    const css = compactCss(EbookReaderComponent);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at);
    expect(wide).toMatch(/\.rd-title(\[[^\]]*\])?\{flex:01auto/);
    expect(wide).toMatch(/\.rd-toolbar-spacer(\[[^\]]*\])?\{display:block;flex:1/);
    // User 15.10.: ab 2400 px wächst die Brett-Spalte bis zur Fensterhöhe (vorher fest höchstens 720 px).
    expect(wide).toMatch(/\.rd-panel(\[[^\]]*\])?\{flex:00var\(--rd-panel-w,clamp\(460px,(calc\()?100dvh-220px\)?,46vw\)\)/);
    expect(wide).not.toContain('720px');
    expect(css.slice(0, at)).toMatch(/\.rd-toolbar-spacer(\[[^\]]*\])?\{display:none/);
    expect(css.slice(0, at)).toContain('clamp(300px,32vw,460px)');
  });

  // ── 0.785.x E-Book-Leser kompakt ─────────────────────────────────────────────────────────────
  it('EINE Leiste: geladen dockt die Kopfleiste an (body.nav-docked), Fehlerzustand nicht; beim Verlassen weg', async () => {
    const fixture = await make(book());
    expect(document.body.classList.contains('nav-docked')).toBeTrue();
    fixture.destroy();
    expect(document.body.classList.contains('nav-docked')).toBeFalse();
    TestBed.resetTestingModule();
    const failing = await makeFailing(new HttpErrorResponse({ status: 500 }));
    expect(document.body.classList.contains('nav-docked')).toBeFalse();
    failing.destroy();
  });

  it('EINE Leiste: fensterhoch, Leiste 48 px, rechts die gemessene Breite der Kopfleiste frei (am Handy in der Titelzeile)', () => {
    const css = compactCss(EbookReaderComponent);
    expect(css).toMatch(/body\.nav-docked[^{]*\.rd-page(\[[^\]]*\])?\{height:100dvh;?\}/);
    expect(css).toMatch(/\.rd-toolbar(\[[^\]]*\])?\{[^}]*min-height:48px;box-sizing:border-box;padding:4px8px/);
    const wide = css.slice(css.indexOf('@media(min-width:600px)'));
    expect(wide).toMatch(/^@media\(min-width:600px\)\{[^{]*body\.nav-docked[^{]*\.rd-toolbar(\[[^\]]*\])?\{padding-right:calc\(8px\+var\(--rh-nav-w,96px\)\)/);
    const mobile = css.slice(css.lastIndexOf('@media(max-width:599.98px)'));
    expect(mobile).toMatch(/body\.nav-docked[^{]*\.rd-title(\[[^\]]*\])?\{flex-basis:calc\(100%-96px-var\(--rh-nav-w,96px\)\)/);
    expect(CSS.supports('flex-basis', 'calc(100% - 96px - var(--rh-nav-w, 96px))')).toBeTrue();
  });

  it('Brett-Kopf: Zugrecht + Bewertung + Herkunft als zweite Kopfzeile, Melden im ⋮ des Bretts, „fraglich“ bleibt sichtbar', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    Object.defineProperty(c, 'loggedIn', { get: () => true });
    c.diagrams.update(l => l.map(d => d.id === 11 ? { ...d, engineEval: 350, solutionCheck: 'engine' as const } : d));
    c.pick(11);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const head = el.querySelector('.rd-panel-head')!;
    expect(head.querySelector('.rd-side')!.textContent).toContain('common.blackToMove');
    expect(head.querySelector('.rd-eval')!.textContent).toMatch(/\+3[.,]5/);
    expect(head.querySelector('.rd-sol-icon')!.getAttribute('aria-label')).toBe('ebooks.reader.solutionEngine');
    expect(el.querySelector('.rd-report')).toBeNull();
    expect(el.querySelector('.rd-panel > .rd-side')).toBeNull();
    const board = fixture.debugElement.query(By.directive(EbookBoardComponent)).componentInstance as EbookBoardComponent;
    expect(board.reportable).toBeTrue();
    service.reportDiagram.and.returnValue(of({ reported: true }));
    board.report.emit();
    expect(service.reportDiagram).toHaveBeenCalledWith(9, 11, true);
    c.diagrams.update(l => l.map(d => d.id === 12 ? { ...d, solutionCheck: 'doubtful' as const } : d));
    c.pick(12);
    fixture.detectChanges();
    expect(el.querySelector('.rd-sol-note--warn')!.textContent).toContain('ebooks.reader.solutionDoubtful');
    expect(el.querySelector('.rd-panel-head .rd-sol-icon')).toBeNull();
    expect(c.solutionNoteIcon({ ...DIAGRAMS[0], solutionCheck: 'replaced' })).toBe('published_with_changes');
    fixture.destroy();
  });

  it('⋮ des Lesers behält Download und „Als Kurs anlegen“', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const el: HTMLElement = fixture.nativeElement;
    Object.defineProperty(fixture.componentInstance, 'loggedIn', { get: () => true });
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.rd-more')!.click();
    fixture.detectChanges(); await fixture.whenStable();
    const text = document.querySelector('.cdk-overlay-container')!.textContent!;
    expect(text).toContain('ebooks.downloadFormat');
    expect(text).toContain('ebooks.reader.createCourse');
    fixture.destroy();
  });
});
