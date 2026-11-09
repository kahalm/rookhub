import {
  EnvironmentProviders, ErrorHandler, Injectable, InjectionToken, inject, makeEnvironmentProviders, provideAppInitializer,
} from '@angular/core';
import { NavigationError, Router } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';

/**
 * Sperre im `sessionStorage`: je fehlendem Bündel der Zeitpunkt (ms) des letzten Neuladens, als JSON-Objekt.
 * Der Eintrag im Register steht in `local-storage-keys.ts`.
 */
export const CHUNK_RELOAD_KEY = 'rookhub_chunk_reload';

/** Innerhalb dieser Spanne wird für dasselbe Bündel nicht ein zweites Mal neu geladen. */
export const CHUNK_RELOAD_WINDOW_MS = 60_000;

/** So lange darf der Service Worker die neue Fassung suchen und aktivieren, dann wird trotzdem neu geladen. */
export const CHUNK_RELOAD_SW_TIMEOUT_MS = 2_000;

/** Lädt die Seite hart neu. Eigenes Token, damit Tests den Aufruf abfangen statt die Testseite neu zu laden. */
export const PAGE_RELOADER = new InjectionToken<() => void>('PAGE_RELOADER', {
  providedIn: 'root',
  factory: () => () => document.location.reload(),
});

/** Meldungen der Browser, wenn ein per `import()` nachgeladenes Bündel fehlt oder kein JavaScript ist. */
const CHUNK_ERROR_PATTERNS: readonly RegExp[] = [
  /error loading dynamically imported module/i,   // Firefox
  /failed to fetch dynamically imported module/i, // Chrome/Edge
  /importing a module script failed/i,            // Safari
  /ChunkLoadError/,                               // webpack-Benennung (ältere Builds, Fremdbibliotheken)
  /loading chunk [\w-]+ failed/i,                 // webpack-Meldungstext
];

/**
 * Packt Hüllen aus: Zone meldet abgelehnte Promises als „Uncaught (in promise)“ mit `rejection`, Angular hängt den
 * Ursprung als `ngOriginalError`/`originalError` an, neuere Fehler tragen ihn als `cause`.
 */
function unwrap(error: unknown): unknown[] {
  const chain: unknown[] = [];
  let current: unknown = error;
  while (current != null && chain.length < 5 && !chain.includes(current)) {
    chain.push(current);
    const c = current as { rejection?: unknown; ngOriginalError?: unknown; originalError?: unknown; cause?: unknown };
    current = typeof current === 'object' ? (c.rejection ?? c.ngOriginalError ?? c.originalError ?? c.cause) : undefined;
  }
  return chain;
}

function messageOf(error: unknown): string {
  if (error instanceof Error) return `${error.name}: ${error.message}`;
  if (typeof error === 'string') return error;
  const e = error as { name?: unknown; message?: unknown } | null;
  return e && typeof e === 'object' ? `${String(e.name ?? '')}: ${String(e.message ?? '')}` : String(error);
}

/** Ist das ein Fehler beim Nachladen eines Bündels (alter Tab nach einem Deploy)? */
export function isChunkLoadError(error: unknown): boolean {
  return unwrap(error).some(e => {
    const text = messageOf(e);
    return CHUNK_ERROR_PATTERNS.some(p => p.test(text));
  });
}

/**
 * Name des fehlenden Bündels aus der Meldung (Firefox und Chrome nennen die Adresse, Safari nicht); sonst
 * `unbekannt` — die Sperre gilt dann für alle namenlosen Fehler gemeinsam.
 */
export function chunkNameOf(error: unknown): string {
  for (const e of unwrap(error)) {
    const m = messageOf(e).match(/([\w.-]+\.m?js)\b/);
    if (m) return m[1];
  }
  return 'unbekannt';
}

/**
 * Alter Tab nach einem Deploy: Das geladene Hauptbündel verweist auf Lazy-Chunks mit Hashes, die es auf dem Server
 * nicht mehr gibt (nginx antwortet darauf seit 2026-11-09 mit 404). Statt einer kaputten Seite wird EINMAL neu geladen
 * — dann kommt die neue index.html mit den neuen Namen.
 *
 * <p><b>Keine Schleife:</b> Die Sperre steht vor dem Neuladen, je Bündel mit Zeitstempel; scheitert dasselbe Bündel
 * innerhalb von {@link CHUNK_RELOAD_WINDOW_MS} erneut, bleibt es beim normalen Fehler im Log. Lässt sich der Speicher
 * nicht lesen oder schreiben, wird gar nicht neu geladen (wie `StaleRouteReloadService` und `AppUpdateService`).</p>
 *
 * <p><b>Service Worker</b> (RookHub, Turnierseite, KidHub): Er kann die alte Fassung weiter ausliefern — vor dem
 * Neuladen wird er höchstens {@link CHUNK_RELOAD_SW_TIMEOUT_MS} lang auf die neue umgestellt (`activateUpdate`
 * entscheidet, siehe `StaleRouteReloadService`). LeagueHub und ClubHub haben keinen, dort sofort neu laden.</p>
 */
@Injectable({ providedIn: 'root' })
export class ChunkReloadService {
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly reloadPage = inject(PAGE_RELOADER);
  private reloading = false;

  /**
   * @returns `true`, wenn der Fehler ein Chunk-Ladefehler ist und neu geladen wird (oder schon wird) — der Aufrufer
   *          meldet ihn dann nicht weiter. `false`: anderer Fehler oder Sperre aktiv, normal behandeln.
   */
  handle(error: unknown): boolean {
    if (!isChunkLoadError(error)) return false;
    // Router-Fehler und unbehandelte Ablehnung desselben import() kommen oft beide an: nur einmal handeln.
    if (this.reloading) return true;
    if (!this.lock(chunkNameOf(error))) return false;
    this.reloading = true;
    void this.prepareServiceWorker().finally(() => this.reloadPage());
    return true;
  }

  private async prepareServiceWorker(): Promise<void> {
    const sw = this.swUpdate;
    if (!sw?.isEnabled) return;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<void>(resolve => { timer = setTimeout(resolve, CHUNK_RELOAD_SW_TIMEOUT_MS); });
    const update = (async () => {
      try {
        await sw.checkForUpdate();
        await sw.activateUpdate();
      } catch { /* neu geladen wird trotzdem */ }
    })();
    await Promise.race([update, timeout]).finally(() => clearTimeout(timer));
  }

  /** Setzt die Sperre für dieses Bündel; `false`, wenn sie noch gilt oder der Speicher nicht nutzbar ist. */
  private lock(chunk: string): boolean {
    const now = Date.now();
    let marks: Record<string, number>;
    try {
      const parsed: unknown = JSON.parse(sessionStorage.getItem(CHUNK_RELOAD_KEY) ?? '{}');
      marks = parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed as Record<string, number> : {};
    } catch {
      return false;
    }
    const last = marks[chunk];
    if (typeof last === 'number' && now - last >= 0 && now - last < CHUNK_RELOAD_WINDOW_MS) return false;
    // Abgelaufene Marken aufräumen, damit der Eintrag nicht wächst.
    const fresh: Record<string, number> = {};
    for (const [k, t] of Object.entries(marks)) {
      if (typeof t === 'number' && now - t >= 0 && now - t < CHUNK_RELOAD_WINDOW_MS) fresh[k] = t;
    }
    fresh[chunk] = now;
    try {
      sessionStorage.setItem(CHUNK_RELOAD_KEY, JSON.stringify(fresh));
      return true;
    } catch {
      return false;
    }
  }
}

/** Fängt Chunk-Ladefehler ab (siehe {@link ChunkReloadService}); alles andere an den Standard-Handler. */
@Injectable()
export class ChunkReloadErrorHandler extends ErrorHandler {
  private readonly chunkReload = inject(ChunkReloadService);

  override handleError(error: unknown): void {
    if (this.chunkReload.handle(error)) return;
    super.handleError(error);
  }
}

/**
 * Für jedes `app.config.ts`: globaler ErrorHandler plus Beobachter auf `NavigationError` (scheiterndes
 * `loadComponent`/`loadChildren` der Lazy-Routen).
 */
export function provideChunkReload(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: ErrorHandler, useClass: ChunkReloadErrorHandler },
    // Ein RouterLink reicht den Fehler zwar selbst an den ErrorHandler, ein `router.navigate()` im Code aber nur als
    // abgelehntes Promise — der Beobachter fängt beide Wege; doppelt kommt er nicht an (`reloading`).
    provideAppInitializer(() => {
      const router = inject(Router);
      const chunkReload = inject(ChunkReloadService);
      router.events.subscribe(e => {
        if (e instanceof NavigationError) chunkReload.handle(e.error);
      });
    }),
  ]);
}
