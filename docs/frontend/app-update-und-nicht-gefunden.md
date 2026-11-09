# Neue Fassung erkennen: `AppUpdateService` + „Seite nicht gefunden“

`core/app-update.service.ts` ist der geteilte Umgang mit dem Service Worker (RookHub, Turnierseite, KidHub):
Hinweis „Neue Version verfügbar“ mit „Neu laden“, Telemetrie bei gescheiterter Installation, Selbstheilung bei
`UNRECOVERABLE_STATE` und die aktive Prüfung beim Start, alle 15 Minuten (`CheckIntervalMs`) und bei Rückkehr des Tabs.
`SwUpdate` wird OPTIONAL injiziert — LeagueHub und ClubHub registrieren keinen Service Worker, nutzen aber die geteilte
Nicht-gefunden-Seite.

## Nicht-gefunden-Seite: erst einmal neu laden, dann Hinweis (0.940.0 + U1)

Eine alte, im Service Worker gehaltene Fassung kennt neue Routen nicht (Beispiele `/games/:id/summary` aus 0.913.0,
`/g/:token/summary` auf 0.932.1) und landet auf „Seite nicht gefunden“, obwohl es die Seite am Server gibt.
`shared/not-found/not-found.component.ts` (RookHub `'**'`, Kurz-URL-Auflöser, LeagueHub `'**'`) zeigt deshalb zuerst
den Ladekreis (`mat-spinner`, `aria-label` = `common.loading`) und geht zwei Stufen durch:

1. **Automatisch, einmal** (`core/stale-route-reload.service.ts`, 0.940.0): Nur mit aktivem Service Worker und solange
   die Adresse in diesem Tab noch nicht gesperrt ist (`canTry()`, Sperrliste im `sessionStorage` unter
   `rookhub_stale_route_reload`): `checkForUpdate()`, Sperre setzen, `activateUpdate()`; liefert das `true`, wird
   dieselbe Adresse neu geladen und die Seite bleibt beim Ladekreis. Sonst wird die Sperre wieder gelöst. Zeitgrenze
   10 s.
2. **Fallback-Hinweis** (U1) — nur wenn Stufe 1 NICHT neu lädt (kein Service Worker, Adresse schon einmal neu geladen,
   nichts zu aktivieren, Zeitgrenze, Fehler): `AppUpdateService.checkNow()`:
   - `SwUpdate.checkForUpdate()`, wenn ein Service Worker läuft;
   - immer zusätzlich `fetchServerVersion()` (`core/app-version-check.ts`): `GET /assets/version.json?ngsw-bypass=1`
     mit `cache: 'no-store'` (die von `scripts/changelog-json.mjs` neben dem Changelog erzeugte Datei `{ version }` —
     wenige Byte statt des 2-MB-Changelogs, Gegenlesung GF-R2; `ngsw-bypass`, damit nicht der SW den alten Stand
     liefert), Version gegen `APP_VERSION` per `isNewerVersion` (dreiteilig numerisch). Zeitlimit 5 s; jeder Fehler
     (offline, 404, SPA-Rückfall statt JSON) zählt als „nichts“.

   Neuere Fassung → „Neue Version verfügbar (x.y.z). Diese Seite gibt es vielleicht erst dort.“ (meldet nur der SW eine,
   ohne Klammer) mit dem Hauptknopf „Aktualisieren“ (`activateAndReload`: mit SW `activateUpdate()` und
   `location.replace(<dieselbe Adresse>)`, ohne SW ein einfaches `document.location.reload()`); sonst und bei Fehlern
   die normale Meldung. Während Stufe 2 läuft, steht weiter derselbe Ladekreis (kein eigener Text mehr, früher
   `app.notFound.checking`).

Ohne Service Worker (LeagueHub): `SwUpdate` ist in beiden Diensten optional injiziert, Stufe 1 entfällt (`canTry()` ist
`false`), Stufe 2 läuft. Spec `src-leaguehub/app/not-found-ohne-service-worker.spec.ts` erzeugt die Seite ohne
`SwUpdate`-Anbieter.

`version.json` wird in die Projekte `app` und `leaguehub` kopiert (angular.json; `changelog.json` nur nach `app`), Stufe 2
greift also auch in LeagueHub. Grenzen: Turnierseite, KidHub und ClubHub haben
keine Nicht-gefunden-Seite (`'**'` leitet auf die Startseite um).
