import { ErrorHandler } from '@angular/core';
import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import {
  CHUNK_RELOAD_KEY, CHUNK_RELOAD_SW_TIMEOUT_MS, CHUNK_RELOAD_WINDOW_MS, ChunkReloadService, PAGE_RELOADER,
  chunkNameOf, isChunkLoadError, provideChunkReload,
} from './chunk-reload';

const FIREFOX = new TypeError('error loading dynamically imported module: https://leaguehub.oberschmid.homes/chunk-CUjRmzKB2.js');
const CHROME = new TypeError('Failed to fetch dynamically imported module: https://rookhub.example/chunk-ZZ99XX11.js');
const SAFARI = new TypeError('Importing a module script failed.');

/** Alter Tab nach einem Deploy: fehlendes Lazy-Bündel → einmal neu laden, nie in einer Schleife. */
describe('chunk-reload', () => {
  describe('Erkennung', () => {
    it('erkennt jede Browser-Meldung', () => {
      expect(isChunkLoadError(FIREFOX)).toBeTrue();
      expect(isChunkLoadError(CHROME)).toBeTrue();
      expect(isChunkLoadError(SAFARI)).toBeTrue();
      const webpack = new Error('Loading chunk 123 failed.');
      webpack.name = 'ChunkLoadError';
      expect(isChunkLoadError(webpack)).toBeTrue();
      expect(isChunkLoadError({ name: 'ChunkLoadError', message: 'x' })).toBeTrue();
    });

    it('erkennt den Fehler auch in Hüllen (Zone-Ablehnung, Angular-Ursprung, cause)', () => {
      expect(isChunkLoadError({ message: 'Uncaught (in promise): TypeError', rejection: FIREFOX })).toBeTrue();
      expect(isChunkLoadError({ ngOriginalError: CHROME })).toBeTrue();
      expect(isChunkLoadError(new Error('Navigation fehlgeschlagen', { cause: SAFARI }))).toBeTrue();
    });

    it('lässt andere Fehler durch', () => {
      expect(isChunkLoadError(new TypeError("Cannot read properties of undefined (reading 'id')"))).toBeFalse();
      expect(isChunkLoadError(new Error('Http failure response for /api/x: 500'))).toBeFalse();
      expect(isChunkLoadError({ status: 404, message: 'Not Found' })).toBeFalse();
      expect(isChunkLoadError('boom')).toBeFalse();
      expect(isChunkLoadError(null)).toBeFalse();
      expect(isChunkLoadError(undefined)).toBeFalse();
    });

    it('liest den Namen des Bündels aus der Meldung', () => {
      expect(chunkNameOf(FIREFOX)).toBe('chunk-CUjRmzKB2.js');
      expect(chunkNameOf({ rejection: CHROME })).toBe('chunk-ZZ99XX11.js');
      expect(chunkNameOf(SAFARI)).toBe('unbekannt');
    });
  });

  describe('Neuladen', () => {
    let reload: jasmine.Spy;
    let check: jasmine.Spy;
    let activate: jasmine.Spy;

    function setup(sw: 'on' | 'off' | 'missing' = 'missing'): ChunkReloadService {
      TestBed.resetTestingModule();
      reload = jasmine.createSpy('reload');
      check = jasmine.createSpy('checkForUpdate').and.resolveTo(true);
      activate = jasmine.createSpy('activateUpdate').and.resolveTo(true);
      TestBed.configureTestingModule({
        providers: [
          { provide: PAGE_RELOADER, useValue: reload },
          ...(sw === 'missing' ? [] : [{
            provide: SwUpdate, useValue: { isEnabled: sw === 'on', checkForUpdate: check, activateUpdate: activate },
          }]),
        ],
      });
      return TestBed.inject(ChunkReloadService);
    }

    beforeEach(() => sessionStorage.removeItem(CHUNK_RELOAD_KEY));
    afterEach(() => sessionStorage.removeItem(CHUNK_RELOAD_KEY));

    it('ohne Service Worker: sofort genau einmal neu laden, Sperre mit Bündel und Zeit', fakeAsync(() => {
      const service = setup();
      expect(service.handle(FIREFOX)).toBeTrue();
      flushMicrotasks();
      expect(reload).toHaveBeenCalledTimes(1);
      const marks = JSON.parse(sessionStorage.getItem(CHUNK_RELOAD_KEY)!);
      expect(typeof marks['chunk-CUjRmzKB2.js']).toBe('number');
    }));

    it('anderer Fehler: nichts passiert', fakeAsync(() => {
      const service = setup();
      expect(service.handle(new Error('anders'))).toBeFalse();
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
      expect(sessionStorage.getItem(CHUNK_RELOAD_KEY)).toBeNull();
    }));

    it('derselbe Fehler zweimal im selben Tab (Router + Ablehnung): nur ein Neuladen', fakeAsync(() => {
      const service = setup();
      expect(service.handle(FIREFOX)).toBeTrue();
      expect(service.handle({ rejection: FIREFOX })).toBeTrue();
      flushMicrotasks();
      expect(reload).toHaveBeenCalledTimes(1);
    }));

    it('Schleifenschutz: dasselbe Bündel nach dem Neuladen innerhalb von 60 s → kein zweites Neuladen', fakeAsync(() => {
      setup().handle(FIREFOX);
      flushMicrotasks();
      tick(CHUNK_RELOAD_WINDOW_MS - 1_000);
      const afterReload = setup();   // neue Seite, frischer Dienst — nur die Sperre im sessionStorage bleibt
      expect(afterReload.handle(FIREFOX)).toBeFalse();
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
    }));

    it('nach Ablauf der 60 s darf dasselbe Bündel wieder neu laden', fakeAsync(() => {
      setup().handle(FIREFOX);
      flushMicrotasks();
      tick(CHUNK_RELOAD_WINDOW_MS + 1);
      const later = setup();
      expect(later.handle(FIREFOX)).toBeTrue();
      flushMicrotasks();
      expect(reload).toHaveBeenCalledTimes(1);
    }));

    it('ein anderes Bündel hat seinen eigenen Versuch', fakeAsync(() => {
      setup().handle(FIREFOX);
      flushMicrotasks();
      const next = setup();
      expect(next.handle(CHROME)).toBeTrue();
      flushMicrotasks();
      expect(reload).toHaveBeenCalledTimes(1);
    }));

    it('Speicher nicht nutzbar: kein Neuladen (lieber Fehler als Schleife)', fakeAsync(() => {
      const service = setup();
      spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
      expect(service.handle(FIREFOX)).toBeFalse();
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
    }));

    it('mit Service Worker: erst neue Fassung aktivieren, dann neu laden', fakeAsync(() => {
      const service = setup('on');
      service.handle(CHROME);
      flushMicrotasks();
      expect(check).toHaveBeenCalledTimes(1);
      expect(activate).toHaveBeenCalledTimes(1);
      expect(reload).toHaveBeenCalledTimes(1);
      expect(activate).toHaveBeenCalledBefore(reload);
    }));

    it('Service Worker hängt: nach höchstens 2 s trotzdem neu laden', fakeAsync(() => {
      const service = setup('on');
      check.and.returnValue(new Promise(() => { /* hängt */ }));
      service.handle(CHROME);
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
      tick(CHUNK_RELOAD_SW_TIMEOUT_MS);
      expect(reload).toHaveBeenCalledTimes(1);
    }));

    it('Service Worker scheitert: trotzdem neu laden', fakeAsync(() => {
      const service = setup('on');
      check.and.rejectWith(new Error('offline'));
      service.handle(CHROME);
      flushMicrotasks();
      tick(CHUNK_RELOAD_SW_TIMEOUT_MS);
      expect(reload).toHaveBeenCalledTimes(1);
    }));

    it('Service Worker aus (Dev-Build): direkt neu laden, ohne ihn zu fragen', fakeAsync(() => {
      const service = setup('off');
      service.handle(SAFARI);
      flushMicrotasks();
      expect(check).not.toHaveBeenCalled();
      expect(reload).toHaveBeenCalledTimes(1);
    }));
  });

  describe('provideChunkReload', () => {
    let reload: jasmine.Spy;
    let consoleError: jasmine.Spy;
    let failingImport: () => Promise<never>;

    beforeEach(() => {
      sessionStorage.removeItem(CHUNK_RELOAD_KEY);
      reload = jasmine.createSpy('reload');
      consoleError = spyOn(console, 'error');
      failingImport = () => Promise.reject(FIREFOX);
      TestBed.configureTestingModule({
        providers: [
          provideRouter([{ path: 'lazy', loadComponent: () => failingImport() }]),
          provideChunkReload(),
          { provide: PAGE_RELOADER, useValue: reload },
        ],
      });
    });
    afterEach(() => sessionStorage.removeItem(CHUNK_RELOAD_KEY));

    it('ErrorHandler: Chunk-Fehler → Neuladen statt Log', fakeAsync(() => {
      TestBed.inject(ErrorHandler).handleError(FIREFOX);
      flushMicrotasks();
      expect(reload).toHaveBeenCalledTimes(1);
      expect(consoleError).not.toHaveBeenCalled();
    }));

    it('ErrorHandler: andere Fehler wie bisher ins Log, kein Neuladen', fakeAsync(() => {
      const other = new Error('anders');
      TestBed.inject(ErrorHandler).handleError(other);
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
      expect(consoleError).toHaveBeenCalled();
    }));

    it('ErrorHandler: Sperre aktiv → normale Fehleranzeige im Log', fakeAsync(() => {
      sessionStorage.setItem(CHUNK_RELOAD_KEY, JSON.stringify({ 'chunk-CUjRmzKB2.js': Date.now() }));
      TestBed.inject(ErrorHandler).handleError(FIREFOX);
      flushMicrotasks();
      expect(reload).not.toHaveBeenCalled();
      expect(consoleError).toHaveBeenCalled();
    }));

    it('Router: scheiterndes loadComponent (NavigationError) → einmal neu laden', fakeAsync(() => {
      const router = TestBed.inject(Router);
      // Ablehnung bewusst geschluckt: so zählt allein der Beobachter auf NavigationError (wie bei router.navigate im Code).
      router.navigateByUrl('/lazy').catch(() => undefined);
      flushMicrotasks();
      tick();
      expect(reload).toHaveBeenCalledTimes(1);
      expect(consoleError).not.toHaveBeenCalled();
    }));
  });
});
