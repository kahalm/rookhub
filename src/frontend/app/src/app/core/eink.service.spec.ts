import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MAT_RIPPLE_GLOBAL_OPTIONS, MATERIAL_ANIMATIONS } from '@angular/material/core';
import {
  EINK_CLASS, EINK_FLASH_PHASE_MS, EINK_MEDIA_QUERY, EINK_STORAGE_KEY, applyEinkEarly, flashEinkRefresh, isBoardSwitch, boardAnimation, isEinkActive, readEinkChoice,
  resolveEink, setEinkActive,
} from './eink';
import { EinkService, provideEinkMaterial } from './eink.service';
import { ThemeService } from './theme.service';

/** E-Ink-Modus (docs/DESIGN.md 3.6): Wahl je Gerät, Media-Query als Vorgabe, Klasse an <html>/<body>. */
describe('E-Ink-Modus', () => {
  let savedChoice: string | null;
  let wasDark: boolean;

  function fakeMedia(matches: boolean): void {
    spyOn(window, 'matchMedia').and.callFake((q: string) =>
      ({ matches: q === EINK_MEDIA_QUERY ? matches : false, media: q, addEventListener() {}, removeEventListener() {} }) as unknown as MediaQueryList);
  }

  beforeEach(() => {
    savedChoice = localStorage.getItem(EINK_STORAGE_KEY);
    localStorage.removeItem(EINK_STORAGE_KEY);
    wasDark = document.documentElement.classList.contains('dark-theme');
  });
  afterEach(() => {
    if (savedChoice === null) localStorage.removeItem(EINK_STORAGE_KEY); else localStorage.setItem(EINK_STORAGE_KEY, savedChoice);
    setEinkActive(false);
    document.documentElement.classList.toggle('dark-theme', wasDark);
    TestBed.resetTestingModule();
  });

  it('gespeicherte Wahl hat Vorrang vor der Media-Query (\'1\' an, \'0\' aus)', () => {
    fakeMedia(true);
    localStorage.setItem(EINK_STORAGE_KEY, '0');
    expect(readEinkChoice()).toBeFalse();
    expect(resolveEink()).toBeFalse();
    localStorage.setItem(EINK_STORAGE_KEY, '1');
    expect(resolveEink()).toBeTrue();
  });

  it('ohne Wahl entscheidet (update: slow), (monochrome)', () => {
    fakeMedia(true);
    expect(readEinkChoice()).toBeNull();
    expect(resolveEink()).toBeTrue();
  });

  it('ohne Wahl und ohne passende Media-Query: aus', () => {
    fakeMedia(false);
    expect(resolveEink()).toBeFalse();
  });

  it('applyEinkEarly setzt die Klasse an <html> und <body> vor dem Start', () => {
    localStorage.setItem(EINK_STORAGE_KEY, '1');
    applyEinkEarly();
    expect(document.documentElement.classList.contains(EINK_CLASS)).toBeTrue();
    expect(document.body.classList.contains(EINK_CLASS)).toBeTrue();
    setEinkActive(false);
    expect(document.documentElement.classList.contains(EINK_CLASS)).toBeFalse();
    expect(document.body.classList.contains(EINK_CLASS)).toBeFalse();
  });

  it('Dienst: set() schaltet sofort, speichert je Gerät und spiegelt das Signal', () => {
    fakeMedia(false);
    const svc = TestBed.inject(EinkService);
    expect(svc.enabled()).toBeFalse();
    expect(svc.set(true)).toBeTrue();
    expect(svc.enabled()).toBeTrue();
    expect(localStorage.getItem(EINK_STORAGE_KEY)).toBe('1');
    expect(document.documentElement.classList.contains(EINK_CLASS)).toBeTrue();
    expect(svc.hasChoice()).toBeTrue();
    svc.set(false);
    expect(localStorage.getItem(EINK_STORAGE_KEY)).toBe('0');
    expect(svc.enabled()).toBeFalse();
    expect(document.documentElement.classList.contains(EINK_CLASS)).toBeFalse();
  });

  it('Dienst übernimmt den beim Start (main.ts) gesetzten Zustand aus der gespeicherten Wahl', () => {
    localStorage.setItem(EINK_STORAGE_KEY, '1');
    applyEinkEarly();
    const svc = TestBed.inject(EinkService);
    expect(svc.enabled()).toBeTrue();
    expect(isEinkActive()).toBeTrue();
  });

  it('erzwingt hell: Dunkelmodus-Wahl wird ignoriert, solange der Modus an ist, und gilt danach wieder', () => {
    const theme = new ThemeService();
    theme.setPreference('dark');
    expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
    setEinkActive(true);
    expect(theme.isDark).toBeFalse();
    expect(document.documentElement.classList.contains('dark-theme')).toBeFalse();
    expect(theme.preference).toBe('dark');
    setEinkActive(false);
    expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
  });

  it('Brett-Animation: an mit Dauer, im Modus aus', () => {
    expect(boardAnimation(200)).toEqual({ enabled: true, duration: 200 });
    setEinkActive(true);
    expect(boardAnimation(200)).toEqual({ enabled: false, duration: 0 });
  });

  it('Material: Ripples und Animationen folgen dem Modus zur Laufzeit', () => {
    TestBed.configureTestingModule({ providers: provideEinkMaterial() });
    const ripple = TestBed.inject(MAT_RIPPLE_GLOBAL_OPTIONS);
    const anim = TestBed.inject(MATERIAL_ANIMATIONS);
    expect(ripple.disabled).toBeFalse();
    expect(anim.animationsDisabled).toBeFalse();
    setEinkActive(true);
    expect(ripple.disabled).toBeTrue();
    expect(anim.animationsDisabled).toBeTrue();
  });

  it('Blättern statt Gleiten: scrollTo/scrollIntoView mit behavior smooth wird im Modus zu auto', () => {
    applyEinkEarly();   // hängt den Wächter ein (idempotent)
    const box = document.createElement('div');
    box.style.cssText = 'height:100px;overflow:auto';
    box.innerHTML = '<div style="height:1000px"></div>';
    document.body.appendChild(box);
    try {
      setEinkActive(true);
      box.scrollTo({ top: 500, behavior: 'smooth' });
      expect(box.scrollTop).withContext('sofort, ohne Gleiten').toBe(500);
      box.scrollBy({ top: -200, behavior: 'smooth' });
      expect(box.scrollTop).toBe(300);
    } finally { box.remove(); }
  });
});

describe('E-Ink: Nachbild-Löscher beim Brettwechsel', () => {
  afterEach(() => { setEinkActive(false); document.querySelectorAll('.eink-flash').forEach(e => e.remove()); });
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  it('ein Zug (auch Rochade) ist kein Brettwechsel, eine andere Stellung schon', () => {
    expect(isBoardSwitch(START, 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1')).toBeFalse();
    expect(isBoardSwitch('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1', 'r3k2r/8/8/8/8/8/8/2KR3R b kq - 1 1')).toBeFalse();
    expect(isBoardSwitch(START, '8/8/4k3/8/8/4K3/4P3/8 w - - 0 1')).toBeTrue();
    expect(isBoardSwitch(undefined, START)).toBeFalse();
  });

  it('blitzt nur im E-Ink-Modus: erst schwarz, dann weiß, dann weg — ohne Mausereignisse', fakeAsync(() => {
    const el = document.createElement('div');
    el.style.cssText = 'width:100px;height:100px';
    document.body.appendChild(el);
    try {
      flashEinkRefresh(el);
      expect(document.querySelector('.eink-flash')).toBeNull();
      setEinkActive(true);
      flashEinkRefresh(el);
      const veil = document.querySelector<HTMLElement>('.eink-flash')!;
      expect(veil.style.background).toContain('rgb(0, 0, 0)');
      expect(veil.style.pointerEvents).toBe('none');
      tick(EINK_FLASH_PHASE_MS);
      expect(veil.style.background).toContain('rgb(255, 255, 255)');
      tick(EINK_FLASH_PHASE_MS);
      expect(document.querySelector('.eink-flash')).toBeNull();
    } finally { el.remove(); }
  }));
});
