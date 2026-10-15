import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { EpubViewerComponent, epubImagePath } from './epub-viewer.component';
import { setEinkActive } from '../../core/eink';

const BASE = 'http://rh.test/api/ebooks/9/epub/tok/';

describe('epubImagePath', () => {
  const none = new Map<string, string>();

  it('Ordner: relative Bildadresse gegen das Kapitel auflösen und die Buch-Basis abschneiden', () => {
    expect(epubImagePath('../Images/d1.png', BASE + 'OEBPS/Text/ch1.xhtml', BASE, none)).toBe('OEBPS/Images/d1.png');
    expect(epubImagePath('img/Board%201.png?x=1#f', BASE + 'OEBPS/ch.xhtml', BASE, none)).toBe('OEBPS/img/Board 1.png');
    expect(epubImagePath(BASE + 'OEBPS/images/img_p17-1.jpg', BASE + 'OEBPS/ch.xhtml', BASE, none)).toBe('OEBPS/images/img_p17-1.jpg');
  });

  it('Archiv: blob:-URL über die Ersetzungs-Tabelle; ohne Basis bleibt der rohe Pfad', () => {
    const blobs = new Map([['blob:http://rh.test/abc', '/OEBPS/images/d2.png']]);
    expect(epubImagePath('blob:http://rh.test/abc', 'about:blank', null, blobs)).toBe('OEBPS/images/d2.png');
    expect(epubImagePath('/OEBPS/images/d3.png', 'about:blank', null, none)).toBe('OEBPS/images/d3.png');
    // Fremde Adresse (nicht unter der Basis): roh, der Dateiname-Rückfall greift danach.
    expect(epubImagePath('http://elsewhere/x/d4.png', BASE + 'OEBPS/ch.xhtml', BASE, none)).toBe('http://elsewhere/x/d4.png');
  });
});

describe('EpubViewerComponent', () => {
  function make(dirSrc: string | null) {
    TestBed.configureTestingModule({ imports: [EpubViewerComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const fixture = TestBed.createComponent(EpubViewerComponent);
    const c = fixture.componentInstance;
    c.src = '/api/ebooks/9/file?t=tok';
    c.dirSrc = dirSrc;
    return c;
  }

  function fakeBook(open: () => Promise<unknown>) {
    return { open: jasmine.createSpy('open').and.callFake(open), destroy: jasmine.createSpy('destroy') };
  }

  it('Mausrad blättert seitenweise — kleine Schritte sammeln, dann kurze Pause gegen Doppelblättern', () => {
    const c = make(null);
    const next = spyOn(c, 'next');
    const prev = spyOn(c, 'prev');
    let now = 10_000;
    spyOn(Date, 'now').and.callFake(() => now);
    const wheel = (dy: number, mode = 0) => c.onWheel(new WheelEvent('wheel', { deltaY: dy, deltaMode: mode }));
    wheel(15); wheel(15);
    expect(next).not.toHaveBeenCalled();          // 30 px (Touchpad): noch kein Umblättern
    wheel(20);
    expect(next).toHaveBeenCalledTimes(1);        // 50 px
    now += 100; wheel(200);
    expect(next).toHaveBeenCalledTimes(1);        // Pause läuft noch
    now += 400; wheel(-3, 1);                     // 3 Zeilen hoch (Firefox)
    expect(prev).toHaveBeenCalledTimes(1);
  });

  it('Darstellung (0.791.0): „Seite“ = paginiert eine Spalte, „Fließend“ = scrollen; Schriftgröße als Prozent; fließend blättert das Rad nicht', () => {
    const c = make(null);
    const made: Record<string, unknown>[] = [];
    const fontSizes: string[] = [];
    const rendition = {
      themes: { default: () => {}, fontSize: (s: string) => fontSizes.push(s) },
      hooks: { content: { register: () => {} } }, on: () => {},
    };
    const book = { renderTo: (_el: HTMLElement, o: Record<string, unknown>) => { made.push(o); return rendition; } };
    const create = (c as unknown as { createRendition(b: unknown): unknown }).createRendition.bind(c);
    c.fontScale = 125;
    create(book);
    expect(made[0]).toEqual(jasmine.objectContaining({ flow: 'paginated', spread: 'none' }));
    expect(fontSizes).toEqual(['125%']);
    c.layout = 'scroll';
    create(book);
    expect(made[1]).toEqual(jasmine.objectContaining({ flow: 'scrolled', manager: 'continuous' }));
    const next = spyOn(c, 'next');
    c.onWheel(new WheelEvent('wheel', { deltaY: 300 }));
    expect(next).not.toHaveBeenCalled();
  });

  it('Größenänderung des Bereichs (Brett-Spalte gezogen) misst epub.js neu — nicht beim ersten Messen, nicht ohne Änderung', () => {
    const c = make(null);
    const resize = jasmine.createSpy('resize');
    (c as unknown as { rendition: unknown }).rendition = { resize };
    spyOn(window, 'requestAnimationFrame').and.callFake((f: FrameRequestCallback) => { f(0); return 1; });
    const area = (c as unknown as { area: { nativeElement: HTMLElement } }).area.nativeElement;
    let w = 800;
    spyOnProperty(area, 'clientWidth').and.callFake(() => w);
    spyOnProperty(area, 'clientHeight').and.returnValue(600);
    c.onAreaResize();
    expect(resize).not.toHaveBeenCalled();             // Aufbau
    c.onAreaResize();
    expect(resize).not.toHaveBeenCalled();             // keine Änderung
    w = 520;
    c.onAreaResize();
    expect(resize).toHaveBeenCalledOnceWith(520, 600);
  });

  it('öffnet als Ordner (absolute Basis, ohne Ersetzungen) und lädt das Archiv NICHT', async () => {
    const c = make('/api/ebooks/9/epub/tok/');
    const book = fakeBook(() => Promise.resolve());
    const ePub = jasmine.createSpy('ePub').and.returnValue(book);
    const fetchSpy = spyOn(window, 'fetch');
    const result = await c.openBook(ePub as never);
    expect(result as unknown).toBe(book);
    expect(ePub).toHaveBeenCalledOnceWith({ replacements: 'none' });
    expect(book.open).toHaveBeenCalledOnceWith(new URL('/api/ebooks/9/epub/tok/', document.baseURI).href, 'directory');
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('fällt aufs ganze Archiv zurück, wenn das Öffnen als Ordner scheitert', async () => {
    const c = make('/api/ebooks/9/epub/tok/');
    const dirBook = fakeBook(() => Promise.reject(new Error('404')));
    const archiveBook = { destroy: () => {} };
    const ePub = jasmine.createSpy('ePub').and.returnValues(dirBook, archiveBook);
    const data = new ArrayBuffer(4);
    spyOn(window, 'fetch').and.resolveTo(new Response(data));
    const result = await c.openBook(ePub as never);
    expect(result as unknown).toBe(archiveBook);
    expect(dirBook.destroy).toHaveBeenCalled();
    expect(ePub.calls.mostRecent().args[0]).toEqual(jasmine.any(ArrayBuffer));
    expect(window.fetch).toHaveBeenCalledWith('/api/ebooks/9/file?t=tok', { credentials: 'same-origin' });
  });

  it('ohne dirSrc wie bisher das Archiv', async () => {
    const c = make(null);
    const ePub = jasmine.createSpy('ePub').and.returnValue({});
    spyOn(window, 'fetch').and.resolveTo(new Response(new ArrayBuffer(2)));
    await c.openBook(ePub as never);
    expect(ePub).toHaveBeenCalledOnceWith(jasmine.any(ArrayBuffer));
  });

  it('markiert im Ordner-Modus die Diagrammbilder (relativ zum Kapitel) und meldet den Doppelklick', async () => {
    const c = make(BASE);
    await c.openBook(jasmine.createSpy('ePub').and.returnValue(fakeBook(() => Promise.resolve())) as never);
    c.images = [{ id: 21, chapter: 4, ref: 'OEBPS/Images/d1.png' }, { id: 22, chapter: 4, ref: 'OEBPS/other/d2.png' }];
    c.activeImage = 21;
    const doc = document.implementation.createHTMLDocument('ch');
    const base = doc.createElement('base');
    base.href = BASE + 'OEBPS/Text/ch1.xhtml';
    doc.head.appendChild(base);
    doc.body.innerHTML = '<img id="a" src="../Images/d1.png"><img id="b" src="../Images/plain.png"><img id="c" src="../x/d2.png">';
    (c as unknown as { markImages(x: { document: Document }): void }).markImages({ document: doc });

    const a = doc.getElementById('a')!;
    expect(a.classList).toContain('rh-diagram');
    expect(a.classList).toContain('rh-diagram--active');
    expect(a.getAttribute('data-rh-ref')).toBe('OEBPS/Images/d1.png');
    expect(doc.getElementById('b')!.classList).not.toContain('rh-diagram');
    // Pfad passt nicht exakt, Dateiname eindeutig → Rückfall über den Namen.
    expect(doc.getElementById('c')!.getAttribute('data-rh-ref')).toBe('OEBPS/other/d2.png');

    const picked: number[] = [];
    c.imagePick.subscribe(id => picked.push(id));
    a.dispatchEvent(new MouseEvent('dblclick'));
    expect(picked).toEqual([21]);
  });

  it('E-Ink-Modus: Kapitel-Dokument bekommt die Klasse eink und die Regel ohne Dauerrahmen', async () => {
    const c = make(BASE);
    await c.openBook(jasmine.createSpy('ePub').and.returnValue(fakeBook(() => Promise.resolve())) as never);
    c.images = [{ id: 21, chapter: 4, ref: 'OEBPS/Images/d1.png' }];
    const mark = (doc: Document) => (c as unknown as { markImages(x: { document: Document }): void }).markImages({ document: doc });
    setEinkActive(true);
    try {
      const doc = document.implementation.createHTMLDocument('ch');
      mark(doc);
      expect(doc.documentElement.classList).toContain('eink');
      expect(doc.getElementById('rh-diagram-style')!.textContent).toContain('html.eink .rh-diagram:not(.rh-diagram--active)');
    } finally { setEinkActive(false); }
    const plain = document.implementation.createHTMLDocument('ch2');
    mark(plain);
    expect(plain.documentElement.classList).not.toContain('eink');
  });

  it('Doppelklick auf ein Bild OHNE erkanntes Diagramm oder auf Text meldet die Stelle (Bildpfad + Kapitel), erkannte nicht', async () => {
    const c = make(BASE);
    await c.openBook(jasmine.createSpy('ePub').and.returnValue(fakeBook(() => Promise.resolve())) as never);
    (c as unknown as { book: unknown }).book = { spine: { get: (i: number) => ({ href: `OEBPS/Text/ch${i + 1}.xhtml` }) } };
    c.images = [{ id: 21, chapter: 3, ref: 'OEBPS/Images/d1.png' }];
    const doc = document.implementation.createHTMLDocument('ch');
    const base = doc.createElement('base');
    base.href = BASE + 'OEBPS/Text/ch3.xhtml';
    doc.head.appendChild(base);
    doc.body.innerHTML = '<img id="a" src="../Images/d1.png"><img id="b" src="../Images/plain.png"><p id="t">Text</p>';
    const contents = { document: doc, sectionIndex: 2 };
    const host = c as unknown as { markImages(x: unknown): void; listenMissed(x: unknown): void };
    host.markImages(contents);
    host.listenMissed(contents);
    host.listenMissed(contents);   // zweimal registriert = trotzdem ein Handler

    const spots: unknown[] = [];
    c.missedPick.subscribe(s => spots.push(s));
    doc.getElementById('a')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    doc.getElementById('b')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    doc.getElementById('t')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    expect(spots).toEqual([
      { ref: 'OEBPS/Images/plain.png', chapter: 'OEBPS/Text/ch3.xhtml' },
      { ref: null, chapter: 'OEBPS/Text/ch3.xhtml' },
    ]);
  });
});
