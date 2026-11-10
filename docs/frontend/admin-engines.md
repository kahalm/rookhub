# Admin-Reiter „Engines“ (0.946.0)

`features/admin/tabs/admin-engines.component.{ts,html,scss}`, Reiter-Schlüssel `engines` (`admin-tabs.ts`, Recht `null` =
nur Admins), Deep-Link `/admin?tab=engines`. API und Herkunft der Zahlen: `docs/api/admin-engines.md`.

- **Aufbau** (von oben): Kopfkarte mit Zustands-Pillen (rechnet / live / Leerlauf / offline / unbekannt, je mit Symbol —
  nie nur Farbe), Stand der Hintergrundjobs-Pause mit Link auf `/admin?tab=jobs` (dort wird umgelegt, hier nur gezeigt),
  „Stand hh:mm:ss“; Karte „Warteschlange“ (Summen + Tabelle je Auftragsart); Karte „Durchsatz“ (Tabelle 1 h / 24 h / 7 T:
  fertig, Ø je Auftrag, kN/s, gescheitert, kein Provider, Zeitüberschreitung, Fehler, unterbrochen); darunter eine Karte
  je Engine im Raster (`minmax(min(100%, 420px), 1fr)`): Name + Quelle/Haus-Engine/„nur auf Anforderung“, Zustand,
  Meta-Zeile (`.rh-dots`), Hintergrund-Zuordnung, laufender Auftrag (Art, für wen, Partie + Halbzug, Tiefe/Ziel bzw.
  Knoten/Ziel, Linien, Tempo, Zeit), Wartende, Durchsatz kompakt, Broker-Zähler seit Start (nur wenn > 0).
- **Tabellen** bleiben auch am Handy Tabellen: `min-width` + eigener Scroll-Kasten (`.table-scroll`, DESIGN 5.3) — die
  Seite selbst läuft bei 390 px nicht über (Spec).
- **Auffrischen**: `setInterval` 15 s (`ENGINE_OVERVIEW_POLL_MS`), übersprungen bei `document.visibilityState === 'hidden'`;
  `visibilitychange` → sofort, wenn der letzte Abruf ≥ 15 s her ist; aufgeräumt über `DestroyRef`. Erstes Laden
  gescheitert → `app-load-error`; späteres Scheitern → alter Stand bleibt, Hinweis „Auffrischen gescheitert“.
- **Offline-Engines** standardmäßig eingeklappt (wie die Profil-Engine-Karte), Knopf „N offline einblenden“ (`aria-pressed`).
- Zahlen über `formatCount`/`formatKiloNps` (`engine-lines.util.ts`, `LOCALE_ID`), Zeiten `formatElapsed`, Datum per
  `DatePipe` `short`/`mediumTime`. Texte `admin.engines.*` in allen 25 Sprachdateien.
