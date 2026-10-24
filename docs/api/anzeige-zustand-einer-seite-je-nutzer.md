# Anzeige-Zustand einer Seite je NUTZER (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Anzeige-Zustand einer Seite je NUTZER (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Die Filterleiste des Turnierkalenders lag nur im `localStorage` und war damit an ein GERAET
gebunden — der Umkreis vom Schreibtisch war am Handy weg, obwohl es die Einstellung eines Nutzers
ist. `UserViewStates` haelt sie serverseitig.

**Der Inhalt ist fuer den Server OPAK** (geprueft werden nur JSON-Gueltigkeit, Objekt-Form und
`ViewStateService.MaxJsonLength` = 8192): er wird nirgends abgefragt oder gefiltert, und die
Filterleiste bekommt weiter Felder — jedes als Spalte zu fuehren waere eine Migration je Feld ohne
jeden Nutzen in SQL (dasselbe Vorgehen wie beim Analysebaum, `CalculationTree.TreeJson`). Die
Spalte ist `text`, nicht `varchar(8192)`: letzteres zaehlt in utf8mb4 mit 32 KB gegen das
64-KB-Zeilenlimit von MariaDB.

**`ViewKey` ist eine ERLAUBTE Kennung** (`ViewStateService.AllowedKeys`, heute nur
`turnier.directory`) — ohne diese Liste waere der Endpunkt ein Schluessel-Wert-Speicher je Nutzer
fuer beliebige Inhalte.

Frontend (`core/view-state.service.ts` + `TournamentDirectoryComponent`): beim Oeffnen gilt ZUERST
die lokale Kopie (ohne Abruf, damit die Vorgabefilter nicht aufblitzen), danach der Server-Stand —
weicht er ab, gewinnt ER (ihn schrieb das Geraet, an dem zuletzt gefiltert wurde) und NUR dann wird
neu geladen. Hat der Server nichts, wird der lokale Zustand hinaufgeschoben — nur bei 204; scheitert der Abruf, geht
erst die erste Aenderung nach dem fertigen ersten Aufbau hinauf (danach wie im Normalfall), vor der Antwort gar
nichts (`DirectoryViewSync`, Codereview F6-015). Geschrieben wird
gedrosselt (`PersistDebounceMs` 1200), weil `storeView()` bei jeder Aenderung der Leiste laeuft.
Fehler sind still: der Zustand ist eine Bequemlichkeit, kein Inhalt.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/view-state/{key}` | Gespeicherter Zustand; **204**, wenn es keinen gibt (Normalfall beim ersten Aufruf, kein Fehler). 404 bei unbekannter Kennung |
| PUT | `/api/view-state/{key}` | Zustand speichern — der Rumpf IST der Zustand (ein JSON-Objekt), nicht ein DTO mit einem Feld darin |
| DELETE | `/api/view-state/{key}` | Zustand verwerfen (idempotent) |
