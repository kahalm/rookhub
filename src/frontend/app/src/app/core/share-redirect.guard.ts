import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of, switchMap, timeout } from 'rxjs';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

/** Art des Teilen-Links, wie sie `GET /api/share-links/target?kind=` erwartet. */
export type ShareLinkKind = 'g' | 'ga' | 'cg' | 's' | 'tl' | 'r';

/** Nur diese Ziele nimmt der Guard an (kein offener Umleiter, falls die Antwort einmal etwas anderes trüge) — mit dem Menüpunkt, den
 *  die Zielroute per `menuGuard` verlangt (`/club-games` hat keinen). */
const TARGETS: { re: RegExp; menu: string | null }[] = [
  { re: /^\/games\/\d+$/, menu: 'games' },
  { re: /^\/club-games\/\d+$/, menu: null },
  { re: /^\/training-videos\/\d+$/, menu: 'training-videos' },
  { re: /^\/reconstruct\/\d+$/, menu: 'reconstruct' },
];

/** So lange wartet die Teilen-Seite höchstens auf die Antwort, danach zeigt sie sich wie bisher. */
export const SHARE_REDIRECT_TIMEOUT_MS = 4000;

/**
 * „Teilen-Link → volle Seite“ (Wunsch 2026-11-22): öffnet ein ANGEMELDETER einen Teilen-Link zu etwas, das er auch auf dem normalen
 * Weg sehen darf (eigene Partie, Vereinspartie seines Vereins mit `league.view`, eigene Trainerstunde, eigene Rekonstruktion), landet
 * er auf der normalen Seite mit allen Funktionen — der Server entscheidet (`ShareLinkTargets`), mit denselben Prüfungen wie dort.
 * Unterseite (`suffix`, z. B. `summary`/`drill`), Query (`?train=white`, `?ply=`, `?roast=` …) und Fragment bleiben erhalten; die
 * Adresse wird ERSETZT (`replaceUrl`), „Zurück“ führt nicht wieder auf den Link.
 *
 * Als Guard statt in der Komponente: die Teilen-Ansicht rendert gar nicht erst, also kein Flackern. Gäste fragen nicht (kein Request,
 * alles wie bisher); kein Zugriff, Fehler, Zeitüberschreitung oder ein ausgeblendeter Menüpunkt des Ziels → die Teilen-Ansicht wie bisher.
 * Link-Vorschau (OG) und Bots berührt das nicht — die bedient der nginx vor der App.
 */
export function shareRedirectGuard(kind: ShareLinkKind, suffix: string[] = []): CanActivateFn {
  return route => {
    const auth = inject(AuthService);
    if (!auth.isLoggedIn) return true;
    const http = inject(HttpClient);
    const router = inject(Router);
    const menu = inject(MenuService);
    let params = new HttpParams().set('kind', kind);
    const token = route.paramMap.get('token');
    if (token) params = params.set('token', token);
    const id = route.paramMap.get('id');
    if (kind === 's' && id) params = params.set('id', id);
    return http.get<{ path?: string }>('/api/share-links/target', { params }).pipe(
      timeout(SHARE_REDIRECT_TIMEOUT_MS),
      switchMap(r => {
        const path = r?.path ?? '';
        const target = TARGETS.find(t => t.re.test(path));
        if (!target) return of(null);
        return target.menu ? menu.check(target.menu).pipe(map(ok => (ok ? path : null))) : of(path);
      }),
      map(path => path
        ? new RedirectCommand(router.createUrlTree([path, ...suffix], {
            queryParams: route.queryParams, fragment: route.fragment ?? undefined }), { replaceUrl: true })
        : true),
      catchError(() => of(true)),
    );
  };
}
