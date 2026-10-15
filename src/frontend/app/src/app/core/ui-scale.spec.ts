import { fakeAsync, tick } from '@angular/core/testing';
import {
  UI_SCALE_STORAGE_KEY, UI_SCALE_VERIFY_MS, uiScaleDiagnostics, UI_SCALE_TOUCH_QUERY, UI_SCALE_ZOOM_CLASS, applyUiScale, applyUiScaleEarly, currentUiScale,
  currentUiScaleMode, normalizeUiScale, readUiScale, resetUiScaleForTests, screenCssWidth, viewportContentFor, writeUiScale,
  zoomIsStandardized,
} from './ui-scale';
import { UiScaleService } from './ui-scale.service';

/** UI-Skalierung je Gerät (docs/DESIGN.md 3.7): Speicherung, Meta-Viewport (Touch) bzw. zoom (Desktop). */
describe('UI-Skalierung', () => {
  let saved: string | null;
  let meta: HTMLMetaElement;
  let metaBefore: string;
  const overrides: Array<() => void> = [];

  /** Eigenschaft nur für diesen Test auf der Instanz überschreiben (Getter liegen sonst am Prototyp). */
  function fake<T extends object>(target: T, prop: string, value: unknown): void {
    Object.defineProperty(target, prop, { configurable: true, get: () => value });
    overrides.push(() => delete (target as Record<string, unknown>)[prop]);
  }
  function fakeTouch(touch: boolean): void {
    const real = window.matchMedia.bind(window);
    spyOn(window, 'matchMedia').and.callFake((q: string) =>
      q === UI_SCALE_TOUCH_QUERY ? ({ matches: touch, media: q, addEventListener() {}, removeEventListener() {} }) as unknown as MediaQueryList : real(q));
  }

  beforeEach(() => {
    saved = localStorage.getItem(UI_SCALE_STORAGE_KEY);
    localStorage.removeItem(UI_SCALE_STORAGE_KEY);
    meta = document.querySelector('meta[name="viewport"]') ?? Object.assign(document.createElement('meta'), { name: 'viewport' });
    if (!meta.isConnected) document.head.appendChild(meta);
    metaBefore = meta.content;
    meta.content = 'width=device-width, initial-scale=1';
    resetUiScaleForTests();
  });
  afterEach(() => {
    resetUiScaleForTests();
    while (overrides.length) overrides.pop()!();
    meta.content = metaBefore;
    if (saved === null) localStorage.removeItem(UI_SCALE_STORAGE_KEY); else localStorage.setItem(UI_SCALE_STORAGE_KEY, saved);
  });

  it('nur die Stufen 100/125/150/175/200 gelten, alles andere ist 100', () => {
    expect(normalizeUiScale('150')).toBe(150);
    expect(normalizeUiScale(200)).toBe(200);
    for (const bad of ['', null, undefined, 'abc', '300', 90, 0, '1.5']) expect(normalizeUiScale(bad)).toBe(100);
  });

  it('speichert je Gerät als Ganzzahl; 100 entfernt den Schlüssel', () => {
    expect(readUiScale()).toBe(100);
    expect(writeUiScale(175)).toBeTrue();
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('175');
    expect(readUiScale()).toBe(175);
    expect(writeUiScale(100)).toBeTrue();
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBeNull();
    localStorage.setItem(UI_SCALE_STORAGE_KEY, 'kaputt');
    expect(readUiScale()).toBe(100);
  });

  it('meldet ein gescheitertes Speichern', () => {
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    expect(writeUiScale(150)).toBeFalse();
  });

  it('Meta-Viewport: Breite = Bildschirmbreite / Stufe, dazu initial-scale', () => {
    expect(viewportContentFor(200, 960)).toBe('width=480, initial-scale=2');
    expect(viewportContentFor(150, 960)).toBe('width=640, initial-scale=1.5');
    expect(viewportContentFor(125, 1280)).toBe('width=1024, initial-scale=1.25');
    expect(viewportContentFor(100, 960)).toBe('width=device-width, initial-scale=1');
    expect(viewportContentFor(200, 0)).toBe('width=device-width, initial-scale=1');
  });

  it('Bildschirmbreite folgt der Lage (iOS dreht screen.width nicht mit)', () => {
    fake(screen, 'width', 960);
    fake(screen, 'height', 1280);
    fake(screen, 'orientation', { type: 'landscape-primary' });
    expect(screenCssWidth()).toBe(1280);
    fake(screen, 'orientation', { type: 'portrait-primary' });
    expect(screenCssWidth()).toBe(960);
  });

  it('Desktop (Meta-Viewport wirkt nicht): zoom auf <html>, Chessground-Container hebt ihn auf; 100 räumt ab', () => {
    fakeTouch(false);
    applyUiScale(150);
    const root = document.documentElement;
    expect(currentUiScaleMode()).toBe('zoom');
    expect(root.style.zoom).toBe('1.5');
    expect(root.classList.contains(UI_SCALE_ZOOM_CLASS)).toBeTrue();
    expect(document.getElementById('rh-ui-scale-style')?.textContent).toContain('cg-container');
    expect(meta.content).toBe('width=device-width, initial-scale=1');
    applyUiScale(100);
    expect(root.style.zoom).toBe('');
    expect(root.classList.contains(UI_SCALE_ZOOM_CLASS)).toBeFalse();
  });

  it('Touch-Gerät, das den Meta-Viewport auswertet: Meta-Viewport statt zoom, nach Drehen neu berechnet', () => {
    fakeTouch(true);
    fake(screen, 'width', 960);
    fake(screen, 'height', 1280);
    fake(screen, 'orientation', { type: 'portrait-primary' });
    fake(document.documentElement, 'clientWidth', 480); // so meldet ein Mobilbrowser die neue Layoutbreite
    applyUiScale(200);
    expect(currentUiScaleMode()).toBe('viewport');
    expect(meta.content).toBe('width=480, initial-scale=2');
    expect(document.documentElement.style.zoom).toBe('');
    fake(screen, 'orientation', { type: 'landscape-primary' });
    window.dispatchEvent(new Event('resize'));
    expect(meta.content).toBe('width=640, initial-scale=2');
    applyUiScale(100);
    expect(meta.content).toBe('width=device-width, initial-scale=1');
  });

  it('Touch-Gerät, das den Meta-Viewport ignoriert (Windows-Tablet): nach der Nachprüfung zurück auf device-width und zoom', fakeAsync(() => {
    fakeTouch(true);
    fake(screen, 'width', 960);
    fake(screen, 'height', 1280);
    fake(document.documentElement, 'clientWidth', 960); // Layoutbreite bleibt
    applyUiScale(200);
    expect(currentUiScaleMode()).toBe('viewport');      // erst versuchen …
    tick(UI_SCALE_VERIFY_MS);
    expect(currentUiScaleMode()).toBe('zoom');          // … dann zurückfallen
    expect(meta.content).toBe('width=device-width, initial-scale=1');
    expect(document.documentElement.style.zoom).toBe('2');
    expect(uiScaleDiagnostics().viewportOk).toBeFalse();
  }));

  // Regression 0.800.2 (User am Boox Tab X C, Chrome 111: „funktioniert überhaupt nicht“): echtes Android-Chrome
  // übernimmt die neue Layoutbreite erst ein paar Bilder später. Die alte Sofortprüfung sah noch 960 px, fiel auf zoom
  // zurück — in Chrome 111 nicht standardisiert → `none`, die Auswahl tat nichts.
  it('Boox: Layoutbreite folgt erst verzögert, zoom nicht standardisiert → bleibt beim Meta-Viewport, nie `none`', fakeAsync(() => {
    fakeTouch(true);
    fake(screen, 'width', 960);
    fake(screen, 'height', 1280);
    fake(screen, 'orientation', { type: 'portrait-primary' });
    fake(document.documentElement, 'clientWidth', 960);  // im selben Takt noch die alte Breite
    spyOn(Element.prototype, 'getBoundingClientRect').and.returnValue({ width: 10 } as DOMRect); // zoom alt (Chrome 111)
    const svc = new UiScaleService();
    svc.set(200);
    expect(currentUiScaleMode()).toBe('viewport');
    expect(meta.content).toBe('width=480, initial-scale=2');
    expect(svc.unsupported()).toBeFalse();
    fake(document.documentElement, 'clientWidth', 480);  // ein paar Bilder später
    tick(UI_SCALE_VERIFY_MS);
    expect(currentUiScaleMode()).toBe('viewport');
    expect(svc.diagnostics()).toEqual(jasmine.objectContaining({ mode: 'viewport', layoutWidth: 480, screenWidth: 960, viewportOk: true }));
  }));

  it('erkennt standardisiertes zoom (Karma-Chrome ≥ 128); altes Modell (gBCR ungezoomt) → nichts skalieren', () => {
    expect(zoomIsStandardized()).toBeTrue();
    fakeTouch(false);
    spyOn(Element.prototype, 'getBoundingClientRect').and.returnValue({ width: 10 } as DOMRect);
    expect(zoomIsStandardized()).toBeFalse();
    const svc = new UiScaleService();
    svc.set(150);
    expect(currentUiScaleMode()).toBe('none');
    expect(document.documentElement.style.zoom).toBe('');
    expect(svc.unsupported()).toBeTrue();
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('150'); // Wahl bleibt, falls das Gerät es später kann
  });

  it('früher Start wendet die gespeicherte Stufe an, ohne Wahl bleibt alles unberührt', () => {
    fakeTouch(false);
    applyUiScaleEarly();
    expect(currentUiScaleMode()).toBeNull();
    expect(document.documentElement.style.zoom).toBe('');
    localStorage.setItem(UI_SCALE_STORAGE_KEY, '125');
    applyUiScaleEarly();
    expect(currentUiScale()).toBe(125);
    expect(document.documentElement.style.zoom).toBe('1.25');
  });

  it('Dienst: set() speichert, wendet sofort an und spiegelt das Signal', () => {
    fakeTouch(false);
    const svc = new UiScaleService();
    expect(svc.scale()).toBe(100);
    expect(svc.set(175)).toBeTrue();
    expect(svc.scale()).toBe(175);
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('175');
    expect(document.documentElement.style.zoom).toBe('1.75');
    svc.set(100);
    expect(svc.scale()).toBe(100);
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBeNull();
  });
});
