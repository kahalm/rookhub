import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { firstValueFrom, timeout } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { SHARE_REDIRECT_TIMEOUT_MS } from '@rh/core/share-redirect.guard';

/** Nur dieses Ziel nimmt der Sprung an — kein offener Umleiter, falls die Antwort einmal etwas anderes trüge. */
const CLUB_GAME_PATH = /^\/club-games\/\d+$/;

/**
 * LeagueHub `/partie/:token` → RookHub `/club-games/{id}` (Wunsch 2026-11-22, Gegenstück zu RookHubs `shareRedirectGuard`): ist man
 * ANGEMELDET und sieht die Vereinspartie hinter der versiegelten Marke auf dem normalen Weg (`league.view` + Verein der Partie, Admin
 * jeder — der Server entscheidet, `GET /api/share-links/target?kind=lp`), springt LeagueHub zur vollen Partieseite in RookHub. Das ist
 * eine andere App auf einem anderen Host: der Sprung läuft wie jeder Weg nach RookHub über `HandoffService.jumpToRookHub` (Einmal-Code,
 * drüben angemeldet; Basis-URL je Umgebung aus `partner-site.ts`). Query und Fragment gehen mit, nur der Einmal-Code `h` nicht.
 *
 * Gäste fragen nicht; ohne Zugriff, bei Fehler, Zeitüberschreitung oder ohne bekanntes RookHub (localhost) bleibt alles wie bisher.
 * Die Begegnung `/s/:token` hat bewusst keinen solchen Sprung. Link-Vorschau und Bots berührt das nicht (der nginx bedient sie).
 */
export const partieRedirectGuard: CanActivateFn = async route => {
  if (!inject(AuthService).isLoggedIn) return true;
  const handoff = inject(HandoffService);
  if (!handoff.rookHubUrl) return true;
  const token = route.paramMap.get('token');
  if (!token) return true;
  const http = inject(HttpClient);
  try {
    const params = new HttpParams().set('kind', 'lp').set('token', token);
    const res = await firstValueFrom(http.get<{ path?: string }>('/api/share-links/target', { params }).pipe(timeout(SHARE_REDIRECT_TIMEOUT_MS)));
    const path = res?.path ?? '';
    if (!CLUB_GAME_PATH.test(path)) return true;
    const query = new URLSearchParams();
    for (const [k, v] of Object.entries(route.queryParams ?? {})) {
      if (k === HandoffService.Param) continue;
      for (const one of Array.isArray(v) ? v : [v]) query.append(k, String(one));
    }
    const q = query.toString();
    await handoff.jumpToRookHub(`${path.slice(1)}${q ? `?${q}` : ''}${route.fragment ? `#${route.fragment}` : ''}`);
    return false;
  } catch {
    return true;
  }
};
