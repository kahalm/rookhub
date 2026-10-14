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
  pageCount: 120, hasCover: false, diagramState: 'unsupported', diagramCount: 0, ownerUserId: 1, ownerName: null, isOwner: true, canManage: true, groupIds: [],
  files: [{ id: 1, format: 'pdf', fileName: 'b.pdf', sizeBytes: 1000 }], primaryFormat: 'pdf',
  progress: { page: 42, pageCount: 120, updatedAt: '' }, createdAt: '', updatedAt: '', ...over,
});

const DIAGRAMS = [
  { id: 11, page: 33, x: 0.1, y: 0.1, w: 0.3, h: 0.2, fen: 'r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b - - 0 1', label: '1', caption: 'Hamppe – Steinitz', sideKnown: true, reportedByMe: false, reportCount: 0 },
  { id: 12, page: 33, x: 0.1, y: 0.4, w: 0.3, h: 0.2, fen: 'rnb3kr/ppp4p/3b3B/3Pp2n/2BP4/3K1Rp1/PPP3q1/RN1Q4 w - - 0 1', label: '2', caption: null, sideKnown: true, reportedByMe: false, reportCount: 0 },
  { id: 13, page: 40, x: 0.5, y: 0.1, w: 0.3, h: 0.2, fen: '8/8/8/8/8/8/8/K6k w - - 0 1', label: null, caption: null, sideKnown: false, reportedByMe: false, reportCount: 0 },
];

describe('EbookReaderComponent', () => {
  let service: jasmine.SpyObj<EbookService>;

  async function make(b: Ebook) {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['get', 'accessToken', 'fileUrl', 'setProgress', 'forgetToken', 'diagrams', 'reportDiagram']);
    service.diagrams.and.returnValue(of({ state: 'done', items: DIAGRAMS }));
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
    expect(c.flipped()).toBeTrue();
    expect(c.panelOpen()).toBeTrue();
    expect(c.analysisParams(c.picked()!)).toEqual({ fen: DIAGRAMS[0].fen, orientation: 'black', from: '/ebooks/9' });
    c.step(1);
    expect(c.picked()!.id).toBe(12);
    expect(c.flipped()).toBeFalse();
    expect(c.pickedIndex).toBe(2);
    c.step(1); c.step(1);
    expect(c.picked()!.id).toBe(13);   // bleibt am Ende stehen
    expect(c.sideKey(c.picked()!)).toBe('ebooks.reader.sideUnknown');
    c.step(-1);
    expect(c.picked()!.id).toBe(12);
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

  it('„Brett spielbar": Züge werden mitgeschrieben, Rücknahme und Buchstellung, Analyse nimmt die aktuelle Stellung', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.pick(11);   // Schwarz am Zug
    expect(c.legal()).toBeTrue();
    c.togglePlay();
    expect(c.playMode()).toBeTrue();
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: 'r7/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5r/4RR1K w - - 0 2' });
    c.onBoardMove({ from: 'h1', to: 'h2', san: 'Kxh2', fen: 'r7/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5K/4RR2 b - - 0 2' });
    expect(c.movesText).toBe('1… Rxh2+ 2. Kxh2');
    expect(c.analysisParams(c.picked()!)['fen']).toContain('BP5K');
    c.undoMove();
    expect(c.boardFen()).toContain('BP5r');
    c.resetMoves();
    expect(c.boardFen()).toBe(DIAGRAMS[0].fen);
    c.step(1);   // neues Diagramm: Züge weg
    expect(c.played().length).toBe(0);
  });

  it('eine regelwidrige Buchstellung bleibt nur zum Ansehen', async () => {
    const fixture = await make(book({ diagramState: 'done', diagramCount: 3 }));
    const c = fixture.componentInstance;
    c.diagrams.update(l => [...l, { ...DIAGRAMS[2], id: 99, fen: '8/8/8/8/8/8/8/8 w - - 0 1' }]);
    c.pick(99);
    expect(c.legal()).toBeFalse();
  });
});
