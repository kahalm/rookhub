// Single Source of Truth fuer App-Version + Changelog.
// Wird von BEIDEN Environment-Dateien importiert (environment.ts = dev,
// environment.prod.ts = prod-Build via fileReplacements). Dadurch zeigt der
// Footer in JEDEM Build dieselbe Version/Changelog — ein Bump aendert nur hier.
export const APP_VERSION = '0.152.6';

export interface ChangelogEntry {
  version: string;
  date: string;
  /** Zweisprachig: en (Default/Fallback) + de. Footer zeigt die aktive UI-Sprache. */
  changes: { en: string; de: string }[];
}

export const CHANGELOG: ChangelogEntry[] = [
  { version: "0.152.5", date: "2026-06-17", changes: [
    { en: "Reliability: starting a conversation with the admin team no longer fails if a user and an admin happen to send their very first message at the same moment — the shared message thread is now created race-safely.", de: "Zuverlässigkeit: Das Starten einer Konversation mit dem Admin-Team schlägt nicht mehr fehl, wenn ein User und ein Admin zufällig im selben Moment ihre allererste Nachricht senden — die gemeinsame Nachrichten-Thread-Zeile wird jetzt rennsicher angelegt." },
  ]},
  { version: "0.152.3", date: "2026-06-17", changes: [
    { en: "Reliability: when you message the admin team, all admins are now notified in a single atomic step — no more risk of only some admins getting the bell notification if one write fails midway.", de: "Zuverlässigkeit: Wenn du dem Admin-Team schreibst, werden jetzt alle Admins in einem atomaren Schritt benachrichtigt — kein Risiko mehr, dass nur ein Teil der Admins die Glocken-Benachrichtigung erhält, falls eine Speicherung mittendrin fehlschlägt." },
  ]},
  { version: "0.152.2", date: "2026-06-17", changes: [
    { en: "Endless mode: a finished run is no longer lost from your history if you leave the game-over screen before clicking \"Continue\" — the run is now saved as soon as it ends, with an extra safety net when you navigate away or close the tab.", de: "Endless-Modus: Ein beendeter Lauf verschwindet nicht mehr aus deiner History, wenn du den Game-Over-Screen verlässt, bevor du auf „Weiter\" klickst — der Lauf wird jetzt beim Verlassen der Seite bzw. beim Schließen des Tabs als Sicherheitsnetz nachträglich gespeichert." },
  ]},
  { version: "0.152.1", date: "2026-06-17", changes: [
    { en: "Analysis board: fixed an engine crash (\"RuntimeError: unreachable\") that could occur when clicking quickly through moves — the engine now waits for the current search to finish before starting the next position, instead of crashing the analysis.", de: "Analysebrett: Ein Engine-Absturz („RuntimeError: unreachable\") behoben, der beim schnellen Durchklicken von Zügen auftreten konnte — die Engine wartet jetzt das Ende der laufenden Suche ab, bevor sie die nächste Stellung startet, statt die Analyse abstürzen zu lassen." },
  ]},
  { version: "0.152.0", date: "2026-06-17", changes: [
    { en: "Analysis board: pawn promotion now lets you pick the piece (queen, rook, bishop or knight) instead of always auto-queening — using the same picker as the puzzle board, including the mobile ghost-tap protection.", de: "Analysebrett: Bei der Bauernumwandlung kannst du jetzt die Figur wählen (Dame, Turm, Läufer oder Springer) statt automatisch immer die Dame zu bekommen — mit demselben Auswahl-Dialog wie im Puzzle-Brett, inkl. Schutz gegen den versehentlichen Touch-Ghost-Tap." },
  ]},
  { version: "0.151.6", date: "2026-06-17", changes: [
    { en: "Pawn promotion on touch devices no longer auto-queens by accident: the move tap could fall through to the freshly shown promotion picker (which appears right under your finger) and instantly pick the queen. The picker now ignores that ghost tap, so you can choose the piece you actually want.", de: "Bauernumwandlung auf Touch-Geräten wandelt nicht mehr versehentlich automatisch in die Dame um: Der Zug-Tap fiel bisher auf den gerade erschienenen Umwandlungs-Dialog (der genau unter dem Finger auftaucht) durch und wählte sofort die Dame. Der Dialog ignoriert diesen Ghost-Tap jetzt, sodass man die gewünschte Figur wirklich auswählen kann." },
  ]},
  { version: "0.151.5", date: "2026-06-16", changes: [
    { en: "Tournament/player requests now return the accurate status when chess-results.com is the problem: timeout (504), unreachable (502) or crawler overloaded (503), instead of collapsing every case into one generic error.", de: "Turnier-/Spieler-Anfragen liefern jetzt den passenden Status, wenn chess-results.com das Problem ist: Timeout (504), nicht erreichbar (502) oder Crawler überlastet (503) — statt alle Fälle in einen generischen Fehler zu werfen." },
  ]},
  { version: "0.151.4", date: "2026-06-16", changes: [
    { en: "Chessable imports interrupted by a server restart/deploy are no longer wrongly marked as failed — they now resume automatically on the next start instead of getting stuck.", de: "Chessable-Importe, die durch einen Server-Neustart/Deploy unterbrochen werden, werden nicht mehr fälschlich als fehlgeschlagen markiert — sie werden beim nächsten Start automatisch fortgesetzt, statt hängen zu bleiben." },
  ]},
  { version: "0.151.3", date: "2026-06-16", changes: [
    { en: "When chess-results.com is slow or unreachable, tournament/player requests now return a clear gateway error (timeout/unavailable) instead of a generic server error.", de: "Wenn chess-results.com langsam oder nicht erreichbar ist, liefern Turnier-/Spieler-Anfragen jetzt einen klaren Gateway-Fehler (Timeout/nicht erreichbar) statt eines generischen Serverfehlers." },
  ]},
  { version: "0.151.2", date: "2026-06-16", changes: [
    { en: "Notification bell: the \"mark all as read\" action is now a compact icon button (✓✓) instead of a text label.", de: "Benachrichtigungs-Glocke: „Alle als gelesen\" ist jetzt ein kompakter Icon-Knopf (✓✓) statt eines Text-Labels." },
  ]},
  { version: "0.151.0", date: "2026-06-16", changes: [
    { en: "Leaderboards now show only the top 5 plus your own position (two above and two below) in each category — your row is highlighted, with a gap shown when you're below the top 5.", de: "Bestenlisten zeigen jetzt je Kategorie nur noch die besten 5 plus deinen eigenen Platz (zwei darüber und zwei darunter) — deine Zeile ist hervorgehoben, mit einer Lücke, wenn du unter den Top 5 stehst." },
  ]},
  { version: "0.149.2", date: "2026-06-16", changes: [
    { en: "Chessable connect help: the \"get your bearer token\" instructions now mention that you can right-click the \"authorization\" header and choose \"Copy value\" in DevTools.", de: "Chessable-Verbinden-Hilfe: Die Anleitung zum Holen des Bearer-Tokens weist jetzt darauf hin, dass man in den DevTools mit Rechtsklick auf den „authorization\"-Header „Copy value\" wählen kann." },
  ]},
  { version: "0.149.1", date: "2026-06-16", changes: [
    { en: "The mail icon in the navbar now turns red when you have unread messages (matching the notification bell).", de: "Das Mail-Symbol in der Navigationsleiste wird jetzt rot, wenn ungelesene Nachrichten vorliegen (analog zur Benachrichtigungs-Glocke)." },
  ]},
  { version: "0.149.0", date: "2026-06-16", changes: [
    { en: "Notification bell now shows only unread notifications. Once read (clicked or via \"Mark all read\"), a notification disappears from the bell and stays visible only on the \"View all\" page.", de: "Die Benachrichtigungs-Glocke zeigt jetzt nur noch ungelesene Benachrichtigungen. Sobald eine gelesen ist (angeklickt oder über „Alle als gelesen\"), verschwindet sie aus der Glocke und bleibt nur noch auf der Seite „Alle anzeigen\" sichtbar." },
  ]},
  { version: "0.148.2", date: "2026-06-16", changes: [
    { en: "Chessable import queue: the displayed queue position now reflects the fair processing order. When several users have courses waiting, imports are processed alternating between users (round-robin) — the shown positions now match that order instead of listing all of one user's courses first.", de: "Chessable-Import-Warteschlange: Die angezeigte Queue-Position spiegelt jetzt die faire Verarbeitungsreihenfolge wider. Wenn mehrere User Kurse wartend haben, werden Importe abwechselnd zwischen den Usern verarbeitet (Round-Robin) — die angezeigten Positionen entsprechen jetzt dieser Reihenfolge, statt zuerst alle Kurse eines Users aufzulisten." },
  ]},
  { version: "0.148.1", date: "2026-06-16", changes: [
    { en: "Notifications: clicking a notification in the bell now marks it as read (the badge updates immediately).", de: "Benachrichtigungen: Ein Klick auf eine Benachrichtigung in der Glocke markiert sie jetzt als gelesen (das Badge aktualisiert sich sofort)." },
    { en: "When a user writes to the admin team, the notification is now clearer (\"… sent the admin team a message\") and clicking it opens that conversation directly.", de: "Wenn ein Nutzer dem Admin-Team schreibt, ist die Benachrichtigung jetzt eindeutiger („… hat dem Admin-Team eine Nachricht geschickt\") und ein Klick öffnet direkt diese Konversation." },
  ]},
  { version: "0.148.0", date: "2026-06-16", changes: [
    { en: "Chessable courses: courses whose data is already cached on the server now show a ⚡ icon — these import almost instantly (no fresh Chessable fetch needed).", de: "Chessable-Kurse: Kurse, deren Daten bereits auf dem Server zwischengespeichert sind, zeigen jetzt ein ⚡-Symbol — diese werden quasi sofort importiert (kein erneuter Chessable-Abruf nötig)." },
  ]},
  { version: "0.147.0", date: "2026-06-16", changes: [
    { en: "New \"Leaderboards\" page (logged-in users): ranking lists for four categories — unique puzzles solved, daily puzzles solved, endless runs, and solved course lines — each for today, this week, this month, or all time.", de: "Neue Seite „Bestenlisten\" (für eingeloggte Nutzer): Ranglisten für vier Kategorien — einzigartige gelöste Puzzles, gelöste Tagespuzzles, Endlos-Läufe und gelöste Kurs-Linien — jeweils für heute, diese Woche, diesen Monat oder gesamt." },
  ]},
  { version: "0.144.0", date: "2026-06-16", changes: [
    { en: "Puzzles: If you finish a puzzle with a different winning line than the one intended (an \"alternative solution\"), a new \"Show original solution\" button now appears — it plays back the move sequence the puzzle was actually looking for. Available in all modes (Standard, Book/Course/Daily, Endless).", de: "Puzzles: Wenn du ein Puzzle mit einer anderen gewinnenden Zugfolge als der vorgesehenen löst (eine „alternative Lösung\"), erscheint jetzt ein neuer Knopf „Originale Lösung zeigen\" — er spielt die Zugfolge durch, die das Puzzle eigentlich sehen wollte. In allen Modi verfügbar (Standard, Buch/Kurs/Tagespuzzle, Endlos)." },
  ]},
  { version: "0.143.0", date: "2026-06-15", changes: [
    { en: "Messages now go both ways: you can contact the admin team yourself directly from the \"Messages\" page (mail icon, always available) — no need to wait for an admin to write first. All admins are notified of your message.", de: "Nachrichten gehen jetzt in beide Richtungen: Du kannst das Admin-Team direkt über die Seite „Nachrichten\" (Brief-Symbol, immer verfügbar) selbst kontaktieren — du musst nicht mehr warten, bis ein Admin zuerst schreibt. Alle Admins werden über deine Nachricht benachrichtigt." },
    { en: "Admin area: incoming conversations can be \"taken over\" — every admin sees all conversations and who is handling which one (replying to an unassigned conversation claims it automatically; it can also be claimed or released manually).", de: "Admin-Bereich: Eingehende Konversationen lassen sich „übernehmen“ — jeder Admin sieht alle Konversationen und wer welche bearbeitet (wer auf eine offene Konversation antwortet, übernimmt sie automatisch; übernehmen/freigeben geht auch manuell)." },
  ]},
  { version: "0.142.0", date: "2026-06-15", changes: [
    { en: "Direct messages: The admin team can now send a message to a user, and the user can reply right here — the conversation continues as a thread. A new \"Messages\" page (mail icon in the navbar, with an unread badge) shows your conversation; you get a bell notification for every new admin message.", de: "Direktnachrichten: Das Admin-Team kann einem User jetzt eine Nachricht schicken, und der User kann direkt hier antworten — die Konversation läuft als Thread weiter. Eine neue Seite „Nachrichten\" (Brief-Symbol in der Navigation, mit Ungelesen-Markierung) zeigt deine Konversation; bei jeder neuen Admin-Nachricht gibt es eine Glocken-Benachrichtigung." },
    { en: "Admin area: A new \"Messages\" tab lists all conversations (with an unread-replies badge), lets you open a thread, write to a user, and start a new conversation via user search.", de: "Admin-Bereich: Ein neuer Tab „Nachrichten\" listet alle Konversationen (mit Markierung ungelesener Antworten), öffnet einen Thread, schreibt einem User und startet per User-Suche eine neue Konversation." },
  ]},
  { version: "0.141.1", date: "2026-06-15", changes: [
    { en: "Build pipeline (CI): All GitHub Actions were updated to versions running on Node.js 24, removing the deprecation warning about Node.js 20 (which GitHub will stop supporting on the build runners). No effect on the app itself.", de: "Build-Pipeline (CI): Alle GitHub-Actions wurden auf Versionen aktualisiert, die unter Node.js 24 laufen — damit entfällt die Verfallswarnung zu Node.js 20 (das GitHub auf den Build-Runnern künftig nicht mehr unterstützt). Keine Auswirkung auf die App selbst." },
  ]},
  { version: "0.140.2", date: "2026-06-15", changes: [
    { en: "Analysis board more robust against engine crashes: If the local Stockfish crashes on a certain position, it is no longer reloaded several times in a row (which reloaded the ~7 MB engine each time and immediately crashed again on the same position) — the analysis now gives up cleanly on that position instead of \"thrashing\". In addition, no calculation is started at all for checkmate/stalemate positions.", de: "Analyse-Brett robuster gegen Engine-Abstürze: Stürzt der lokale Stockfish auf einer bestimmten Stellung ab, wird er nicht mehr mehrfach hintereinander neu geladen (das lud jedes Mal die ~7 MB große Engine neu und brachte dieselbe Stellung sofort wieder zum Absturz) — die Analyse gibt auf dieser Stellung jetzt sauber auf, statt zu „thrashen\". Außerdem wird auf Matt-/Patt-Stellungen gar keine Berechnung mehr gestartet." },
    { en: "The crash report now includes the affected position (FEN), so that such engine crashes can be reproduced and fixed in a targeted way.", de: "Die Absturzmeldung enthält jetzt die betroffene Stellung (FEN), damit solche Engine-Abstürze gezielt nachgestellt und behoben werden können." },
  ]},
  { version: "0.140.1", date: "2026-06-15", changes: [
    { en: "Internal monitoring: The \"Sign in as user\" audit log (admin impersonation) is now written at level \"Information\" instead of \"Warning\". It remains fully traceable in Kibana but no longer skews the warning rate (avoided a false alarm from the log watcher).", de: "Internes Monitoring: Das „Als Nutzer einsteigen\"-Audit-Log (Admin-Impersonation) wird jetzt auf Stufe „Information\" statt „Warnung\" geschrieben. Es bleibt vollständig in Kibana nachvollziehbar, verfälscht aber nicht mehr die Warn-Rate (vermied einen Fehlalarm des Log-Watchers)." },
  ]},
  { version: "0.140.0", date: "2026-06-15", changes: [
    { en: "Blindfold (Visualization Level 1): There is now a \"Show\" button here too. Since the board in Level 1 is permanently frozen on the starting position, the button reveals the actual current position for 3 seconds and then returns to the frozen starting position — in all modes (Standard, Book/Course/Daily puzzle, Endless).", de: "Blindspiel (Visualisierung Level 1): Es gibt jetzt auch hier den „Anzeigen\"-Button. Da das Brett im Level 1 dauerhaft auf der Startstellung eingefroren ist, blendet der Button für 3 Sekunden die tatsächliche aktuelle Stellung ein und kehrt danach wieder zur eingefrorenen Startstellung zurück — in allen Modi (Standard, Buch/Kurs/Tagespuzzle, Endlos)." },
  ]},
  { version: "0.139.0", date: "2026-06-15", changes: [
    { en: "Endless mode: Your very first run (as long as there is no completed session yet) now follows a deliberately steep difficulty curve — rating 2000 after 15 puzzles, 3000 after 30 — so that the first puzzle rush ends reliably and relatively quickly. From the second run on, the adaptive curve, which is based on your previous level, applies again.", de: "Endlosmodus: Dein allererster Lauf (solange noch keine abgeschlossene Session vorliegt) folgt jetzt einer bewusst steilen Schwierigkeitskurve — Rating 2000 nach 15 Puzzles, 3000 nach 30 — damit der erste Puzzle-Rush relativ schnell sicher endet. Ab dem zweiten Lauf greift wieder die adaptive Kurve, die sich an deinem bisherigen Niveau orientiert." },
    { en: "In the first run, the phase indicator (Phase 1/2/3) follows this steep curve accordingly (boundaries at puzzle 15 and 30).", de: "Die Phasenanzeige (Phase 1/2/3) richtet sich im ersten Lauf passend nach dieser steilen Kurve (Grenzen bei Puzzle 15 und 30)." },
  ]},
  { version: "0.138.0", date: "2026-06-15", changes: [
    { en: "In the \"Share puzzle\" popup you can now also send the puzzle directly to friends (the same multi-select button as after solving) — and indeed both for the current puzzle and, if available, for the previous puzzle (via a toggle in the dialog).", de: "Im „Puzzle teilen\"-Popup kannst du das Puzzle jetzt auch direkt an Freunde schicken (gleicher Mehrfach-Auswahl-Button wie nach dem Lösen) — und zwar sowohl für das aktuelle als auch, sofern vorhanden, für das vorherige Puzzle (per Umschalter im Dialog)." },
  ]},
  { version: "0.137.1", date: "2026-06-15", changes: [
    { en: "Sending a challenge in Endless/Course/Book mode: As soon as you open the \"Send to friend\" menu, the automatic next-puzzle countdown stops — you now have time to calmly select friends without the puzzle jumping ahead.", de: "Challenge verschicken im Endlos-/Kurs-/Buchmodus: Sobald du das „An Freund schicken\"-Menü öffnest, stoppt der automatische Weiter-Countdown — du hast jetzt Zeit, in Ruhe Freunde auszuwählen, ohne dass das Puzzle weiterspringt." },
  ]},
  { version: "0.137.0", date: "2026-06-15", changes: [
    { en: "\"Send to friend\" is now available in all puzzle modes: Standard, Endless, single book puzzle, Daily puzzle and Course (Weekly post excepted). Book/Course puzzles can be sent just like classic puzzles.", de: "„An Freund schicken\" gibt es jetzt in allen Puzzle-Modi: Standard, Endlos, Einzel-Buchpuzzle, Tagespuzzle und Kurs (Wochenpost ausgenommen). Buch-/Kurspuzzles lassen sich damit ebenso verschicken wie klassische Puzzles." },
    { en: "You can now send a puzzle to several friends at once: in the menu a checkbox per friend, \"Select all\" and \"Send (n)\". The recipient solves the challenge as usual; the result (solved/failed) is reported back to you.", de: "Du kannst ein Puzzle jetzt an mehrere Freunde gleichzeitig schicken: Im Menü pro Freund eine Checkbox, „Alle auswählen\" und „Senden (n)\". Der Empfänger löst die Challenge wie gewohnt; das Ergebnis (gelöst/gescheitert) wird dir zurückgemeldet." },
  ]},
  { version: "0.136.1", date: "2026-06-15", changes: [
    { en: "Endless mode fix: At 0 hearts the run is definitively over. The fatal puzzle can still be played again via \"Retry\", but a solved retry no longer revives the run — you no longer keep playing with 0 hearts (previously you could play on beyond the heart limit this way).", de: "Endlosmodus-Fix: Bei 0 Herzen ist der Lauf endgültig vorbei. Das tödliche Puzzle lässt sich per „Retry\" zwar nochmal spielen, ein gelöstes Retry belebt den Lauf aber nicht mehr — man spielt nicht länger mit 0 Herzen weiter (vorher konnte man so über die Herzgrenze hinaus weiterspielen)." },
  ]},
  { version: "0.136.0", date: "2026-06-15", changes: [
    { en: "Endless run history: A new column \"Elo ±\" shows per run how much Elo you gained (green, e.g. +340) or lost (red) — measured as the maximum rating reached minus the starting Elo of the run.", de: "Endlosrun-History: Neue Spalte „Elo ±\" zeigt je Lauf, wie viel Elo du gestiegen (grün, z. B. +340) oder gefallen (rot) bist — gemessen als erreichtes Max-Rating minus Start-Elo des Laufs." },
  ]},
  { version: "0.135.0", date: "2026-06-15", changes: [
    { en: "Repertoires: When editing/creating there is now the option \"Use for the extension\". Only repertoires marked this way are used by the browser extension/userscript for listing and deviation detection (existing repertoires stay active). The note in the Repertoires tab also clarifies that the extension only shows deviations in analysis mode (analysis board) on chess.com/lichess.org.", de: "Repertoires: Beim Bearbeiten/Anlegen gibt es jetzt die Option „Für die Extension verwenden\". Nur so markierte Repertoires werden von der Browser-Erweiterung/dem Userscript fürs Listing und die Abweichungserkennung genutzt (bestehende Repertoires bleiben aktiv). Der Hinweis im Repertoires-Tab stellt außerdem klar, dass die Extension Abweichungen nur im Analysemodus (Analysebrett) auf chess.com/lichess.org anzeigt." },
  ]},
  { version: "0.134.2", date: "2026-06-15", changes: [
    { en: "Internal: The API now automatically retries transient database errors (EF Core EnableRetryOnFailure) — short connection losses during a MariaDB restart/recreate no longer cause background tasks/requests to fail hard.", de: "Intern: API wiederholt transiente Datenbankfehler jetzt automatisch (EF Core EnableRetryOnFailure) — kurze Verbindungsverluste beim MariaDB-Neustart/Recreate lassen Background-Tasks/Requests nicht mehr hart fehlschlagen." },
  ]},
  { version: "0.134.1", date: "2026-06-15", changes: [
    { en: "Internal: Updated a stale CourseService test (the course result POST has, since the chapter overview, additionally included chapterIndex) — test gate green again.", de: "Intern: Stehengebliebenen CourseService-Test nachgezogen (Kurs-Ergebnis-POST enthält seit der Kapitelübersicht zusätzlich chapterIndex) — Test-Gate wieder grün." },
  ]},
  { version: "0.134.0", date: "2026-06-15", changes: [
    { en: "Notification bell: Merely opening it no longer automatically marks everything as read — unread entries stay highlighted. Instead, there is a \"Mark all as read\" button at the top of the bell menu that clears the badge.", de: "Benachrichtigungs-Glocke: Das bloße Öffnen markiert nicht mehr automatisch alles als gelesen — ungelesene Einträge bleiben hervorgehoben. Stattdessen gibt es oben im Glocken-Menü einen Button „Alle als gelesen markieren\", der das Badge leert." },
  ]},
  { version: "0.133.0", date: "2026-06-15", changes: [
    { en: "Notifications: Via \"Show all\" in the bell there is now a dedicated page with the complete notification history (paginated, \"Load more\") — not just the last few.", de: "Benachrichtigungen: Über „Alle anzeigen\" in der Glocke gibt es jetzt eine eigene Seite mit der vollständigen Benachrichtigungs-History (paginiert, „Mehr laden\") — nicht nur die letzten paar." },
  ]},
  { version: "0.132.0", date: "2026-06-15", changes: [
    { en: "The Chessable queue is now fair: If one user queues many courses in a row and another joins, that user's first course moves directly to second place — after that the users' imports are processed alternately (round-robin), instead of one user blocking all the others.", de: "Chessable-Warteschlange ist jetzt fair: Reiht ein Nutzer viele Kurse hintereinander ein und ein anderer kommt dazu, rückt dessen erster Kurs direkt an die zweite Stelle — danach werden die Importe der Nutzer abwechselnd verarbeitet (Round-Robin), statt dass einer alle anderen blockiert." },
  ]},
  { version: "0.131.0", date: "2026-06-15", changes: [
    { en: "Chessable import: The \"Course finished\" notification now shows how long the import took — waiting time in the queue and pure fetch time. In the admin overview the duration is additionally shown for each completed import.", de: "Chessable-Import: Die „Kurs fertig\"-Benachrichtigung zeigt jetzt, wie lange der Import gedauert hat — Wartezeit in der Warteschlange und reine Holzeit. In der Admin-Übersicht steht die Dauer zusätzlich bei jedem abgeschlossenen Import." },
  ]},
  { version: "0.130.0", date: "2026-06-15", changes: [
    { en: "Chessable import: On the Chessable page there is now a note about the fetch pace (roughly 15–20 lines/min — a course with 500 lines takes about 30 min, 1000 lines about 1 hour). During fetching, an estimated remaining time is shown based on the chapter progress so far.", de: "Chessable-Import: Auf der Chessable-Seite steht jetzt ein Hinweis zum Hol-Tempo (grob 15–20 Zeilen/min — ein Kurs mit 500 Zeilen dauert ca. 30 Min, 1000 Zeilen ca. 1 Std). Während des Holens wird aus dem bisherigen Kapitel-Fortschritt eine geschätzte Restzeit eingeblendet." },
  ]},
  { version: "0.129.0", date: "2026-06-15", changes: [
    { en: "Training goals: Below the tracker there is now a daily history — a table that, for each day with activity, shows the number per category (Puzzles and Book/Course in minutes, Play in games), newest first.", de: "Trainingsziele: Unter dem Tracker gibt es jetzt eine Tageshistory — eine Tabelle, die für jeden Tag mit Aktivität je Kategorie die Zahl zeigt (Puzzles und Buch/Kurs in Minuten, Spielen in Partien), neueste zuerst." },
  ]},
  { version: "0.128.0", date: "2026-06-15", changes: [
    { en: "Courses now have a chapter overview: In each course card the chapter list can be expanded. Each chapter shows its own progress bar (solved/total) and can be worked through separately, sequentially or randomly.", de: "Kurse haben jetzt eine Kapitelübersicht: In jeder Kurs-Karte lässt sich die Kapitelliste aufklappen. Jedes Kapitel zeigt einen eigenen Fortschrittsbalken (gelöst/gesamt) und kann separat sequenziell oder zufällig durchgearbeitet werden." },
  ]},
  { version: "0.127.1", date: "2026-06-15", changes: [
    { en: "New notifications now stand out more clearly: instead of a small dot, the bell shows a bold red \"!\" (subtly pulsing) and turns red. The count is shown in the tooltip.", de: "Neue Benachrichtigungen fallen jetzt deutlicher auf: statt eines kleinen Punkts zeigt die Glocke ein fettes rotes „!\" (dezent pulsierend) und färbt sich rot. Die Anzahl steht im Tooltip." },
  ]},
  { version: "0.127.0", date: "2026-06-15", changes: [
    { en: "In your profile you can now store, change or remove your email address (e.g. for resetting the password).", de: "Im Profil kannst du jetzt deine E-Mail-Adresse hinterlegen, ändern oder entfernen (z. B. für das Zurücksetzen des Passworts)." },
  ]},
  { version: "0.126.0", date: "2026-06-15", changes: [
    { en: "The course overview is now split into two sections: \"Public courses\" (shared via your groups) and \"Chessable courses\" (imported by yourself). Sections with no courses are hidden.", de: "Die Kurs-Übersicht ist jetzt in zwei Bereiche aufgeteilt: „Öffentliche Kurse\" (über deine Gruppen freigegeben) und „Chessable-Kurse\" (von dir selbst importiert). Bereiche ohne Kurse werden ausgeblendet." },
  ]},
  { version: "0.125.0", date: "2026-06-15", changes: [
    { en: "New notification bell at the top of the navigation: a red dot shows when something new is there. In the dropdown you see the latest events and jump to the relevant place with one click; on opening, everything is considered read.", de: "Neue Benachrichtigungs-Glocke oben in der Navigation: ein roter Punkt zeigt, wenn etwas Neues da ist. Im Dropdown siehst du die letzten Ereignisse und springst mit einem Klick zur passenden Stelle; beim Öffnen gilt alles als gelesen." },
    { en: "You are now notified about: a finished Chessable course import (or a failed import), a new friend request, an accepted request, a puzzle challenge sent to you/resolved, as well as when a friend tackles one of your puzzles in revenge mode. (Email/Push will follow later.)", de: "Du wirst jetzt benachrichtigt bei: fertig importiertem Chessable-Kurs (oder fehlgeschlagenem Import), neuer Freundschaftsanfrage, angenommener Anfrage, einer an dich gesendeten/aufgelösten Puzzle-Challenge sowie wenn ein Freund eines deiner Puzzles im Revenge-Modus angeht. (E-Mail/Push folgen später.)" },
    { en: "The previous counter on the friends menu has moved into the central bell.", de: "Der bisherige Zähler am Freunde-Menü ist in die zentrale Glocke umgezogen." },
  ]},
  { version: "0.124.0", date: "2026-06-15", changes: [
    { en: "Chessable (Admin): In the Chessable tab admins now see a new section \"All imports (Admin)\" with all course imports of all users — including sender, status and the complete history, updated live.", de: "Chessable (Admin): Im Chessable-Tab sehen Admins jetzt einen neuen Bereich „Alle Importe (Admin)\" mit sämtlichen Kurs-Importen aller Nutzer — inklusive Absender, Status und vollständigem Verlauf, live aktualisiert." },
    { en: "Dashboard (Admin): As soon as a Chessable import is running or waiting anywhere, a \"Chessable queue\" card appears on the dashboard for admins, with the count and a list of the active imports (course, user, progress).", de: "Dashboard (Admin): Sobald irgendwo ein Chessable-Import läuft oder wartet, erscheint für Admins auf dem Dashboard eine Karte „Chessable-Warteschlange\" mit der Anzahl und einer Liste der aktiven Importe (Kurs, Nutzer, Fortschritt)." },
  ]},
  { version: "0.123.1", date: "2026-06-14", changes: [
    { en: "Chessable course import: Large courses no longer abort with \"Timeout\". The fetch now keeps running as long as it is making progress (instead of stopping after a fixed 15 minutes) — and is automatically restarted on a real stall, instead of failing.", de: "Chessable-Kurs-Import: Große Kurse brechen nicht mehr mit „Zeitüberschreitung\" ab. Der Abruf läuft jetzt so lange weiter, wie er Fortschritt macht (statt nach festen 15 Minuten zu stoppen) — und wird bei einem echten Stillstand automatisch neu gestartet, statt fehlzuschlagen." },
  ]},
  { version: "0.123.0", date: "2026-06-14", changes: [
    { en: "Puzzle Elo settles in faster at the start: As long as your level has not yet been found, the Elo steps are larger (in both directions) — fourfold until you have solved at least 5 puzzles AND failed 5, then double until 10/10, then normal. So you land at the right difficulty after a few puzzles instead of only after many.", de: "Puzzle-Elo pendelt sich am Anfang schneller ein: Solange dein Niveau noch nicht gefunden ist, sind die Elo-Schritte größer (in beide Richtungen) — vierfach, bis du mindestens 5 Puzzles gelöst UND 5 nicht gelöst hast, danach doppelt bis 10/10, dann normal. So landest du nach wenigen Puzzles beim passenden Schwierigkeitsgrad statt erst nach vielen." },
  ]},
  { version: "0.122.0", date: "2026-06-14", changes: [
    { en: "Revenge mode as a round: When you tackle a failed puzzle of a friend, you stay in revenge mode after solving and get the next open puzzle of that friend directly — until all are done. To finish, there is a congratulations message with fireworks and the number of avenged puzzles.", de: "Revenge-Modus als Runde: Wenn du ein gescheitertes Puzzle eines Freundes angehst, bleibst du nach dem Lösen im Revenge-Modus und bekommst direkt das nächste offene Puzzle dieses Freundes — bis alle durch sind. Zum Abschluss gibt es eine Glückwunsch-Meldung mit Feuerwerk und der Anzahl gerächter Puzzles." },
    { en: "Friends revenge list: Already avenged puzzles (solved by you) disappear from the open list and collect in a new section \"Already done\".", de: "Freunde-Revanche-Liste: Bereits gerächte (von dir gelöste) Puzzles verschwinden aus der offenen Liste und sammeln sich in einem neuen Abschnitt „Bereits erledigt\"." },
  ]},
  { version: "0.121.0", date: "2026-06-14", changes: [
    { en: "Revenge notifications: When a friend tackles one of your failed puzzles in revenge mode, you are now informed — whether he solves it or also fails. The messages appear in the Challenges tab under \"Revenge on your puzzles\" and count toward the bell badge in the navigation (marked as read when you open the friends page).", de: "Revenge-Benachrichtigungen: Wenn ein Freund eines deiner gescheiterten Puzzles im Revenge-Modus angeht, wirst du jetzt informiert — egal ob er es löst oder ebenfalls scheitert. Die Meldungen erscheinen im Challenges-Tab unter „Revanche an deinen Puzzles\" und zählen mit ins Glocken-Badge der Navigation (wird beim Öffnen der Freunde-Seite als gelesen markiert)." },
  ]},
  { version: "0.120.0", date: "2026-06-14", changes: [
    { en: "Friend challenges: After solving a puzzle you can pass it directly to a friend as a challenge via \"Send to friend\". The friend sees it in the new Challenges tab (inbox) and on the bell counter in the navigation; after solving, the result (solved/failed + time) is reported back to you under \"Sent\".", de: "Freunde-Challenges: Nach dem Lösen eines Puzzles kannst du es per „An Freund schicken\" direkt an einen Freund als Challenge weitergeben. Der Freund sieht sie im neuen Challenges-Tab (Posteingang) und am Glocken-Zähler in der Navigation; nach dem Lösen wird dir das Ergebnis (gelöst/gescheitert + Zeit) unter „Gesendet\" zurückgemeldet." },
  ]},
  { version: "0.119.0", date: "2026-06-14", changes: [
    { en: "Friends: \"Revenge a Friend\" — via the new icon in the friends list you see puzzles a friend has failed and has not solved to this day. With one click you solve the same puzzle yourself and do it better. List sorted by most recent failed attempt, with rating, themes and number of failed attempts.", de: "Freunde: „Revenge a Friend\" — über das neue Symbol in der Freundesliste siehst du Puzzles, an denen ein Freund gescheitert ist und die er bis heute nicht gelöst hat. Mit einem Klick löst du dasselbe Puzzle selbst und machst es besser. Liste sortiert nach jüngstem Fehlversuch, mit Wertung, Themen und Anzahl der Fehlversuche." },
  ]},
  { version: "0.118.0", date: "2026-06-14", changes: [
    { en: "Friends: New statistics comparison \"You vs. friend\". In the friends list, the chart icon opens a comparison page with puzzle Elo, solved puzzles, attempts, accuracy as well as current and best streak — side by side, with the better value highlighted in each case. Below it a themes comparison (accuracy per tactic theme).", de: "Freunde: Neuer Statistik-Vergleich „Du vs. Freund\". In der Freundesliste öffnet das Diagramm-Symbol eine Vergleichsseite mit Puzzle-Elo, gelösten Puzzles, Versuchen, Genauigkeit sowie aktueller und bester Serie — jeweils nebeneinander, wobei der jeweils bessere Wert markiert wird. Darunter ein Themen-Vergleich (Genauigkeit je Taktik-Thema)." },
  ]},
  { version: "0.117.2", date: "2026-06-14", changes: [
    { en: "Robustness: Ending \"Log in as user\" no longer hangs even with a corrupted session backup (clean logout instead of an error). Copying an API token no longer fails silently when the clipboard is unavailable.", de: "Robustheit: „Als Nutzer einsteigen\" beenden bleibt auch bei einem beschädigten Sitzungs-Backup nicht mehr hängen (sauberer Logout statt Fehler). Das Kopieren eines API-Tokens scheitert nicht mehr stillschweigend, wenn die Zwischenablage nicht verfügbar ist." },
    { en: "Internal: Tournament monitor and crawl job responses are now typed (instead of `any`) — no visible change.", de: "Intern: Turnier-Monitor- und Crawl-Job-Antworten sind jetzt typisiert (statt `any`) — keine sichtbare Änderung." },
  ]},
  { version: "0.117.1", date: "2026-06-14", changes: [
    { en: "Multilingual: The explanations of the visualization levels (Normal/Blindfold/Checker/Dark/Invisible) and of the difficulty levels in the puzzle settings now appear in the selected language instead of being fixed to German.", de: "Mehrsprachigkeit: Die Erklärungen der Visualisierungs-Stufen (Normal/Blindspiel/Checker/Dunkel/Unsichtbar) und der Schwierigkeitsgrade in den Puzzle-Einstellungen erscheinen jetzt in der gewählten Sprache statt fest auf Deutsch." },
    { en: "Security/maintenance: External links (Chess-Results, RepCheck) now consistently open with rel=\"noopener noreferrer\". Cleanup: unused visualization code removed.", de: "Sicherheit/Wartung: Externe Links (Chess-Results, RepCheck) öffnen jetzt durchgängig mit rel=\"noopener noreferrer\". Aufräumen: ungenutzter Visualisierungs-Code entfernt." },
  ]},
  { version: "0.117.0", date: "2026-06-13", changes: [
    { en: "Endless mode: The theme selection is now a searchable multi-select field instead of a free text field. Just type to filter from all available themes, and select as many as you like with a click — the selection appears as removable chips. Custom/free themes can still be entered via Enter, comma or space.", de: "Endless-Modus: Die Themen-Auswahl ist jetzt ein durchsuchbares Mehrfach-Auswahlfeld statt eines freien Textfelds. Einfach tippen, um aus allen verfügbaren Themen zu filtern, und beliebig viele per Klick auswählen — die Auswahl erscheint als entfernbare Chips. Eigene/freie Themen lassen sich weiterhin per Enter, Komma oder Leertaste eintippen." },
  ]},
  { version: "0.116.1", date: "2026-06-13", changes: [
    { en: "Chessable import: The currently running import is shown again as \"running now\" in the queue. Previously, with many (>20) imports it could fall out of the list, so that only queue slots were visible, even though a course was already being fetched in the background.", de: "Chessable-Import: Der gerade laufende Import wird in der Warteschlange wieder als „läuft gerade\" angezeigt. Bisher konnte er bei vielen (>20) Importen aus der Liste fallen, sodass nur Warteschlangen-Plätze zu sehen waren, obwohl im Hintergrund schon ein Kurs geladen wurde." },
  ]},
  { version: "0.116.0", date: "2026-06-13", changes: [
    { en: "Chessable: New one-click bookmarklet (under \"How do I get my token?\"). Drag it into the bookmarks bar once, click it after the Chessable login — RookHub takes over the token automatically, no more copying from the developer tools.", de: "Chessable: Neues Ein-Klick-Bookmarklet (unter „Wie komme ich an meinen Token?“). Einmal in die Lesezeichenleiste ziehen, nach dem Chessable-Login anklicken — RookHub übernimmt den Token automatisch, kein Kopieren aus den Entwicklertools mehr nötig." },
    { en: "The token is passed securely via URL fragment (never lands in a server log) and is immediately removed from the address bar again.", de: "Der Token wird dabei sicher per URL-Fragment übergeben (landet nie in einem Server-Log) und sofort wieder aus der Adresszeile entfernt." },
  ]},
  { version: "0.115.1", date: "2026-06-13", changes: [
    { en: "Croatian translation completed (73 missing texts added: e.g. menu visibility, \"Log in as user\", Chessable import).", de: "Kroatische Übersetzung vervollständigt (73 fehlende Texte ergänzt: u. a. Menü-Sichtbarkeit, „Als Nutzer einsteigen\", Chessable-Import)." },
    { en: "Maintenance: Angular frontend updated to the latest 19.2 patch; the tournament crawler no longer logs full page contents (leaner, more data-economical).", de: "Wartung: Angular-Frontend auf den neuesten 19.2-Patch aktualisiert; der Turnier-Crawler protokolliert keine vollständigen Seiteninhalte mehr (schlanker, datensparsamer)." },
  ]},
  { version: "0.115.0", date: "2026-06-13", changes: [
    { en: "Course training time now counts fully toward the daily goal: The time of EVERY attempt is recorded (solved, failed, give up and repeat) instead of only the first solution — previously a lot of practiced time was lost.", de: "Kurs-Trainingszeit zählt jetzt vollständig aufs Tagesziel: Es wird die Zeit JEDES Versuchs erfasst (gelöst, fehlgeschlagen, Aufgeben und Wiederholung) statt nur die erste Lösung — bisher ging viel geübte Zeit verloren." },
    { en: "Books now have a type (Admin → Books, column \"Type\"): For a puzzle/tactics book the course time counts toward the \"Puzzles\" category, for a study book toward \"Book/Course\". Existing books count as a puzzle book by default.", de: "Bücher haben jetzt eine Art (Admin → Bücher, Spalte „Art“): Bei einem Puzzle-/Taktikbuch zählt die Kurszeit in die Kategorie „Puzzles“, bei einem Studienbuch in „Buch/Kurs“. Bestehende Bücher gelten standardmäßig als Puzzlebuch." },
  ]},
  { version: "0.114.4", date: "2026-06-13", changes: [
    { en: "Internal refactoring of the tournament detail view: HTTP access moved into a dedicated service and the favorites filter logic moved into a tested helper function (no visible change). Also repaired the frontend unit test run again (the navbar test had been broken since the menu visibility feature).", de: "Internes Refactoring der Turnier-Detailansicht: HTTP-Zugriffe in einen eigenen Service ausgelagert und die Favoriten-Filterlogik in eine getestete Hilfsfunktion verschoben (keine sichtbare Änderung). Außerdem den Frontend-Unit-Test-Lauf wieder repariert (Navbar-Test war seit der Menü-Sichtbarkeit kaputt)." },
  ]},
  { version: "0.114.3", date: "2026-06-13", changes: [
    { en: "Crawler (internal): If a client cancels the player search, the running request to chess-results.com is now cleanly aborted instead of being needlessly completed (CancellationToken passed through).", de: "Crawler (intern): Bricht ein Client die Spielersuche ab, wird die laufende Anfrage an chess-results.com jetzt sauber abgebrochen statt unnötig zu Ende geführt (CancellationToken durchgereicht)." },
  ]},
  { version: "0.114.2", date: "2026-06-13", changes: [
    { en: "Security (crawler standalone setup): The example compose file no longer contains default passwords — DB passwords must now be set explicitly in the .env, otherwise the stack will not start at all.", de: "Sicherheit (Crawler-Standalone-Setup): Die Beispiel-Compose-Datei enthält keine Default-Passwörter mehr — DB-Passwörter müssen jetzt explizit in der .env gesetzt werden, sonst startet der Stack gar nicht erst." },
  ]},
  { version: "0.114.1", date: "2026-06-13", changes: [
    { en: "CI: Docker images are only built and published after the automated tests are green (applies to RookHub API, frontend and crawler) — faulty builds no longer reach the registry.", de: "CI: Docker-Images werden erst gebaut und veröffentlicht, nachdem die automatischen Tests grün sind (gilt für RookHub-API, Frontend und Crawler) — fehlerhafte Builds gelangen nicht mehr in die Registry." },
  ]},
  { version: "0.114.0", date: "2026-06-13", changes: [
    { en: "New API endpoints for the Discord bot's Daily puzzle leaderboard: a monthly ranking (points per Daily puzzle solved on the first attempt + daily bonus for the fastest solvers) and an all-time hall of fame (most solutions, most gold days, fastest solution).", de: "Neue API-Endpunkte fürs Tagespuzzle-Leaderboard des Discord-Bots: eine Monats-Wertung (Punkte je im Erstversuch gelöstem Tagespuzzle + Tages-Bonus für die schnellsten Löser) und eine all-time Hall of Fame (meiste Lösungen, meiste Gold-Tage, schnellste Lösung)." },
  ]},
  { version: "0.113.0", date: "2026-06-13", changes: [
    { en: "Admins can now \"Log in as user\" in the admin area (login icon per user) and see the app from that user's perspective — a red banner at the top shows the impersonation, \"Back to admin\" ends it again.", de: "Admins können jetzt im Admin-Bereich „Als Nutzer einsteigen\" (Login-Symbol je Nutzer) und die App aus dessen Sicht sehen — ein rotes Banner oben zeigt die Impersonation an, „Zurück zu Admin\" beendet sie wieder." },
    { en: "The login is logged server-side (audit) and the session token deliberately expires quickly.", de: "Der Einstieg wird serverseitig protokolliert (Audit) und das Sitzungstoken läuft bewusst kurz ab." },
  ]},
  { version: "0.112.0", date: "2026-06-13", changes: [
    { en: "Admins can now set per menu entry who sees it: everyone (even logged out), registered users, specific groups or admins only (Admin → new \"Menu\" tab).", de: "Admins können jetzt pro Menüeintrag festlegen, wer ihn sieht: alle (auch ausgeloggt), registrierte Nutzer, bestimmte Gruppen oder nur Admins (Admin → neuer Tab „Menü\")." },
    { en: "Hidden entries are not only hidden in the navigation, but also blocked when the page URL is accessed directly.", de: "Ausgeblendete Einträge sind nicht nur in der Navigation versteckt, sondern auch beim direkten Aufruf der Seiten-URL gesperrt." },
  ]},
  { version: "0.111.0", date: "2026-06-12", changes: [
    { en: "Courses can now be downloaded as PGN (download icon at each course) — one game per line, generated from the stored puzzles.", de: "Kurse lassen sich jetzt als PGN herunterladen (Download-Symbol bei jedem Kurs) — ein Spiel je Linie, aus den gespeicherten Puzzles erzeugt." },
    { en: "Repertoires also get a \"Download PGN\" button (combined PGN of all files).", de: "Repertoires bekommen ebenfalls einen „PGN herunterladen\"-Button (kombiniertes PGN aller Dateien)." },
  ]},
  { version: "0.110.1", date: "2026-06-12", changes: [
    { en: "If a course is already cached (e.g. from an earlier import), the import is now processed immediately instead of waiting in the queue — the queue is now only for courses that actually have to be fetched from Chessable.", de: "Ist ein Kurs bereits zwischengespeichert (z. B. von einem früheren Import), wird der Import jetzt sofort verarbeitet statt in der Warteschlange zu warten — die Warteschlange ist nur noch für Kurse da, die tatsächlich von Chessable geholt werden müssen." },
  ]},
  { version: "0.110.0", date: "2026-06-12", changes: [
    { en: "Chessable import queue: When triggering an import, a message appears with the number of courses still ahead of you in the global queue. At the top of the page you see your own waiting/running imports including queue position.", de: "Chessable-Import-Warteschlange: Beim Anstoßen eines Imports erscheint eine Meldung mit der Anzahl der Kurse, die noch vor dir in der globalen Warteschlange sind. Oben auf der Seite sieht man die eigenen wartenden/laufenden Importe samt Warteposition." },
    { en: "Imports can be paused, resumed and aborted.", de: "Importe lassen sich pausieren, fortsetzen und abbrechen." },
    { en: "Several courses of the same user can be queued at once; the server processes them one after another.", de: "Mehrere Kurse desselben Nutzers können gleichzeitig eingereiht werden; der Server arbeitet sie nacheinander ab." },
    { en: "The bearer token help additionally points to the RepCheck browser extension.", de: "Bearer-Token-Hilfe weist zusätzlich auf die RepCheck-Browser-Erweiterung hin." },
    { en: "If a second user downloads the same course, the already stored raw data is used — no renewed fetch at Chessable (per course/bid, persistent cache).", de: "Lädt ein zweiter Nutzer denselben Kurs herunter, werden die bereits gespeicherten Rohdaten genutzt — kein erneuter Abruf bei Chessable (kurs-/bid-weiter, persistenter Cache)." },
  ]},
  { version: "0.109.2", date: "2026-06-12", changes: [
    { en: "After importing a Chessable course as a book, the \"Courses\" menu now appears immediately (previously only after a reload). It shows — as before — only your own imported books.", de: "Nach dem Import eines Chessable-Kurses als Buch erscheint das „Kurse\"-Menü jetzt sofort (vorher erst nach Neuladen). Es zeigt — wie bisher — nur die eigenen importierten Bücher." },
  ]},
  { version: "0.109.1", date: "2026-06-12", changes: [
    { en: "Chessable disclaimer worded more clearly (explicit note: a ban on Chessable is not my fault; only for personal backups).", de: "Chessable-Haftungsausschluss klarer formuliert (ausdrücklicher Hinweis: ein Bann auf Chessable ist nicht meine Schuld; nur für persönliche Backups)." },
  ]},
  { version: "0.109.0", date: "2026-06-12", changes: [
    { en: "Chessable: On first opening, a disclaimer now appears that must be confirmed (use at your own risk, only for personal backups) — the confirmation is stored per account.", de: "Chessable: Beim ersten Öffnen erscheint jetzt ein Haftungsausschluss, der bestätigt werden muss (Nutzung auf eigene Gefahr, nur für persönliche Backups) — die Bestätigung wird pro Konto gespeichert." },
    { en: "At the bearer field, instead of the short note there is a help link that explains step by step how to find your Chessable bearer token.", de: "Beim Bearer-Feld steht statt des Kurz-Hinweises ein Hilfe-Link, der Schritt für Schritt erklärt, wie man seinen Chessable-Bearer-Token findet." },
  ]},
  { version: "0.108.4", date: "2026-06-12", changes: [
    { en: "The Chessable course list now shows per course whether it has already been imported as a repertoire and/or as a book (green checkmark instead of a button) — already imported variants cannot be triggered again.", de: "Chessable-Kursliste zeigt jetzt pro Kurs, ob er bereits als Repertoire und/oder als Buch importiert wurde (grünes Häkchen statt Button) — schon importierte Varianten lassen sich nicht erneut anstoßen." },
    { en: "Several courses can be queued at once; the server processes them one after another, each row shows its own status (in queue / fetching course / importing).", de: "Mehrere Kurse lassen sich gleichzeitig in die Warteschlange legen; der Server arbeitet sie nacheinander ab, jede Zeile zeigt ihren eigenen Status (in Warteschlange / hole Kurs / importiere)." },
    { en: "If an already fetched course is imported in the other variant (e.g. after \"Repertoire\" also \"Book\"), the data is reused — no renewed fetch at Chessable.", de: "Wird ein bereits geholter Kurs in der anderen Variante importiert (z. B. nach „Repertoire\" noch „Buch\"), werden die Daten wiederverwendet — kein erneuter Abruf bei Chessable." },
    { en: "Course list layout reworked (status text no longer overlaps with the next row).", de: "Layout der Kursliste überarbeitet (Status-Text überlappt nicht mehr mit der nächsten Zeile)." },
  ]},
  { version: "0.108.3", date: "2026-06-12", changes: [
    { en: "The Chessable import now shows real progress: while the course is being fetched, the chapter and line count runs along (e.g. \"Chapter 3/12 · 45 lines\") instead of just a spinner.", de: "Chessable-Import zeigt jetzt echten Fortschritt: während der Kurs geholt wird, läuft die Kapitel- und Linienzahl mit (z. B. „Kapitel 3/12 · 45 Linien\") statt nur eines Spinners." },
  ]},
  { version: "0.108.2", date: "2026-06-12", changes: [
    { en: "Chessable courses are now stored: The course list loads automatically when the page is opened (no more \"Load courses\" needed); a \"Refresh\" button fetches them anew when needed. The status is shown.", de: "Chessable-Kurse werden jetzt gespeichert: Die Kursliste lädt beim Öffnen der Seite automatisch (kein „Kurse laden\" mehr nötig); ein „Aktualisieren\"-Button holt sie bei Bedarf neu. Der Stand wird angezeigt." },
    { en: "Better feedback during import: a status banner shows the imported course including the current phase (fetching course… / importing…) instead of just a spinner. If an import is still running when the page is reloaded, it is shown again.", de: "Bessere Rückmeldung beim Import: ein Statusbanner zeigt den importierten Kurs samt aktueller Phase (hole Kurs… / importiere…) statt nur eines Spinners. Läuft beim Neuladen der Seite ein Import noch, wird er wieder angezeigt." },
  ]},
  { version: "0.108.1", date: "2026-06-12", changes: [
    { en: "Chessable import more robust: The progress is stored in the database (including the already fetched course). If the app is restarted during an import, it automatically resumes on the next start — without fetching the course again at Chessable and without creating anything twice. During the import the current phase is shown (fetching course… / importing…).", de: "Chessable-Import robuster: Der Fortschritt wird in der Datenbank gespeichert (inkl. des bereits geholten Kurses). Wird die App während eines Imports neu gestartet, setzt er beim nächsten Start automatisch fort — ohne den Kurs erneut bei Chessable abzurufen und ohne etwas doppelt anzulegen. Während des Imports wird die aktuelle Phase angezeigt (hole Kurs… / importiere…)." },
  ]},
  { version: "0.108.0", date: "2026-06-12", changes: [
    { en: "Chessable course import: On the \"Chessable\" page, any listed course can now be imported with a single click — either \"As repertoire\" (ends up as PGN under Repertoires) or \"As book\" (becomes a personal, playable course under Courses; the first key move of each variation is the trainable solution).", de: "Chessable-Kurs-Import: In der „Chessable\"-Seite kann jetzt jeder gelistete Kurs mit einem Klick importiert werden — entweder „Als Repertoire\" (landet als PGN unter Repertoires) oder „Als Buch\" (wird zu einem persönlichen, durchspielbaren Kurs unter Kurse; der erste Schlüsselzug jeder Variante ist die trainierbare Lösung)." },
    { en: "The import runs in the background (a large course = many positions): after the click a loading indicator appears, and the result is reported once it is finished.", de: "Der Import läuft im Hintergrund (großer Kurs = viele Stellungen): nach dem Klick erscheint ein Lade-Indikator, das Ergebnis wird gemeldet, sobald es fertig ist." },
    { en: "Personal books: A book you imported yourself is only visible to your own account (plus admins) — separate from the group-shared admin books.", de: "Persönliche Bücher: Ein selbst importiertes Buch sieht nur der eigene Account (zusätzlich Admins) — getrennt von den gruppenfreigegebenen Admin-Büchern." },
  ]},
  { version: "0.107.1", date: "2026-06-12", changes: [
    { en: "Chessable requests now run over the VPN (gluetun proxy): piratechess explicitly routes the curl calls through the proxy instead of directly with the host IP — so the crawler's IP rotation also applies to Chessable.", de: "Chessable-Requests laufen jetzt über die VPN (gluetun-Proxy): piratechess leitet die curl-Calls explizit über den Proxy, statt mit der Host-IP direkt — damit greift auch die IP-Rotation des Crawlers für Chessable." },
    { en: "More observability in Elasticsearch/Kibana: skipped Chessable lines and aborted course fetches are logged; RookHub now logs failed Chessable calls as a warning (instead of info); the crawler records the new IP after each VPN rotation.", de: "Mehr Beobachtbarkeit in Elasticsearch/Kibana: übersprungene Chessable-Linien und abgebrochene Kurs-Abrufe werden geloggt; RookHub loggt fehlgeschlagene Chessable-Aufrufe nun als Warnung (statt Info); der Crawler protokolliert nach jeder VPN-Rotation die neue IP." },
  ]},
  { version: "0.107.0", date: "2026-06-11", changes: [
    { en: "Chessable integration (frontend, stage 2): New page under \"Chessable\" (reachable from the account menu). Input field for your personal Bearer, Save/Delete/Test, \"Load courses\" lists your own Chessable courses — all without a Chessable login in the browser; the calls run server-side via the piratechess API.", de: "Chessable-Integration (Frontend, Etappe 2): Neue Seite unter „Chessable\" (im Konto-Menü erreichbar). Eingabefeld für den persönlichen Bearer, Speichern/Löschen/Test, „Kurse laden\" listet die eigenen Chessable-Kurse auf — alles ohne Chessable-Login im Browser, die Calls laufen serverseitig über die piratechess-API." },
    { en: "Translations into all 25 languages (German in detail, the remaining languages with English fallback text).", de: "Übersetzungen in alle 25 Sprachen (Deutsch ausführlich, restliche Sprachen mit englischem Fallback-Text)." },
    { en: "Logging update piratechess: same ECS/Elasticsearch stack as RookHub, with its own index `piratechess-logs-*`; the rookhub/piratechess stream shown side by side in Kibana.", de: "Logging-Update piratechess: gleicher ECS-/Elasticsearch-Stack wie RookHub, eigener Index `piratechess-logs-*`; rookhub/piratechess-Stream in Kibana nebeneinander." },
  ]},
  { version: "0.106.0", date: "2026-06-11", changes: [
    { en: "Chessable integration (backend, stage 1): RookHub can now store a personal Chessable Bearer per user encrypted in the database and use it via the piratechess API to test against Chessable or fetch your own courses. Endpoints: GET/POST/DELETE /api/chessable/credentials, POST /api/chessable/test, GET /api/chessable/courses.", de: "Chessable-Integration (Backend, Etappe 1): RookHub kann jetzt einen persönlichen Chessable-Bearer pro Nutzer verschlüsselt in der Datenbank ablegen und über die piratechess-API gegen Chessable testen bzw. die eigenen Kurse abrufen. Endpoints: GET/POST/DELETE /api/chessable/credentials, POST /api/chessable/test, GET /api/chessable/courses." },
    { en: "The actual Chessable calls (curl-impersonate against Cloudflare) stay in the piratechess stack; RookHub passes the Bearer through statelessly per request. Wired up via the external Docker network `chessable-bridge` (provided by piratechess_docker) and service-key auth (X-Service-Key) between the two APIs.", de: "Die eigentlichen Chessable-Calls (curl-impersonate gegen Cloudflare) bleiben im piratechess-Stack; RookHub reicht den Bearer pro Request stateless durch. Verkabelung über das externe Docker-Netz `chessable-bridge` (von piratechess_docker bereitgestellt) und Service-Key-Auth (X-Service-Key) zwischen den beiden APIs." },
    { en: "New env variables: CHESSABLE_API_URL, CHESSABLE_SERVICE_KEY (must match SERVICE_API_KEY in piratechess_docker), ENCRYPTION_KEY (AES key for the stored Bearer). Frontend follows in stage 2.", de: "Neue Env-Variablen: CHESSABLE_API_URL, CHESSABLE_SERVICE_KEY (muss mit SERVICE_API_KEY in piratechess_docker übereinstimmen), ENCRYPTION_KEY (AES-Schlüssel für den gespeicherten Bearer). Frontend folgt in Etappe 2." },
  ]},
  { version: "0.105.0", date: "2026-06-11", changes: [
    { en: "Forgot password: The login page now has \"Forgot password?\". After entering the registered email address, a time-limited reset link (valid for 1 hour, single use) is sent to that address; the link lets you set a new password. For privacy reasons, the page does not reveal whether an address is registered.", de: "Passwort vergessen: Auf der Anmeldeseite gibt es jetzt „Passwort vergessen?\". Nach Eingabe der hinterlegten E-Mail-Adresse wird ein zeitlich begrenzter Reset-Link (1 Stunde gültig, einmalig nutzbar) an diese Adresse geschickt; über den Link lässt sich ein neues Passwort setzen. Aus Datenschutzgründen verrät die Seite nicht, ob eine Adresse registriert ist." },
    { en: "Actual sending requires a configured SMTP server (environment variables EMAIL_SMTP_* + APP_BASE_URL); without configuration the mail is only written to the log.", de: "Voraussetzung für den tatsächlichen Versand ist ein konfigurierter SMTP-Server (Umgebungsvariablen EMAIL_SMTP_* + APP_BASE_URL); ohne Konfiguration wird die Mail nur ins Log geschrieben." },
  ]},
  { version: "0.104.1", date: "2026-06-11", changes: [
    { en: "Endless start with \"5 weakest topics\" significantly sped up: the chain generation fetches one puzzle per rating window via an index seek (TagId, Rating) instead of loading the entire rating span of all windows and sampling in memory. For frequent topics, the response time dropped from ~25 s to ~0.1 s.", de: "Endlos-Start mit „5 schwächste Themen\" deutlich beschleunigt: Die Ketten-Generierung holt pro Rating-Fenster gezielt ein Puzzle per Index-Seek (TagId, Rating), statt die gesamte Rating-Spanne aller Fenster zu laden und im Speicher zu sampeln. Bei häufigen Themen sank die Antwortzeit von ~25 s auf ~0,1 s." },
  ]},
  { version: "0.104.0", date: "2026-06-11", changes: [
    { en: "Endless mode history: Clicking a run now opens its detail view — the same summary as right after the game ends (max rating, solved puzzles, level, duration, lost lives, and the puzzle overview to replay). For this, the individual puzzle attempts of a run are now stored server-side; for old runs (before this update) the puzzle overview stays empty, the remaining values are shown.", de: "Endlos-Modus History: Ein Klick auf einen Lauf öffnet jetzt dessen Detail-Ansicht — dieselbe Zusammenfassung wie direkt nach Spielende (Max-Rating, gelöste Puzzles, Level, Dauer, verlorene Leben und die Puzzle-Übersicht zum Nachspielen). Dafür werden die einzelnen Puzzle-Versuche eines Laufs jetzt serverseitig gespeichert; bei Altläufen (vor diesem Update) bleibt die Puzzle-Übersicht leer, die übrigen Werte werden angezeigt." },
  ]},
  { version: "0.103.0", date: "2026-06-11", changes: [
    { en: "Share puzzle (random & endless mode): In the share dialog (QR code + link) you can now switch between the current and the previous puzzle via a button — so you can still share the puzzle you just solved and already advanced past.", de: "Puzzle teilen (Zufalls- & Endlos-Modus): Im Teilen-Dialog (QR-Code + Link) lässt sich jetzt per Button zwischen dem aktuellen und dem vorherigen Puzzle umschalten — so kann man auch das gerade gelöste, schon weitergeschaltete Puzzle noch teilen." },
  ]},
  { version: "0.102.1", date: "2026-06-11", changes: [
    { en: "Help page: Links in the \"Browser extension & userscript (RepCheck)\" section are now clickable (open in a new tab). Additionally linked: the signed Firefox extension on addons.mozilla.org alongside the GitHub repo and Tampermonkey userscript.", de: "Hilfeseite: Links im Abschnitt „Browser-Erweiterung & Userscript (RepCheck)\" sind jetzt klickbar (öffnen in neuem Tab). Zusätzlich verlinkt: die signierte Firefox-Erweiterung auf addons.mozilla.org neben GitHub-Repo und Tampermonkey-Userscript." },
  ]},
  { version: "0.102.0", date: "2026-06-11", changes: [
    { en: "Repertoires are now available to all logged-in users (previously only admins) — the \"Repertoires\" menu item appears for every logged-in account; everyone sees only their own repertoires. The API was already secured per user.", de: "Repertoires sind jetzt für alle eingeloggten Nutzer verfügbar (bisher nur Admins) — der Menüpunkt „Repertoires\" erscheint für jeden angemeldeten Account; jeder sieht ausschließlich seine eigenen Repertoires. Die API war bereits pro-Nutzer abgesichert." },
    { en: "Repertoire overview: Hint banner pointing to the matching browser extension/userscript (RepCheck), which shows deviations from the repertoire directly on chess.com and lichess.org — with a link to the help.", de: "Repertoire-Übersicht: Hinweis-Banner auf die passende Browser-Erweiterung/das Userscript (RepCheck), das Abweichungen vom Repertoire direkt auf chess.com und lichess.org anzeigt — mit Link in die Hilfe." },
    { en: "Help page: new \"Browser extension & userscript (RepCheck)\" section with setup (extension token), variants (signed Firefox extension or Tampermonkey userscript), and installation link. The deep link /help#extension scrolls straight to the section.", de: "Hilfeseite: neuer Abschnitt „Browser-Erweiterung & Userscript (RepCheck)\" mit Einrichtung (Extension-Token), Varianten (signierte Firefox-Erweiterung bzw. Tampermonkey-Userscript) und Installationslink. Deep-Link /help#extension scrollt direkt zum Abschnitt." },
  ]},
  { version: "0.101.1", date: "2026-06-10", changes: [
    { en: "Endless mode: \"Try again\" now also works when you fail by losing your last life — you may retry the puzzle on which the run ended (previously the button did nothing on the last life). If you solve it, the run continues in sudden death; otherwise \"Continue\" leads to game over as before.", de: "Endlos-Modus: „Nochmal versuchen\" funktioniert jetzt auch, wenn man beim Verlust des letzten Lebens scheitert — man darf das Puzzle, an dem der Run endete, erneut probieren (vorher tat der Button beim letzten Leben nichts). Löst man es, geht der Run im Sudden-Death weiter; sonst führt „Weiter\" wie gehabt ins Game Over." },
  ]},
  { version: "0.101.0", date: "2026-06-10", changes: [
    { en: "Statistics: New \"Weakest topics\" card shows the 5 topics with the lowest solve rate (from 3 attempts, highlighted in red) — complementing the existing topics overview.", de: "Statistik: Neue Karte „Schwächste Themen\" zeigt die 5 Themen mit der niedrigsten Lösungsquote (ab 3 Versuchen, rot hervorgehoben) — ergänzend zur bestehenden Themen-Übersicht." },
    { en: "Statistics: The Elo curve is additionally smoothed slightly (moving average over the Elo series), which dampens outliers and makes the curve look calmer; the axis values still show the actual min/max.", de: "Statistik: Die Elo-Kurve wird zusätzlich leicht geglättet (gleitender Mittelwert über die Elo-Reihe), wodurch Ausreißer gedämpft werden und der Verlauf ruhiger wirkt; die Achsenwerte zeigen weiterhin das tatsächliche Min/Max." },
  ]},
  { version: "0.100.2", date: "2026-06-09", changes: [
    { en: "Internal docs: TODO \"Migrate chess bot to Elasticsearch\" marked as done (implemented in the bot repo v2.60.0/2.60.1) — no app change.", de: "Interne Doku: TODO „Schach-Bot auf Elasticsearch umbauen\" als erledigt markiert (umgesetzt im Bot-Repo v2.60.0/2.60.1) — keine App-Änderung." },
  ]},
  { version: "0.100.1", date: "2026-06-09", changes: [
    { en: "Logging dashboards (Kibana, Dev + Prod): The \"Errors & Warnings\" panel now shows errors and warnings separately as two labeled tiles (each with its own counter) instead of a single combined total.", de: "Logging-Dashboards (Kibana, Dev + Prod): Das Panel „Errors & Warnings\" zeigt Fehler und Warnungen jetzt getrennt als zwei beschriftete Kacheln (je eigener Zähler) statt einer kombinierten Gesamtzahl." },
  ]},
  { version: "0.100.0", date: "2026-06-09", changes: [
    { en: "App installation reworked: The \"Install app\" entry no longer opens a dialog but a dedicated installation page (/install) with two paths — Android APK (download + instructions) and installation as a web app (PWA). Both variants show the appropriate path depending on the system; if one is not possible (e.g. APK on Mac/iOS, direct PWA installation in unsupported browsers), this is noted on the spot. On iPhone/iPad there is the \"Add to Home Screen\" guide; if the app is already installed, that is shown.", de: "App-Installation überarbeitet: Der „App installieren\"-Eintrag öffnet keinen Dialog mehr, sondern eine eigene Installationsseite (/install) mit zwei Wegen — Android-APK (Download + Anleitung) und Installation als Web-App (PWA). Beide Varianten zeigen je nach System den passenden Weg; ist eine nicht möglich (z. B. APK auf Mac/iOS, PWA-Direktinstallation in nicht unterstützten Browsern), wird das an Ort und Stelle vermerkt. Auf iPhone/iPad gibt es die „Zum Home-Bildschirm\"-Anleitung, läuft die App bereits installiert, wird das angezeigt." },
  ]},
  { version: "0.99.1", date: "2026-06-09", changes: [
    { en: "Endless mode: the manual topic field is now OR-linked (e.g. \"fork pin\" delivers puzzles with fork OR pin) — previously all topics had to occur at once, which yielded hardly any hits.", de: "Endlos-Modus: das manuelle Themenfeld ist jetzt ODER-verknüpft (z. B. „fork pin\" liefert Puzzles mit fork ODER pin) — vorher mussten alle Themen gleichzeitig vorkommen, was kaum Treffer ergab." },
  ]},
  { version: "0.99.0", date: "2026-06-09", changes: [
    { en: "New detailed help page (/help): explains all areas (account, profile, Discord, friends, tournaments, puzzles, endless, daily puzzle, courses, weekly post, training goals, statistics, analysis, repertoires, offline/app, theme/language, API tokens, privacy, feedback) with a table of contents. Linked in the navbar (help icon + account menu) and footer; localized in de/en/hr (other languages fall back to en).", de: "Neue ausführliche Hilfeseite (/help): erklärt alle Bereiche (Konto, Profil, Discord, Freunde, Turniere, Puzzles, Endlos, Tagespuzzle, Kurse, Wochenpost, Trainingsziele, Statistik, Analyse, Repertoires, Offline/App, Design/Sprache, API-Tokens, Datenschutz, Feedback) mit Inhaltsverzeichnis. Verlinkt in Navbar (Hilfe-Icon + Konto-Menü) und Footer; lokalisiert in de/en/hr (übrige Sprachen fallen auf en zurück)." },
  ]},
  { version: "0.98.9", date: "2026-06-09", changes: [
    { en: "Revert of 0.98.8 (readiness check of the tag index): the completeness query was reset back to the simple variant.", de: "Revert von 0.98.8 (Bereitschafts-Prüfung des Tag-Index): die Vollständigkeits-Abfrage wurde wieder auf die einfache Variante zurückgesetzt." },
  ]},
  { version: "0.98.7", date: "2026-06-09", changes: [
    { en: "Daily puzzle → Discord: The webhook to the chess bot now sends the solve time per solver (timeSeconds) — the bot shows it in the \"Solved by\" line (was empty before, because the time was not transmitted).", de: "Tagespuzzle → Discord: Der Webhook an den Schach-Bot sendet jetzt die Lösungszeit pro Löser (timeSeconds) — der Bot zeigt sie in der „Gelöst von\"-Zeile an (war bisher leer, weil die Zeit nicht übertragen wurde)." },
  ]},
  { version: "0.98.6", date: "2026-06-09", changes: [
    { en: "Admin: new \"Puzzles\" tab with a \"Build tag table\" button — triggers the one-time backfill of the topic tag table (instead of calling the API by hand).", de: "Admin: neuer „Puzzles\"-Tab mit Button „Tag-Tabelle aufbauen\" — stößt den einmaligen Backfill der Themen-Tag-Tabelle an (statt API-Aufruf von Hand)." },
  ]},
  { version: "0.98.5", date: "2026-06-09", changes: [
    { en: "Performance: The topic filter (\"train weakest topics\") now uses a normalized, indexed tag table (Tags + PuzzleTags) instead of a LIKE full scan — puzzle selection is thus index-backed and fast.", de: "Performance: Themen-Filter („schwächste Themen trainieren\") nutzt jetzt eine normalisierte, indizierte Tag-Tabelle (Tags + PuzzleTags) statt LIKE-Full-Scan — Puzzle-Auswahl ist damit indexgestützt und schnell." },
    { en: "Admin: a one-time backfill of the tag table is required after deploy (POST /api/admin/puzzles/backfill-tags); afterwards the import maintains the tags automatically.", de: "Admin: einmaliger Backfill der Tag-Tabelle nach Deploy nötig (POST /api/admin/puzzles/backfill-tags); danach pflegt der Import die Tags automatisch." },
  ]},
  { version: "0.98.4", date: "2026-06-09", changes: [
    { en: "\"Train weakest topics\": After a solved puzzle, the matching topic (the one the solved puzzle carries) is highlighted green in the filter bar — in puzzle and endless mode.", de: "„Schwächste Themen trainieren\": Nach gelöstem Puzzle wird das passende Thema (das das gelöste Puzzle trägt) in der Filterleiste grün hervorgehoben — in Puzzle- und Endless-Modus." },
  ]},
  { version: "0.98.3", date: "2026-06-09", changes: [
    { en: "Internal docs: note on the local dotnet path + InMemory test gap in CLAUDE.md (no app change).", de: "Interne Doku: Hinweis zum lokalen dotnet-Pfad + InMemory-Test-Lücke in CLAUDE.md (keine App-Änderung)." },
  ]},
  { version: "0.98.2", date: "2026-06-09", changes: [
    { en: "\"Train weakest topics\": On activation, the 5 topics are now written visibly into the topic field (endless), and the active topic filter is shown as chips during solving/the run (both modes).", de: "„Schwächste Themen trainieren\": Beim Aktivieren werden die 5 Themen jetzt sichtbar ins Themenfeld geschrieben (Endless), und der aktive Themenfilter wird während des Lösens/Runs als Chips angezeigt (beide Modi)." },
    { en: "Performance: topic-filtered puzzle selection massively sped up — instead of several full scans per call (in the endless batch ×number of windows), now a single scan over the rating span (the first puzzle no longer takes ~30 s to load).", de: "Performance: Theme-gefilterte Puzzle-Auswahl massiv beschleunigt — statt mehrerer Full-Scans pro Aufruf (im Endless-Batch ×Fensteranzahl) jetzt ein einziger Scan über die Rating-Spanne (erstes Puzzle lädt nicht mehr ~30 s)." },
  ]},
  { version: "0.98.1", date: "2026-06-09", changes: [
    { en: "Fix: \"Train 5 weakest topics\" loaded no puzzle (puzzle + endless mode). Cause: the OR topic filter produced a LIKE predicate that MySQL could not translate (EF.Functions as a constant instead of a static property access).", de: "Fix: „5 schwächste Themen trainieren\" lud kein Puzzle (Puzzle- + Endless-Modus). Ursache: der ODER-Themenfilter erzeugte ein von MySQL nicht übersetzbares LIKE-Prädikat (EF.Functions als Konstante statt statischem Property-Zugriff)." },
  ]},
  { version: "0.98.0", date: "2026-06-09", changes: [
    { en: "New: \"Train 5 weakest topics\" — in standard puzzle mode (settings) and in endless mode (configuration). Pulls puzzles specifically for the topics you solve worst (lowest solve rate).", de: "Neu: „5 schwächste Themen trainieren\" — im Standard-Puzzle-Modus (Einstellungen) und im Endless-Modus (Konfiguration). Zieht gezielt Puzzles zu den Themen, die du am schlechtesten löst (niedrigste Lösungsquote)." },
    { en: "Backend: OR topic filter (themesAny) for puzzle selection + endpoint /api/puzzles/stats/worst-themes.", de: "Backend: ODER-Themenfilter (themesAny) für die Puzzle-Auswahl + Endpoint /api/puzzles/stats/worst-themes." },
  ]},
  { version: "0.97.18", date: "2026-06-09", changes: [
    { en: "Tests: 11 outdated unit tests adapted to the current behavior (AnalysisComponent engine mock extended with engineFatalError$/destroy; resign tests now correctly expect FAILED instead of SOLVED; reviewLastPuzzle-from-param). Whole suite green again (258/258).", de: "Tests: 11 veraltete Unit-Tests an das aktuelle Verhalten angepasst (AnalysisComponent-Engine-Mock um engineFatalError$/destroy ergänzt; Aufgeben-Tests erwarten jetzt korrekt FAILED statt SOLVED; reviewLastPuzzle-from-Param). Gesamte Suite wieder grün (258/258)." },
  ]},
  { version: "0.97.17", date: "2026-06-09", changes: [
    { en: "Test infrastructure: karma.conf.js automatically finds the Puppeteer headless shell — ng test now runs without system-wide Chrome and without root.", de: "Test-Infrastruktur: karma.conf.js findet automatisch die Puppeteer-Headless-Shell — ng test läuft jetzt ohne system-weites Chrome und ohne root." },
  ]},
  { version: "0.97.16", date: "2026-06-09", changes: [
    { en: "Player statistics: Elo history curve is smoothed (Bézier instead of jagged polyline) — applies to single- and all-level view.", de: "Spielerstatistik: Elo-Verlaufskurve wird geglättet (Bézier statt zackiger Polyline) — gilt für Einzel- und Alle-Level-Ansicht." },
    { en: "Player statistics: The history view starts by default at the visualization level currently set for the puzzles.", de: "Spielerstatistik: Die Verlaufs-Ansicht startet standardmäßig auf der aktuell bei den Puzzles eingestellten Visualisierungsstufe." },
    { en: "Player statistics: \"By topic\" is sorted in descending order by solve %.", de: "Spielerstatistik: „Nach Thema\" wird absteigend nach Lösungs-% sortiert." },
    { en: "Player statistics: Fixes that the bars in \"Solved by rating\" were all the same height (bar height is now scaled correctly).", de: "Spielerstatistik: Behebt, dass die Balken bei „Nach Rating gelöst\" alle gleich hoch waren (Balkenhöhe wird jetzt korrekt skaliert)." },
  ]},
  { version: "0.97.15", date: "2026-06-09", changes: [
    { en: "Logging: the raw forwarding chain (X-Forwarded-For + X-Real-IP) is additionally captured as a diagnostic field so that all proxy hops are visible.", de: "Logging: rohe Weiterleitungs-Kette (X-Forwarded-For + X-Real-IP) wird zusätzlich als Diagnosefeld erfasst, damit alle Proxy-Hops sichtbar sind." },
  ]},
  { version: "0.97.14", date: "2026-06-09", changes: [
    { en: "Logging/rate limiter now capture the real client IP instead of the Docker gateway IP (X-Forwarded-For unwound across both proxy hops).", de: "Logging/Rate-Limiter erfassen jetzt die echte Client-IP statt der Docker-Gateway-IP (X-Forwarded-For über beide Proxy-Hops zurückgerollt)." },
  ]},
  { version: "0.97.13", date: "2026-06-08", changes: [
    { en: "Puzzle settings: difficulty levels now have an (i) info panel describing the Elo offsets.", de: "Puzzle-Einstellungen: Schwierigkeitsstufen haben jetzt ein (i)-Info-Panel mit Beschreibung der Elo-Offsets." },
    { en: "Puzzle: On a difficulty change, it automatically jumps to the next puzzle in the new range.", de: "Puzzle: Bei Schwierigkeitsänderung wird automatisch zum nächsten Puzzle im neuen Bereich gesprungen." },
  ]},
  { version: "0.97.12", date: "2026-06-08", changes: [
    { en: "Security: Swagger UI only available in the development build (not in production).", de: "Security: Swagger-UI nur noch im Development-Build verfügbar (nicht in Production)." },
    { en: "Security: RememberMe token lifetime reduced from 20 years to 1 year.", de: "Security: RememberMe-Token-Laufzeit von 20 Jahren auf 1 Jahr reduziert." },
  ]},
  { version: "0.97.11", date: "2026-06-08", changes: [
    { en: "StockfishService: removed dead ternary in mate eval (both branches were identical).", de: "StockfishService: toter Ternary bei Mate-Eval entfernt (beide Zweige waren identisch)." },
    { en: "EndlessProgressService: BulkImport now carries over Seed and ChainPuzzleIds from the DTO (were silently discarded before).", de: "EndlessProgressService: BulkImport überträgt jetzt Seed und ChainPuzzleIds aus dem DTO (wurden bisher stillschweigend verworfen)." },
  ]},
  { version: "0.97.10", date: "2026-06-08", changes: [
    { en: "Analysis: engine crash detection improved — after 3 failed auto-recovery attempts, an error banner with a reload-page button is shown instead of silently hanging.", de: "Analyse: Engine-Crash-Erkennung verbessert — nach 3 fehlgeschlagenen Auto-Recovery-Versuchen wird ein Fehlerbanner mit Seite-neu-laden-Button angezeigt statt lautlos zu hängen." },
    { en: "Analysis: crashStreak is reset when leaving the page, so that a renewed visit guarantees a clean restart.", de: "Analyse: crashStreak wird beim Verlassen der Seite zurückgesetzt, sodass ein erneuter Besuch einen sauberen Neustart garantiert." },
    { en: "StockfishService: worker.onerror now passes the error message to the telemetry hook.", de: "StockfishService: worker.onerror übergibt jetzt die Fehlermeldung an den Telemetrie-Hook." },
  ]},
  { version: "0.97.9", date: "2026-06-08", changes: [
    { en: "RoundMonitorService: SaveChanges now per iteration instead of once at the end — on an error in later entries, earlier updates are no longer lost.", de: "RoundMonitorService: SaveChanges jetzt pro Iteration statt einmalig am Ende — bei Fehler in späteren Einträgen gehen frühere Updates nicht mehr verloren." },
  ]},
  { version: "0.97.8", date: "2026-06-08", changes: [
    { en: "Puzzle: RecordAttemptAsync with idempotency (30s window) and Elo guard — only the first attempt per puzzle counts for Elo, further attempts are stored but do not change the rating.", de: "Puzzle: RecordAttemptAsync mit Idempotenz (30s-Fenster) und Elo-Guard — nur der erste Versuch pro Puzzle zählt für Elo, weitere Versuche werden gespeichert aber verändern die Wertung nicht." },
  ]},
  { version: "0.97.7", date: "2026-06-08", changes: [
    { en: "Crawler: CrawlJob no longer stays permanently stuck on \"Queued\" on a queue-full error — the job is set to \"Failed\" immediately.", de: "Crawler: CrawlJob bleibt bei Queue-voll-Fehler nicht mehr dauerhaft auf \"Queued\" hängen — Job wird sofort auf \"Failed\" gesetzt." },
  ]},
  { version: "0.97.6", date: "2026-06-08", changes: [
    { en: "Book puzzle: a load error now shows an error message with a retry button instead of an endless spinner.", de: "Buchpuzzle: Ladefehler zeigt jetzt Fehlermeldung mit Retry-Button statt endlosem Spinner." },
  ]},
  { version: "0.97.5", date: "2026-06-08", changes: [
    { en: "Analysis: engine hang after a puzzle→analysis switch fixed — the worker is cleanly terminated when leaving the analysis page.", de: "Analyse: Engine-Hang nach Puzzle→Analyse-Wechsel behoben — Worker wird beim Verlassen der Analyseseite sauber terminiert." },
  ]},
  { version: "0.97.4", date: "2026-06-08", changes: [
    { en: "Endless: After \"Analyze\" on the last lost heart, when returning you correctly land on the summary screen instead of the home page.", de: "Endless: Nach \"Analysieren\" beim letzten verlorenen Herz landet man beim Zurückkehren korrekt im Zusammenfassungs-Screen statt auf der Startseite." },
  ]},
  { version: "0.97.3", date: "2026-06-08", changes: [
    { en: "Endless: Only the first failure per puzzle costs a heart — further resigning or resetting on the same puzzle is free.", de: "Endless: Nur der erste Fehlschlag pro Puzzle kostet ein Herz — weiteres Aufgeben oder Zurücksetzen beim selben Puzzle ist kostenlos." },
  ]},
  { version: "0.97.2", date: "2026-06-07", changes: [
    { en: "Dark mode: All hardcoded gray tones in the frontend switched to color-mix(currentColor) — navbar, PGN viewer, weekly list, share dialogs, profile token box, app install dialog fully compatible.", de: "Dark Mode: Alle hardkodierten Grautöne im Frontend auf color-mix(currentColor) umgestellt — Navbar, PGN-Viewer, Weekly-Liste, Share-Dialoge, Profil-Token-Box, App-Install-Dialog vollständig kompatibel." },
  ]},
  { version: "0.97.1", date: "2026-06-07", changes: [
    { en: "Profile: theme selection (System/Light/Dark) visible as a button toggle directly in the profile.", de: "Profil: Theme-Auswahl (System/Hell/Dunkel) als Button-Toggle direkt im Profil sichtbar." },
  ]},
  { version: "0.97.0", date: "2026-06-07", changes: [
    { en: "Dark mode: New theme toggle in the navbar (sun/moon/auto icon). Default: system setting (prefers-color-scheme). A manual choice is stored in the browser.", de: "Dark Mode: Neuer Theme-Toggle in der Navbar (Sonne/Mond/Auto-Icon). Default: System-Einstellung (prefers-color-scheme). Manuelle Wahl wird im Browser gespeichert." },
  ]},
  { version: "0.96.23", date: "2026-06-07", changes: [
    { en: "Puzzle: move text under the board now larger and black for better contrast.", de: "Puzzle: Zugtext unter dem Brett jetzt größer und schwarz für besseren Kontrast." },
    { en: "Puzzle: reset and mouse-slip buttons appear from the first move, even when you are on the correct solution branch.", de: "Puzzle: Reset- und Mausslip-Schaltfläche erscheinen ab dem ersten Zug, auch wenn man im richtigen Lösungszweig ist." },
    { en: "Puzzle/Endless/Book: mouse-slip now also works on the solution path (takes back the last correct move).", de: "Puzzle/Endless/Buch: Mausslip funktioniert jetzt auch auf dem Lösungspfad (nimmt den letzten korrekten Zug zurück)." },
    { en: "Fix: \"Review last puzzle\" returns the current (new) puzzle after the review, not the reviewed one.", de: "Fix: „Letztes Puzzle reviewen\" gibt nach dem Review das aktuelle (neue) Puzzle zurück, nicht das reviewte." },
  ]},
  { version: "0.96.22", date: "2026-06-07", changes: [
    { en: "Fix: The Kibana dashboard link in the admin now points to the correct dashboard — Prod to rookhub-prod-dashboard, Dev to rookhub-dev-dashboard (instead of the non-existent ID rookhub-logging-dashboard).", de: "Fix: Kibana-Dashboard-Link im Admin zeigt jetzt auf das korrekte Dashboard — Prod auf rookhub-prod-dashboard, Dev auf rookhub-dev-dashboard (statt der nicht existierenden ID rookhub-logging-dashboard)." },
  ]},
  { version: "0.96.21", date: "2026-06-07", changes: [
    { en: "Repertoires: the name and category of existing repertoires can now be changed via an edit button.", de: "Repertoires: Name und Kategorie bestehender Repertoires können jetzt über einen Bearbeiten-Button geändert werden." },
  ]},
  { version: "0.96.20", date: "2026-06-07", changes: [
    { en: "API: JsonStringEnumConverter enabled – extension token connection no longer returns a 400 on POST analyze-game.", de: "API: JsonStringEnumConverter aktiviert – Extension-Token-Verbindung gibt kein 400 mehr bei POST analyze-game." },
  ]},
  { version: "0.96.19", date: "2026-06-07", changes: [
    { en: "Endless: The game-over list now also shows the puzzles that were played on another tab (multi-tab resume).", de: "Endless: Game-Over-Liste zeigt jetzt auch die Puzzles, die auf einem anderen Tab gespielt wurden (Multi-Tab-Resume)." },
  ]},
  { version: "0.96.18", date: "2026-06-07", changes: [
    { en: "Infra: Kibana init now creates two dashboards (Prod + Dev) with identical layout. ES index format configurable via Elasticsearch:IndexFormat.", de: "Infra: Kibana-Init erstellt jetzt zwei Dashboards (Prod + Dev) mit identischem Layout. ES-Index-Format konfigurierbar über Elasticsearch:IndexFormat." },
  ]},
  { version: "0.96.17", date: "2026-06-07", changes: [
    { en: "Extension API: new endpoint POST /api/extension/analyze-game. The Repcheck userscript/extension (≥ v1.6.0) now sends the move list of the current game and the server returns ply-by-ply annotations — the repertoire PGN no longer leaves the server. Server-side position-set cache (15 min / 5 min sliding), invalidated on PGN upload/delete.", de: "Extension-API: neuer Endpoint POST /api/extension/analyze-game. Repcheck-Userscript/Extension (≥ v1.6.0) schickt jetzt die Zugliste der aktuellen Partie und der Server liefert ply-weise Annotationen zurück — das Repertoire-PGN verlässt den Server nicht mehr. Server-seitiger Positions-Set-Cache (15 min / 5 min sliding), invalidiert bei PGN-Upload/-Delete." },
  ]},
  { version: "0.96.16", date: "2026-06-07", changes: [
    { en: "Admin: regenerating the daily puzzle sends a webhook to the bot — it posts the new puzzle in Discord and leaves a note in the old thread.", de: "Admin: Tagespuzzle neu generieren schickt Webhook an den Bot — dieser postet das neue Puzzle in Discord und hinterlässt im alten Thread einen Hinweis." },
  ]},
  { version: "0.96.15", date: "2026-06-07", changes: [
    { en: "Daily puzzle/claim session: If the puzzle was already solved while logged in, the anonymous entry is now correctly deleted on login instead of remaining as a duplicate.", de: "Tagespuzzle/Claim-Session: War das Puzzle bereits eingeloggt gelöst, wird der anonyme Eintrag beim Login jetzt korrekt gelöscht statt als Duplikat stehen zu bleiben." },
  ]},
  { version: "0.96.14", date: "2026-06-07", changes: [
    { en: "Daily puzzle: Anonymous solutions are automatically assigned to the account on the next login.", de: "Tagespuzzle: Anonyme Lösungen werden beim nächsten Login automatisch dem Account zugeordnet." },
  ]},
  { version: "0.96.13", date: "2026-06-07", changes: [
    { en: "Auth: Normal login valid for 30 days, \"Stay logged in\" practically unlimited (20 years).", de: "Auth: Normaler Login 30 Tage gültig, „Eingeloggt bleiben\" praktisch unbegrenzt (20 Jahre)." },
  ]},
  { version: "0.96.12", date: "2026-06-07", changes: [
    { en: "Auth: Session token validity increased from 1 day to 48 hours.", de: "Auth: Session-Token-Gültigkeit von 1 Tag auf 48 Stunden erhöht." },
  ]},
  { version: "0.96.11", date: "2026-06-06", changes: [
    { en: "Endless: Per-puzzle timer (elapsedSeconds) is now passed to the status card and shown after solving (like Standard/Book).", de: "Endless: Per-Puzzle-Timer (elapsedSeconds) wird jetzt an die Status-Card übergeben und nach dem Lösen angezeigt (wie Standard/Book)." },
    { en: "Standard: \"Review last puzzle\" button directly after the status card (like Endless), no longer below the stats card.", de: "Standard: „Letztes Puzzle reviewen\"-Button direkt nach der Status-Card (wie Endless), nicht mehr unter der Stats-Card." },
    { en: "Standard + Endless: Viz countdown text uses an i18n key instead of hardcoded \"s…\" (like Book).", de: "Standard + Endless: Viz-Countdown-Text nutzt i18n-Key statt hardcoded \"s…\" (wie Book)." },
  ]},
  { version: "0.96.10", date: "2026-06-06", changes: [
    { en: "Standard puzzle: Panel order adjusted — Your Turn at the top, then rating card, then stats (like Endless).", de: "Standard-Puzzle: Panel-Reihenfolge angepasst — Your Turn oben, dann Rating-Card, dann Stats (wie Endless)." },
  ]},
  { version: "0.96.9", date: "2026-06-06", changes: [
    { en: "Refactor: Unified PuzzleStatusCardComponent for all three puzzle modes (Standard/Endless/Book). Encapsulates gear button + the entire state switch (LOADING/SETUP/YourTurn/SOLVED/FAILED) — same look and same features in all modes.", de: "Refactor: Einheitliche PuzzleStatusCardComponent für alle drei Puzzle-Modi (Standard/Endless/Book). Kapselt Zahnrad-Button + gesamten State-Switch (LOADING/SETUP/YourTurn/SOLVED/FAILED) — gleiche Optik und gleiche Features in allen Modi." },
    { en: "Standard: Give Up now leads to the FAILED state (instead of SOLVED), like in Endless/Book. Retry, Analyze and next-puzzle button are visible in FAILED.", de: "Standard: Aufgeben (Give Up) führt jetzt wie in Endless/Book zum FAILED-Zustand (statt SOLVED). Retry, Analysieren und Nächstes-Puzzle-Button sind in FAILED sichtbar." },
    { en: "Book: Analyze button moved from the lower actions into the status card (SOLVED + FAILED). Next-puzzle button also in FAILED (failedNext() method).", de: "Book: Analysieren-Button aus den unteren Aktionen in die Status-Card verschoben (SOLVED + FAILED). Nächstes-Puzzle-Button auch in FAILED (failedNext()-Methode)." },
  ]},
  { version: "0.96.8", date: "2026-06-06", changes: [
    { en: "Settings dialog: (i) button next to \"Visualization\" expands a description list of all 5 modes; the actively selected mode is highlighted.", de: "Einstellungs-Dialog: (i)-Button neben „Visualisierung\" klappt Beschreibungsliste aller 5 Modi auf; aktiv gewählter Modus ist hervorgehoben." },
  ]},
  { version: "0.96.7", date: "2026-06-06", changes: [
    { en: "Puzzle settings: The gear button now opens a modal dialog (PuzzleSettingsDialogComponent) with piece/board preview, dropdown selection for piece set, board design, mode, visualization, opponent-move arrow and mode-specific options (difficulty + skip solved for Standard; Stockfish depth for Standard/Book). Available in all three modes (Standard, Endless, Book).", de: "Puzzle-Einstellungen: Gear-Button öffnet jetzt einen Modal-Dialog (PuzzleSettingsDialogComponent) mit Figuren/Brett-Vorschau, Dropdown-Auswahl für Figuren-Set, Brett-Design, Modus, Visualisierung, Gegnerzug-Pfeil und modus-spezifischen Optionen (Schwierigkeit + Gelöste überspringen für Standard; Stockfish-Tiefe für Standard/Book). In allen drei Modi (Standard, Endless, Book) verfügbar." },
    { en: "Refactor: Inline settings panels (filter-card, theme-card, config-card) removed from all three puzzle templates; ThemePickerComponent removed from the individual imports of the three components.", de: "Refactor: Inline-Settings-Panels (filter-card, theme-card, config-card) aus allen drei Puzzle-Template entfernt; ThemePickerComponent aus den Einzel-Importen der drei Komponenten entfernt." },
  ]},
  { version: "0.96.6", date: "2026-06-06", changes: [
    { en: "Endless: Settings gear button moved into the status card (was previously visible as a free-standing icon between the status card and rating card).", de: "Endless: Settings-Gear-Button in die Status-Card verschoben (war vorher als freies Icon zwischen Status-Card und Rating-Card sichtbar)." },
  ]},
  { version: "0.96.5", date: "2026-06-06", changes: [
    { en: "Refactor: \"Your turn!\" panel as a standalone PuzzleYourTurnComponent (Standard/Endless/Book modes use the same component, i18n via mode input).", de: "Refactor: „Your turn!\"-Panel als eigenständige PuzzleYourTurnComponent (Standard/Endless/Book-Modi nutzen dieselbe Komponente, i18n per Mode-Input)." },
    { en: "Refactor: Puzzle rating card as a standalone PuzzleRatingCardComponent (Standard + Endless; shows rating, level range, tags and share button).", de: "Refactor: Puzzle-Rating-Card als eigenständige PuzzleRatingCardComponent (Standard + Endless; zeigt Rating, Level-Range, Tags und Share-Button)." },
    { en: "Standard puzzle: Share button now in the rating card directly above the status card (was previously separated below it).", de: "Standard-Puzzle: Share-Button jetzt in der Rating-Card direkt über der Status-Card (war vorher getrennt darunter)." },
  ]},
  { version: "0.96.4", date: "2026-06-06", changes: [
    { en: "Puzzle modes: Tags/themes are hidden by default in all three modes (Standard/Endless/Book), a new \"Show tags\" link expands them.", de: "Puzzle-Modi: Tags/Themes sind in allen drei Modi (Standard/Endless/Book) standardmäßig ausgeblendet, neuer „Tags anzeigen\"-Link klappt sie auf." },
    { en: "Refactor: New reusable PuzzleTagsComponent (instead of tag-toggle markup copied 3×); CSS duplicates removed in the puzzle SCSS files.", de: "Refactor: neue wiederverwendbare PuzzleTagsComponent (statt 3× kopiertes Tag-Toggle-Markup); CSS-Doubletten in den Puzzle-SCSS-Dateien entfernt." },
  ]},
  { version: "0.96.3", date: "2026-06-06", changes: [
    { en: "Book puzzle: Empty gray settings card under \"Share puzzle\" hidden as long as the settings panel is not open (config-card now entirely in @if).", de: "Book-Puzzle: Leere graue Settings-Card unter \"Share puzzle\" ausgeblendet, solange das Einstellungs-Panel nicht offen ist (config-card jetzt komplett im @if)." },
  ]},
  { version: "0.96.2", date: "2026-06-06", changes: [
    { en: "Build: anyComponentStyle budget raised to 8 kB warning / 12 kB error (endless-puzzle.scss was 6 bytes over the old 10 kB limit → CI build failed).", de: "Build: anyComponentStyle-Budget auf 8 kB Warning / 12 kB Error angehoben (endless-puzzle.scss lag 6 Bytes über dem alten 10-kB-Limit → CI-Build schlug fehl)." },
  ]},
  { version: "0.96.1", date: "2026-06-06", changes: [
    { en: "UI: Unified layout across all puzzle modes (Book/Weekly/Course/Standard/Endless) — context card at the top, status card in the middle, share button at the bottom, viz text directly below the board.", de: "UI: Einheitliches Layout über alle Puzzle-Modi (Book/Weekly/Kurs/Standard/Endless) — Kontext-Card oben, Status-Card mittig, Share-Button unten, Viz-Text direkt unter dem Brett." },
    { en: "Book puzzle: Separate Weekly/Course card merged with the puzzle info card into a single context card at the top.", de: "Book-Puzzle: Separate Weekly-/Kurs-Card mit Puzzle-Info-Card zur einzigen Kontext-Card oben zusammengeführt." },
    { en: "Viz text is no longer shown as a card in the right column, but as plain text directly below the chessboard.", de: "Viz-Text wird nicht mehr als Card in der rechten Spalte, sondern als plain Text direkt unter dem Schachbrett angezeigt." },
  ]},
  { version: "0.96.0", date: "2026-06-06", changes: [
    { en: "Daily puzzle: Solve time per solver in the Discord embed (e.g. \"@Anna (1:23)\").", de: "Tagespuzzle: Lösungszeit je Solver im Discord-Embed (z. B. „@Anna (1:23)\")." },
  ]},
  { version: "0.95.14", date: "2026-06-06", changes: [
    { en: "Weekly: Post title in the header chip instead of a generic \"Weekly post\" label (saves a line).", de: "Weekly: Post-Titel im Header-Chip statt generischem \"Wochenpost\"-Label (spart eine Zeile)." },
    { en: "Viz card: Reworked — level badge instead of full-text title, no more monospace, blue accent bar for moves.", de: "Viz-Card: Überarbeitet — Level-Badge statt Volltext-Titel, kein Monospace mehr, blauer Akzentbalken für Züge." },
    { en: "\"Give up\" button visually dimmed (no warning red, smaller font) — clear hierarchy relative to \"Show eval\".", de: "\"Aufgeben\"-Button visuell abgedimmt (kein Warn-Rot, kleinere Schrift) — klare Hierarchie zu \"Eval zeigen\"." },
  ]},
  { version: "0.95.13", date: "2026-06-06", changes: [
    { en: "Profile: Change-password function (current + new password with confirmation).", de: "Profil: Passwort-Ändern-Funktion (aktuelles + neues Passwort mit Bestätigung)." },
  ]},
  { version: "0.95.12", date: "2026-06-06", changes: [
    { en: "Endless: Bugfix — after Give Up + Analyze, resuming landed you back on the same given-up puzzle and you lost a second life for it.", de: "Endless: Bugfix — nach Aufgeben + Analysieren landete man beim Fortsetzen wieder auf demselben aufgegebenen Puzzle und verlor ein zweites Leben dafür." },
  ]},
  { version: "0.95.11", date: "2026-06-06", changes: [
    { en: "Endless: Curve thresholds adjusted — T1 after puzzle 10 (was 5), T2 after puzzle 25 (was 20).", de: "Endless: Kurven-Thresholds angepasst — T1 nach Puzzle 10 (war 5), T2 nach Puzzle 25 (war 20)." },
  ]},
  { version: "0.95.10", date: "2026-06-06", changes: [
    { en: "Statistics: \"Show Eval\" clicks (EvalShown) and viz show-button clicks (VizShowCount) are now stored per puzzle attempt.", de: "Statistiken: \"Show Eval\"-Klicks (EvalShown) und Viz-Show-Button-Klicks (VizShowCount) werden pro Puzzle-Versuch mitgespeichert." },
  ]},
  { version: "0.95.9", date: "2026-06-06", changes: [
    { en: "Viz settings: Checkbox \"Opponent-move arrow\" (only visible at levels 1–4, setting stored locally); arrow timer shortened from 3s to 1s.", de: "Viz-Einstellungen: Checkbox \"Gegnerzug-Pfeil\" (nur bei Level 1–4 sichtbar, Einstellung wird lokal gespeichert); Pfeil-Timer von 3s auf 1s verkürzt." },
  ]},
  { version: "0.95.8", date: "2026-06-06", changes: [
    { en: "Visualization slider replaced by radio chips (Normal / Blindfold / Checker / Dark / Invisible) — in all three puzzle modes.", de: "Visualisierungs-Slider durch Radio-Chips ersetzt (Normal / Blindfold / Checker / Dark / Invisible) — in allen drei Puzzle-Modi." },
  ]},
  { version: "0.95.7", date: "2026-06-06", changes: [
    { en: "Visualization mode: Opponent moves shown in bold in the move list.", de: "Visualisierungsmodus: Gegnerzüge in der Zugliste fett dargestellt." },
  ]},
  { version: "0.95.6", date: "2026-06-06", changes: [
    { en: "Visualization mode: Opponent-move arrow only for follow-up moves (not the first setup move); the arrow fades out automatically after 3 seconds.", de: "Visualisierungsmodus: Gegnerzug-Pfeil nur für Folgezüge (nicht den ersten Setup-Zug); Pfeil blendet sich nach 3 Sekunden automatisch aus." },
  ]},
  { version: "0.95.5", date: "2026-06-06", changes: [
    { en: "Visualization modes 1–4: The last opponent move is shown as a red arrow on the frozen board (via chessground setAutoShapes, independent of the selection ring).", de: "Visualisierungsmodus 1–4: Letzter Gegnerzug wird als roter Pfeil auf dem eingefrorenen Brett angezeigt (via chessground setAutoShapes, unabhängig vom Auswahl-Ring)." },
  ]},
  { version: "0.95.4", date: "2026-06-06", changes: [
    { en: "Weekly posts are not visible to non-admins before their publication date (scheduledAt) — the API returns 404 (list, detail, puzzles, results, progress, attempt).", de: "Wochenposts sind vor ihrem Erscheinungstermin (scheduledAt) für Nicht-Admins nicht sichtbar — API gibt 404 zurück (Liste, Detail, Puzzles, Ergebnisse, Fortschritt, Versuch)." },
  ]},
  { version: "0.95.3", date: "2026-06-06", changes: [
    { en: "Docs: Added a mandatory rule to `CLAUDE.md` — `git pull` MUST run before every file edit. Background: when working in parallel on multiple clones, an edit against a stale state leads to merge conflicts and lost work.", de: "Doku: `CLAUDE.md` um eine Pflicht-Regel ergänzt — vor jedem Datei-Edit MUSS `git pull` laufen. Hintergrund: bei Parallel-Arbeit auf mehreren Klonen führt ein Edit gegen veralteten Stand zu Merge-Konflikten und verlorener Arbeit." },
  ]},
  { version: "0.95.2", date: "2026-06-06", changes: [
    { en: "Docs: `CLAUDE.md` tidied up — endpoints/schema brought up to date.", de: "Doku: `CLAUDE.md` aufgeräumt — Endpoints/Schema auf aktuellen Stand gebracht." },
  ]},
  { version: "0.95.1", date: "2026-06-06", changes: [
    { en: "Fix: After \"Give Up → Analyze → Back\" the same puzzle was reloaded instead of jumping to the next one. The analysis \"Back\" link now points to the general puzzle page instead of the specific puzzle ID.", de: "Fix: Nach „Aufgeben → Analysieren → Zurück\" wurde dasselbe Puzzle neu geladen statt zum nächsten zu springen. Der Analyse-„Zurück\"-Link zeigt jetzt auf die allgemeine Puzzle-Seite statt auf die konkrete Puzzle-ID." },
  ]},
  { version: "0.95.0", date: "2026-06-05", changes: [
    { en: "Weekly post leaderboard: In the overview, a leaderboard can be expanded per entry — sorted by accuracy (solved out of all puzzles of the post), ties broken by the faster total time. Per person: rank, solved/total + %, time.", de: "Wochenpost-Bestenliste: in der Übersicht lässt sich je Eintrag eine Bestenliste aufklappen — sortiert nach Genauigkeit (gelöst von allen Puzzles des Posts), bei Gleichstand zählt die schnellere Gesamtzeit. Pro Person: Platz, gelöst/gesamt + %, Zeit." },
  ]},
  { version: "0.94.1", date: "2026-06-05", changes: [
    { en: "Internal: Weekly post attempts are now logged in a structured way to Kibana (observability).", de: "Intern: Wochenpost-Versuche werden jetzt strukturiert nach Kibana geloggt (Observability)." },
  ]},
  { version: "0.94.0", date: "2026-06-05", changes: [
    { en: "Weekly post now also shows the total time in RookHub: in the overview behind your progress (⏱) and while playing through above the board — the sum of your time across all played puzzles of the post.", de: "Wochenpost zeigt jetzt auch in RookHub die Gesamtzeit an: in der Übersicht hinter deinem Fortschritt (⏱) und beim Durchspielen über dem Brett — die Summe deiner Zeit über alle gespielten Puzzles des Posts." },
  ]},
  { version: "0.93.0", date: "2026-06-05", changes: [
    { en: "The chess bot now shows live progress in the weekly post thread on Discord: who has completed the post, solved/total per person and the total time — updates automatically as soon as someone plays (new endpoint + webhook to the bot).", de: "Der Schach-Bot zeigt jetzt im Wochenpost-Thread auf Discord live den Fortschritt: wer den Post erledigt hat, gelöst/gesamt pro Person und die Gesamtzeit — aktualisiert sich automatisch, sobald jemand spielt (neuer Endpoint + Webhook an den Bot)." },
  ]},
  { version: "0.92.0", date: "2026-06-05", changes: [
    { en: "Playing through a weekly post: If you already have progress, playing through now starts directly at the first puzzle you haven't played yet — and once you've done them all, from the beginning again.", de: "Wochenpost durchspielen: Wenn du schon Fortschritt hast, startet das Durchspielen jetzt direkt beim ersten Puzzle, das du noch nicht gespielt hast — und wenn du alle durch hast, wieder von vorne." },
  ]},
  { version: "0.91.1", date: "2026-06-05", changes: [
    { en: "Weekly post: When you press \"Give up\" on a puzzle or reset it after a move, it now counts as ✗ (played, not solved) — previously only a solved puzzle or one lost by a wrong move was counted.", de: "Wochenpost: Wenn du bei einem Puzzle „Aufgeben\" drückst oder es nach einem Zug zurücksetzt, zählt es jetzt als ✗ (gespielt, nicht gelöst) — vorher wurde nur ein gelöstes oder per Fehlzug verlorenes Puzzle gewertet." },
  ]},
  { version: "0.91.0", date: "2026-06-05", changes: [
    { en: "Weekly post overview now shows your progress: behind each entry it shows solved/not-solved (✓/✗) and what % of it you've already played.", de: "Wochenpost-Übersicht zeigt jetzt deinen Fortschritt: hinter jedem Eintrag steht gelöst/nicht-gelöst (✓/✗) und wie viel % du davon schon gespielt hast." },
  ]},
  { version: "0.90.1", date: "2026-06-05", changes: [
    { en: "Anyone who opens a link to a login-required page (e.g. a weekly post) without being logged in now sees a note on the login page \"Please log in or register to continue\" — and lands directly on the desired page after logging in.", de: "Wer einen Link auf eine Login-pflichtige Seite öffnet (z. B. einen Wochenpost) und nicht eingeloggt ist, sieht auf der Login-Seite jetzt einen Hinweis „Bitte logge dich ein oder registriere dich, um fortzufahren\" — und landet nach dem Login direkt auf der gewünschten Seite." },
  ]},
  { version: "0.90.0", date: "2026-06-05", changes: [
    { en: "Weekly post moves to RookHub: the weekly post can now be played through here while logged in (previously admin only), and your progress is remembered. Every played puzzle counts — whether solved or not; once all are played, the weekly post counts as completed. The page shows \"X/Y played · Z solved\". (The chess bot will in future only announce new weekly posts with a link here, instead of managing them itself.)", de: "Wochenpost zieht auf RookHub: der Wochenpost ist jetzt hier eingeloggt durchspielbar (vorher nur Admin), und dein Fortschritt wird gemerkt. Jedes gespielte Puzzle zählt — egal ob gelöst oder nicht; sind alle gespielt, gilt der Wochenpost als erledigt. Die Seite zeigt „X/Y gespielt · Z gelöst\". (Der Schach-Bot kündigt neue Wochenposts künftig nur noch mit Link hier an, statt sie selbst zu verwalten.)" },
  ]},
  { version: "0.89.0", date: "2026-06-05", changes: [
    { en: "Admin: Regenerate the daily puzzle. The admin area has a new \"Daily puzzle\" tab — choose a date, view the current puzzle and re-roll it with a button. The link/date stays the same, only the puzzle behind it changes. The previously drawn puzzle is retired and never drawn again as a daily, random or blindfold puzzle.", de: "Admin: Tagespuzzle neu generieren. Im Admin-Bereich gibt es einen neuen Tab „Tagespuzzle\" — Datum wählen, aktuelles Puzzle ansehen und per Knopf neu auswürfeln. Der Link/das Datum bleibt gleich, nur das Puzzle dahinter wechselt. Das bisher gezogene Puzzle wird ausgemustert und nie wieder als Tages-, Zufalls- oder Blindpuzzle gezogen." },
  ]},
  { version: "0.88.0", date: "2026-06-05", changes: [
    { en: "The chess bot can now read your training progress for a daily, personal motivation DM: a new bot-internal endpoint `GET /api/bot/player-progress/{discordId}` returns today's training-goals status + the puzzle statistics for an account linked to Discord. Secured by a shared HMAC secret (`SchachBot:StatsSecret`), no user data without a valid signature. Nothing changes in the web interface — only the Discord bot uses this function.", de: "Der Schach-Bot kann jetzt deinen Trainings-Fortschritt für eine tägliche, persönliche Motivations-DM lesen: neuer bot-interner Endpoint `GET /api/bot/player-progress/{discordId}` liefert für ein mit Discord verknüpftes Konto den heutigen Trainingsziele-Stand + die Puzzle-Statistik. Abgesichert über ein geteiltes HMAC-Secret (`SchachBot:StatsSecret`), keine Nutzerdaten ohne gültige Signatur. Nichts an der Web-Oberfläche ändert sich — die Funktion nutzt ausschließlich der Discord-Bot." },
  ]},
  { version: "0.87.0", date: "2026-06-05", changes: [
    { en: "The email address at registration is now optional — anyone who doesn't want to provide one simply leaves the field empty. If one is provided, it must still have a valid format and be unique.", de: "Die E-Mail-Adresse bei der Registrierung ist jetzt optional — wer keine angeben möchte, lässt das Feld einfach leer. Wird eine angegeben, muss sie weiterhin ein gültiges Format haben und eindeutig sein." },
  ]},
  { version: "0.86.1", date: "2026-06-04", changes: [
    { en: "Android TWA: Dev variant added. Alongside the prod build against `rookhub.oberschmid.homes`, a dev build against `rookhub-dev.oberschmid.homes` can now be built in parallel — its own package id (`…rookhub.dev`), its own app name (\"RookHub Dev\"), installable alongside the prod app on the same phone. The CI workflow \"Build Android TWA\" now prompts for a `variant` selection (prod/dev) and uploads the artifact named accordingly. `assetlinks.json` lists both package ids with the same keystore fingerprint, so the dev build also launches without a browser address bar.", de: "Android-TWA: Dev-Variante hinzugefügt. Neben dem Prod-Build gegen `rookhub.oberschmid.homes` lässt sich jetzt parallel ein Dev-Build gegen `rookhub-dev.oberschmid.homes` bauen — eigene Package-Id (`…rookhub.dev`), eigener App-Name („RookHub Dev\"), parallel zur Prod-App auf demselben Telefon installierbar. CI-Workflow „Build Android TWA\" fragt jetzt eine `variant`-Auswahl (prod/dev) ab und lädt das Artefakt entsprechend benannt hoch. `assetlinks.json` listet beide Package-Ids mit demselben Keystore-Fingerprint, sodass ohne Browser-Adressleiste auch im Dev-Build gestartet wird." },
  ]},
  { version: "0.86.0", date: "2026-06-04", changes: [
    { en: "RookHub is now available as an Android app: in the account menu (profile picture top right), \"Install app (Android)\" leads to a short guide with a download link to the APK.", de: "RookHub gibt es jetzt als Android-App: im Konto-Menü (Profilbild oben rechts) führt „App installieren (Android)\" zu einer kurzen Anleitung mit Download-Link zur APK." },
    { en: "The download link always points to the latest published version.", de: "Der Download-Link zeigt immer auf die neueste veröffentlichte Version." },
  ]},
  { version: "0.85.6", date: "2026-06-04", changes: [
    { en: "Fix (viz mode, mobile): several attempts to make the selection visible via chessground brush or the \"selected\" highlight simply didn't come through on some phones — no marking at all on tap. We now draw the green selection ring as a separate DOM overlay directly above the board (absolutely positioned on the respective square, thick green border) — independent of chessground's SVG drawable, so that on truly every device it becomes visible which square was tapped. chessground's brush/highlight additionally stay active as a bonus.", de: "Fix (Viz-Modus, Mobile): mehrere Versuche, die Auswahl per chessground-Brush bzw. „selected\"-Highlight sichtbar zu kriegen, sind auf manchen Handys schlicht nicht angekommen — keinerlei Markierung beim Tap. Jetzt zeichnen wir den grünen Auswahl-Ring als eigenes DOM-Overlay direkt über dem Brett (absolute positioniert auf das jeweilige Feld, dicker grüner Border) — unabhängig von chessgrounds SVG-Drawable, damit auf wirklich jedem Gerät sichtbar wird, welches Feld angetippt wurde. chessgrounds Brush/Highlight bleiben zusätzlich als Bonus aktiv." },
  ]},
  { version: "0.85.5", date: "2026-06-04", changes: [
    { en: "Fix (localization): While cleaning up the \"Logs\" tab in 0.85.1, a superfluous comma was left behind in the three i18n files (en/de/hr) — this made the JSON files syntactically invalid, ngx-translate couldn't parse them and only showed the keys like \"dashboard.friends.manage\" instead of the texts. JSON repaired in all 3 languages; the remaining 22 languages were unchanged and fine.", de: "Fix (Lokalisierung): Beim Aufräumen des „Logs\"-Tabs in 0.85.1 ist in den drei i18n-Dateien (en/de/hr) ein überflüssiges Komma stehen geblieben — die JSON-Dateien waren dadurch syntaktisch ungültig, ngx-translate konnte sie nicht parsen und zeigte nur noch die Schlüssel wie „dashboard.friends.manage\" statt der Texte. JSON in allen 3 Sprachen repariert; die übrigen 22 Sprachen waren unverändert in Ordnung." },
  ]},
  { version: "0.85.4", date: "2026-06-04", changes: [
    { en: "Fix (viz mode, mobile): in 0.85.3 the yellowish \"selected\" marking on tap was removed and only a thick green circle was drawn — but on some phones this circle was no longer visible, so on tap NO marking was visible at all. Now back: yellowish square highlight AND green circle together; the circle is even thicker (stroke 22 instead of 18) so it stands out prominently on mobile and the highlight remains as a reliable fallback.", de: "Fix (Viz-Modus, Mobile): in 0.85.3 wurde die gelbliche „selected\"-Markierung beim Tap entfernt und nur noch ein dicker grüner Kreis gezeichnet — auf einigen Handys war dieser Kreis aber nicht mehr sichtbar, sodass beim Tap GAR keine Markierung mehr zu sehen war. Jetzt wieder: gelbliches Feld-Highlight UND grüner Kreis zusammen; der Kreis ist dabei nochmals dicker (Stroke 22 statt 18), damit er auf Mobile prominent steht und das Highlight als verlässliche Rückfallebene da bleibt." },
  ]},
  { version: "0.85.3", date: "2026-06-04", changes: [
    { en: "Visualization mode (mobile): A tap on a square now draws the same prominent green circle as a right-click on desktop — previously the selection on small boards was only marked by chessground's subtle yellowish \"selected\" highlight, which was barely visible under the finger. The circle stroke is thickened compared to the default brush (lineWidth 18 instead of 10) so it stands out large and clearly even on small phone boards.", de: "Visualisierungs-Modus (Mobile): Ein Tap auf ein Feld zeichnet jetzt denselben prominenten grünen Kreis wie ein Rechtsklick auf Desktop — vorher war die Auswahl auf kleinen Brettern nur durch das dezente gelbliche „selected\"-Highlight von chessground markiert, das unter dem Finger kaum zu sehen war. Der Kreis-Stroke ist gegenüber der Default-Brush verdickt (lineWidth 18 statt 10), damit er auch auf kleinen Handy-Brettern groß und gut sichtbar ist." },
  ]},
  { version: "0.85.2", date: "2026-06-04", changes: [
    { en: "Daily puzzle fairness: Anyone who makes a wrong move on the daily puzzle and presses \"Reset\" or \"Mouse slip\" — or gives up — counts as not-solved for that day, even if they then solve it after all. Previously a failed first attempt could be \"overwritten\" by a later solve, so the same day counted as both solved and not-solved at once. The server now decides by the first attempt per user; the Discord solver list shows the first attempt.", de: "Tagespuzzle-Fairness: Wer beim Tagespuzzle einen falschen Zug macht und „Zurücksetzen\" oder „Mausrutscher\" drückt — oder aufgibt — gilt für diesen Tag als nicht-gelöst, selbst wenn er es anschließend doch noch löst. Vorher konnte ein fehlgeschlagener erster Versuch durch einen späteren Solve „überschrieben\" werden, sodass derselbe Tag gleichzeitig als gelöst und nicht-gelöst zählte. Server entscheidet jetzt per erstem Versuch je User; Discord-Solver-Liste zeigt den ersten Versuch." },
  ]},
  { version: "0.85.1", date: "2026-06-04", changes: [
    { en: "Fix (admin): The Logs tab in the admin screen threw \"failed to load logs\" in prod, because the underlying `RequestLogs` table along with the `/api/request-logs` endpoint had long been removed (logs are in Elasticsearch/Kibana). The orphaned tab including filters, table, state and i18n keys was removed — the Kibana dashboard link in the header remains the only place to go for request logs.", de: "Fix (Admin): Der Logs-Tab im Admin-Screen warf in Prod „failed to load logs\", weil die zugrundeliegende `RequestLogs`-Tabelle samt `/api/request-logs`-Endpoint längst entfernt war (Logs liegen in Elasticsearch/Kibana). Der verwaiste Tab inkl. Filter, Tabelle, State und i18n-Keys wurde entfernt — der Kibana-Dashboard-Link im Header bleibt die einzige Anlaufstelle für Request-Logs." },
    { en: "Admin: \"Kibana dashboard\" link now opens the RookHub logging dashboard directly instead of just the Kibana root (deep link to `app/dashboards#/view/rookhub-logging-dashboard`). The env variable `KIBANA_URL` stays the root — the deep-link assembly happens server-side in `/api/admin/config`.", de: "Admin: „Kibana-Dashboard\"-Link öffnet jetzt direkt das RookHub-Logging-Dashboard statt nur die Kibana-Wurzel (Deep-Link auf `app/dashboards#/view/rookhub-logging-dashboard`). Env-Variable `KIBANA_URL` bleibt der Root — die Deep-Link-Zusammensetzung passiert serverseitig in `/api/admin/config`." },
  ]},
  { version: "0.85.0", date: "2026-06-04", changes: [
    { en: "Training goal \"Play\" changed: instead of play time in minutes/day, it now counts the number of Rapid/Classical games played per week.", de: "Trainingsziel „Spielen\" umgestellt: statt Spielzeit in Minuten/Tag zählt jetzt die Anzahl gespielter Rapid-/Classical-Partien pro Woche." },
    { en: "Counted are Rapid and Classical games on Lichess, and Rapid games on chess.com (chess.com has no separate \"Classical\" category). Bullet, Blitz and correspondence/daily do not count.", de: "Gezählt werden auf Lichess Rapid- und Classical-Partien, auf chess.com Rapid-Partien (chess.com hat keine eigene „Classical\"-Kategorie). Bullet, Blitz und Korrespondenz/Daily zählen nicht." },
    { en: "The play goal is thus a weekly goal and is shown separately (\"X / Y games this week\"); the daily star in the tracker is still based on puzzles and book/course.", de: "Das Spielen-Ziel ist damit ein Wochenziel und wird separat angezeigt („X / Y Partien diese Woche\"); der Tages-Stern im Tracker richtet sich weiterhin nach Puzzles und Buch/Kurs." },
    { en: "Note: Existing \"Play\" goals (in minutes) were reset and must be set anew as games per week; the play time recorded so far is rebuilt as a game count.", de: "Hinweis: Bestehende „Spielen\"-Ziele (in Minuten) wurden zurückgesetzt und müssen als Partien pro Woche neu gesetzt werden; die bisher erfasste Spielzeit wird neu als Partienzahl aufgebaut." },
  ]},
  { version: "0.84.2", date: "2026-06-04", changes: [
    { en: "Translations completed: the training-goals area (daily goals, goals tracker, coach template in the group management) is now also translated in the 22 additional languages — so all 25 languages are fully localized.", de: "Übersetzungen vervollständigt: der Trainingsziele-Bereich (Tagesziele, Ziele-Tracker, Coach-Vorlage in der Gruppenverwaltung) ist jetzt auch in den 22 zusätzlichen Sprachen übersetzt — damit sind alle 25 Sprachen vollständig lokalisiert." },
  ]},
  { version: "0.84.1", date: "2026-06-04", changes: [
    { en: "Localization completed: the most recently added interface texts (extension tokens, repertoire category, daily-puzzle navigation, \"Offline pool exhausted\") are now translated in all 25 languages — previously they fell back to English in the 22 additional languages.", de: "Lokalisierung vervollständigt: die zuletzt hinzugekommenen Oberflächen-Texte (Extension-Tokens, Repertoire-Kategorie, Tagespuzzle-Navigation, „Offline-Pool aufgebraucht\") sind jetzt in allen 25 Sprachen übersetzt — vorher fielen sie in den 22 zusätzlichen Sprachen auf Englisch zurück." },
  ]},
  { version: "0.84.0", date: "2026-06-04", changes: [
    { en: "New training goals mode (training support for students):", de: "Neuer Trainingsziele-Modus (Trainingsunterstützung für Schüler):" },
    { en: "Set your own daily goals per category — puzzles, book/course study, and playing (in minutes) — plus a weekly goal (on how many days per week all goals should be reached).", de: "Eigene Tagesziele je Kategorie festlegen — Puzzles, Buch-/Kursstudie und Spielen (in Minuten) — plus Wochenziel (an wie vielen Tagen pro Woche alle Ziele erreicht werden sollen)." },
    { en: "Coach/admin can set a goal template per group; students adopt it automatically and may adjust it for themselves (or reset it to the template).", de: "Coach/Admin kann pro Gruppe eine Ziel-Vorlage hinterlegen; Schüler übernehmen sie automatisch und dürfen sie für sich anpassen (oder auf die Vorlage zurücksetzen)." },
    { en: "Second activity tracker as a calendar heatmap: a star per day when all goals were reached, a partial symbol on partial fulfillment — in addition to the existing puzzle heatmap.", de: "Zweiter Aktivitäts-Tracker als Kalender-Heatmap: pro Tag ein Stern, wenn alle Ziele erreicht wurden, ein Teil-Symbol bei teilweiser Erfüllung — zusätzlich zur bestehenden Puzzle-Heatmap." },
    { en: "Playing time on Lichess/chess.com is captured automatically via the respective public API (linked username required) and counted toward the \"Playing\" category.", de: "Die Spielzeit auf Lichess/chess.com wird automatisch über die jeweilige öffentliche API erfasst (verknüpfter Benutzername vorausgesetzt) und in die Kategorie „Spielen\" eingerechnet." },
  ]},
  { version: "0.83.0", date: "2026-06-04", changes: [
    { en: "Google Play preparation & multilingual support (major update):", de: "Google-Play-Vorbereitung & Mehrsprachigkeit (großes Update):" },
    { en: "Real app icons (192/512 px + \"maskable\") + completed web app manifest → RookHub is a cleanly installable PWA.", de: "Echte App-Icons (192/512 px + „maskable\") + vervollständigtes Web-App-Manifest → RookHub ist eine sauber installierbare PWA." },
    { en: "Delete your own account (GDPR): Profile → \"Delete account\" with password confirmation; identity and personal data are removed, anonymized statistics are kept. New public page /account-deletion.", de: "Konto selbst löschen (DSGVO): Profil → „Konto löschen\" mit Passwort-Bestätigung; Identität und persönliche Daten werden entfernt, anonymisierte Statistiken bleiben erhalten. Neue öffentliche Seite /account-deletion." },
    { en: "New public legal pages: privacy policy (/privacy) and imprint (/impressum), linked on the login page.", de: "Neue öffentliche Rechtsseiten: Datenschutzerklärung (/privacy) und Impressum (/impressum), auf der Anmeldeseite verlinkt." },
    { en: "22 new languages — the app is now available in 25 languages (including Spanish, Chinese, Hindi, Arabic, Portuguese, French, Russian, Japanese, Korean …), including right-to-left rendering for Arabic and Persian.", de: "22 neue Sprachen — die App ist jetzt in 25 Sprachen verfügbar (u. a. Spanisch, Chinesisch, Hindi, Arabisch, Portugiesisch, Französisch, Russisch, Japanisch, Koreanisch …), inklusive Rechts-nach-links-Darstellung für Arabisch und Persisch." },
    { en: "Technical groundwork for the later Android app in the Google Play Store (Digital Asset Links, TWA build scaffolding) and central configuration of the imprint operator data.", de: "Technische Grundlagen für die spätere Android-App im Google Play Store (Digital Asset Links, TWA-Build-Gerüst) und zentrale Konfiguration der Impressum-Betreiberdaten." },
  ]},
  { version: "0.82.4", date: "2026-06-04", changes: [
    { en: "Android TWA: Digital Asset Links served at `/.well-known/assetlinks.json` with the app signature fingerprint — links the installed app to the domain so the TWA runs without the browser address bar (full-screen).", de: "Android-TWA: Digital Asset Links unter `/.well-known/assetlinks.json` mit dem App-Signatur-Fingerprint ausgeliefert — verknüpft die installierte App mit der Domain, sodass die TWA ohne Browser-Adressleiste (full-screen) läuft." },
  ]},
  { version: "0.82.3", date: "2026-06-04", changes: [
    { en: "Standard puzzle offline: When the pre-saved puzzle pool is used up (all played), you now see a clear note \"No more new offline puzzles\" with a button \"Play last puzzle again\" — instead of the unclear \"nothing saved\" screen or the silent repetition of the same puzzle. As soon as you are back online, the page automatically loads new puzzles.", de: "Standard-Puzzle Offline: Wenn der vorab gespeicherte Puzzle-Pool aufgebraucht ist (alle gespielt), sieht man jetzt einen klaren Hinweis „Keine neuen Offline-Puzzles mehr\" mit einem Button „Letztes Puzzle nochmal spielen\" — statt dem unklaren „nichts gespeichert\"-Bildschirm bzw. dem stillen Wiederholen desselben Puzzles. Sobald man wieder online ist, lädt die Seite automatisch neue Puzzles nach." },
  ]},
  { version: "0.82.2", date: "2026-06-04", changes: [
    { en: "Fix (mobile, visualization mode): When you tapped a square in viz mode (≥ level 1), the selection was barely visible on the phone — just a thin circle stroke that often disappeared under the finger. Now the tapped square is additionally highlighted full-area (chessground \"selected\", yellowish) and the green circle remains as a second accent. Chessground's own interaction is disabled in viz mode so the tap doesn't clear the selection again at the same time.", de: "Fix (Mobile, Visualisierungs-Modus): Wenn man im Viz-Modus (≥ Stufe 1) auf ein Feld tippte, war die Auswahl auf dem Handy kaum erkennbar — nur ein dünner Kreis-Stroke, der unter dem Finger oft verschwand. Jetzt wird das angetippte Feld zusätzlich vollflächig hervorgehoben (chessground-„selected\", gelblich) und der grüne Kreis bleibt als zweiter Akzent. Chessgrounds eigene Interaktion ist im Viz-Modus abgeschaltet, damit der Tap nicht gleichzeitig die Auswahl wieder löscht." },
  ]},
  { version: "0.82.1", date: "2026-06-04", changes: [
    { en: "Endless config screen: The \"Continue / Archive run + restart / Start\" block now sits right at the top — immediately visible without first scrolling through start rating, thresholds, visualization, etc. The config fields remain below it for fine-tuning before the next run.", de: "Endlos-Config-Screen: „Weiterspielen / Lauf archivieren + Neu starten / Start\"-Block sitzt jetzt ganz oben — sofort sichtbar ohne erst durch Start-Rating, Schwellenwerte, Visualisierung etc. zu scrollen. Die Config-Felder bleiben darunter zum Feintunen vor dem nächsten Lauf." },
  ]},
  { version: "0.82.0", date: "2026-06-04", changes: [
    { en: "Endless: compact \"quick stats\" pill row directly below the board — puzzle rating, current level and hearts are now visible without scrolling even on small phone screens (previously this info was in the stats card below the visualization/status card).", de: "Endlos: kompakte „Quick-Stats\"-Pillen-Reihe direkt unter dem Brett — Puzzle-Rating, aktuelles Level und Herzen sind jetzt auch auf kleinen Handy-Screens ohne Scrollen sichtbar (vorher steckten diese Infos in der Stats-Karte unter Visualisierungs-/Status-Karte)." },
  ]},
  { version: "0.81.1", date: "2026-06-04", changes: [
    { en: "Fix (mobile, Endless): After solving, the \"Solved\" card below the board looked unusually narrow because the info section on mobile (flex column) took its width from the content instead of spanning 100%. Both sections (board + status card) are now set to full width in the mobile layout.", de: "Fix (Mobile, Endlos): Nach dem Lösen sah die „Gelöst\"-Karte unter dem Brett ungewohnt schmal aus, weil die Info-Sektion auf Mobile (flex-Spalte) ihre Breite vom Inhalt nahm statt 100 % zu spannen. Beide Sektionen (Brett + Status-Karte) werden jetzt im Mobile-Layout auf volle Breite gesetzt." },
  ]},
  { version: "0.81.0", date: "2026-06-03", changes: [
    { en: "Daily puzzle view (`/puzzles/daily/:date`): no more book navigation (\"Next/Random from book\"), but date navigation instead — \"Previous day\" always, \"Next day\" only when you are in the past (no future). Browser forward/back also works.", de: "Tagespuzzle-Ansicht (`/puzzles/daily/:date`): keine Buch-Navigation („Nächstes/Zufällig aus Buch\") mehr, sondern Datums-Navigation — „Voriger Tag\" immer, „Nächster Tag\" nur, wenn man in der Vergangenheit ist (keine Zukunft). Browser-Vor/Zurück funktionieren ebenfalls." },
    { en: "New quick access \"Daily puzzle\" on the dashboard puzzles card (next to Solve/Endless) → opens today's daily puzzle.", de: "Neuer Schnellzugriff „Tagespuzzle\" auf der Dashboard-Puzzles-Karte (neben Lösen/Endlos) → öffnet das heutige Tagespuzzle." },
  ]},
  { version: "0.80.1", date: "2026-06-03", changes: [
    { en: "Fix (critical): The API no longer started — during auth setup, the JWT handler collided with the policy scheme \"Bearer\" (both registered under the name \"Bearer\" → \"Scheme already exists: Bearer\", startup crash). The JWT handler now runs under the name \"Jwt\"; the policy scheme \"Bearer\" remains the default and routes depending on the token (`rkh_…` → ApiToken, otherwise → JWT). Fixes the API outage since the auth/API token feature.", de: "Fix (kritisch): Die API startete nicht mehr — beim Auth-Setup kollidierte der JWT-Handler mit dem Policy-Scheme „Bearer\" (beide unter dem Namen „Bearer\" registriert → „Scheme already exists: Bearer\", Startup-Crash). Der JWT-Handler läuft jetzt unter dem Namen „Jwt\"; das Policy-Scheme „Bearer\" bleibt der Default und leitet je nach Token (`rkh_…` → ApiToken, sonst → JWT) weiter. Behebt den API-Ausfall seit dem Auth-/API-Token-Feature." },
  ]},
  { version: "0.80.0", date: "2026-06-03", changes: [
    { en: "The daily puzzle is now reachable via a stable, date-based link: new route `/puzzles/daily/:date` (e.g. `/puzzles/daily/20260603`) loads the daily puzzle of the respective UTC date. From now on the chess bot links its daily puzzle posts there (instead of to the changing puzzle ID `/puzzles/book/{id}`) — the same link permanently points to that day's daily puzzle.", de: "Tagespuzzle ist jetzt über einen stabilen, datumsbasierten Link erreichbar: neue Route `/puzzles/daily/:date` (z.B. `/puzzles/daily/20260603`) lädt das Tagespuzzle des jeweiligen UTC-Datums. Der Schach-Bot verlinkt seine Tagespuzzle-Posts ab sofort dorthin (statt auf die wechselnde Puzzle-ID `/puzzles/book/{id}`) — derselbe Link zeigt dauerhaft auf das Tagespuzzle dieses Tages." },
  ]},
  { version: "0.79.1", date: "2026-06-03", changes: [
    { en: "Extension endpoint (`/api/extension/*`): scope guard — an API token may only use the endpoint when `scope=extension` (secures early separation of machine token permissions for future scopes). JWT users may continue as before. CORS policy `AllowCredentials` removed (auth runs strictly via Bearer token in the header, cookies not needed); CORS reduced to `GET`.", de: "Extension-Endpoint (`/api/extension/*`): Scope-Guard — ein API-Token darf den Endpoint nur nutzen, wenn `scope=extension` (sichert frühe Trennung von Maschinen-Token-Berechtigungen für künftige Scopes). JWT-User dürfen weiter wie bisher. CORS-Policy `AllowCredentials` raus (Auth läuft strikt über Bearer-Token im Header, Cookies nicht gebraucht); CORS auf `GET` reduziert." },
  ]},
  { version: "0.79.0", date: "2026-06-03", changes: [
    { en: "Extension tokens: \"Extension tokens\" section in the profile to create / revoke personal API tokens (rkh_… format, GitHub PAT style). When creating, the raw token is shown once in the modal with a copy button and warning — after that only the prefix is visible. Optional expiry (30/90/365 days or never). Backend endpoints `GET/POST/DELETE /api/profile/tokens`. Preparation for the chess.com Tampermonkey extension, which will access `/api/extension/*` via Bearer token (scope `extension`).", de: "Extension-Tokens: Sektion „Extension-Tokens\" im Profil zum Erstellen / Widerrufen persönlicher API-Tokens (rkh_…-Format, GitHub-PAT-Style). Beim Anlegen wird der Raw-Token einmalig im Modal mit Copy-Button und Warnhinweis gezeigt — danach nur noch der Prefix sichtbar. Optionaler Ablauf (30/90/365 Tage oder nie). Backend-Endpoints `GET/POST/DELETE /api/profile/tokens`. Vorbereitung für die chess.com-Tampermonkey-Erweiterung, die per Bearer-Token (Scope `extension`) auf `/api/extension/*` zugreifen wird." },
  ]},
  { version: "0.78.1", date: "2026-06-03", changes: [
    { en: "Repertoire category added: When creating, you now choose a category (Opening / Middlegame / Endgame / —). In the list, the category is shown as a colored chip next to the name. Backend field `Repertoire.Kind` (enum `None|Opening|Middlegame|Endgame`), migration `AddRepertoireKind`. Preparation for the chess.com extension, which will fetch only opening PGNs specifically via `GET /api/extension/repertoires?kind=opening`; the endpoint additionally returns `totalSizeBytes` per repertoire as a hint for client-side limits.", de: "Repertoire-Kategorie hinzu: Beim Anlegen wählst du jetzt eine Kategorie (Eröffnung / Mittelspiel / Endspiel / —). In der Liste wird die Kategorie als farbiger Chip neben dem Namen angezeigt. Backend-Feld `Repertoire.Kind` (Enum `None|Opening|Middlegame|Endgame`), Migration `AddRepertoireKind`. Vorbereitung für die chess.com-Extension, die per `GET /api/extension/repertoires?kind=opening` gezielt nur Eröffnungs-PGNs holen wird; der Endpoint liefert zusätzlich `totalSizeBytes` pro Repertoire als Hinweis für Client-side-Limits." },
  ]},
  { version: "0.78.0", date: "2026-06-03", changes: [
    { en: "Endless gauntlet: each run now gets a unique seed; seed + the ordered chain puzzle IDs are saved on the server at run end (new columns on the session). This means the exact chain of a run is permanently stored — the basis for a later replay.", de: "Endlos-Gauntlet: jeder Lauf bekommt jetzt einen eindeutigen Seed; Seed + die geordneten Ketten-Puzzle-IDs werden beim Lauf-Ende am Server gespeichert (neue Spalten auf der Session). Damit ist die exakte Kette eines Laufs dauerhaft hinterlegt — Grundlage für ein späteres Replay." },
  ]},
  { version: "0.77.0", date: "2026-06-03", changes: [
    { en: "Endless is now a \"gauntlet\": at the start the complete puzzle chain is generated and stored on your device. A page refresh or \"Continue\" therefore always shows exactly the same puzzle — the order is fixed from the beginning.", de: "Endlos ist jetzt ein „Gauntlet\": Beim Start wird die komplette Puzzle-Kette generiert und auf deinem Gerät abgelegt. Ein Seiten-Refresh oder „Fortsetzen\" zeigt deshalb immer exakt dasselbe Puzzle — die Reihenfolge steht von Anfang an fest." },
    { en: "Difficulty follows an approximately logarithmic curve: rises quickly up to the 1st threshold (avg first error, ~5 puzzles), then more gradually up to the 2nd threshold (avg maximum of your last 5 runs, ~20 puzzles), after that only slightly increasing.", de: "Die Schwierigkeit folgt einer annähernd logarithmischen Kurve: schnell hoch bis zum 1. Schwellenwert (Ø erster Fehler, ~5 Puzzles), dann gemächlicher bis zum 2. Schwellenwert (Ø Maximum deiner letzten 5 Läufe, ~20 Puzzles), danach nur noch leicht ansteigend." },
    { en: "Every puzzle moves you forward — whether solved or not: an error costs a life AND advances to the next (higher) puzzle. At 0 lives it's over.", de: "Jedes Puzzle führt weiter — egal ob gelöst oder nicht: Ein Fehler kostet ein Leben UND rückt zum nächsten (höheren) Puzzle. Bei 0 Leben ist Schluss." },
    { en: "Online, new puzzles are auto-generated at the chain end; offline at the end of the chain \"You win\" 🏆 appears.", de: "Online wird am Kettenende automatisch nachgeneriert; offline am Ende der Kette erscheint „You win\" 🏆." },
    { en: "Settings: both thresholds remain editable; the preview now shows the rating progression of the chain (puzzle 1 / 6 / 21 / 30).", de: "Einstellungen: beide Schwellenwerte bleiben editierbar; die Vorschau zeigt jetzt den Rating-Verlauf der Kette (Puzzle 1 / 6 / 21 / 30)." },
    { en: "The offline preloading of the Endless pool now uses the same chain curve as the live gauntlet (previously the old run logic).", de: "Das Offline-Vorabladen des Endlos-Pools nutzt jetzt dieselbe Ketten-Kurve wie der Live-Gauntlet (vorher die alte Lauf-Logik)." },
  ]},
  { version: "0.76.0", date: "2026-06-03", changes: [
    { en: "The daily puzzle is persisted: new endpoint `GET /api/book-puzzles/daily/{yyyyMMdd|today}` (anonymous) returns the daily puzzle for a UTC date. One row per day in the new table `DailyPuzzles` (PK=Date, FK→BookPuzzle); a background service `DailyPuzzleScheduler` creates today's assignment at 00:00 UTC + backfills missed days on startup. On-demand fallback if the scheduler was offline. `GetRandomAsync(pool=\"daily\")` now routes through the persisted assignment — historical daily puzzles thus stay stable, no matter how the `forDaily` pool later changes. Migration `AddDailyPuzzleTable`, book deletion clears history.", de: "Tagespuzzle wird persistiert: neuer Endpoint `GET /api/book-puzzles/daily/{yyyyMMdd|today}` (anonym) liefert das Tagespuzzle für ein UTC-Datum. Pro Tag eine Zeile in der neuen Tabelle `DailyPuzzles` (PK=Date, FK→BookPuzzle); ein Background-Service `DailyPuzzleScheduler` legt die heutige Zuordnung um 00:00 UTC an + holt verpasste Tage beim Start nach. On-Demand-Fallback wenn Scheduler offline war. `GetRandomAsync(pool=\"daily\")` routet jetzt durch die persistierte Zuordnung — historische Tagespuzzles bleiben damit stabil, egal wie der `forDaily`-Pool sich später ändert. Migration `AddDailyPuzzleTable`, Book-Löschung räumt Historie ab." },
  ]},
  { version: "0.75.0", date: "2026-06-03", changes: [
    { en: "Daily puzzle solver updates via webhook: after every book/daily puzzle attempt, the API fires an HMAC-signed POST to the chess bot — which updates the Discord post immediately instead of only after ≤5 minutes (previous polling model). New service `SchachBotWebhookService`, fire-and-forget via the existing `BackgroundTaskQueue`. Config: `SchachBot__WebhookUrl` (e.g. http://schach-bot:9000/webhook/puzzle-attempt) + `SchachBot__WebhookSecret` (must be identical to the bot `WEBHOOK_SECRET`). Both empty = webhook disabled. Compose files (dev / dev.vpn / vpn) pass through `SCHACH_BOT_WEBHOOK_URL` and `SCHACH_BOT_WEBHOOK_SECRET`.", de: "Tagespuzzle-Solver-Updates per Webhook: nach jedem Buch-/Tagespuzzle-Versuch feuert die API einen HMAC-signierten POST an den Schach-Bot — der aktualisiert den Discord-Post sofort statt erst nach ≤5 Minuten (vorheriges Polling-Modell). Neuer Service `SchachBotWebhookService`, fire-and-forget via vorhandene `BackgroundTaskQueue`. Konfig: `SchachBot__WebhookUrl` (z.B. http://schach-bot:9000/webhook/puzzle-attempt) + `SchachBot__WebhookSecret` (muss identisch zum Bot-`WEBHOOK_SECRET` sein). Beide leer = Webhook deaktiviert. Compose-Files (dev / dev.vpn / vpn) reichen `SCHACH_BOT_WEBHOOK_URL` und `SCHACH_BOT_WEBHOOK_SECRET` durch." },
  ]},
  { version: "0.74.0", date: "2026-06-03", changes: [
    { en: "Admin: Kibana dashboard link in the admin header (localized en/de/hr). Shown only when `KIBANA_URL` is set in the server env — read via new endpoint `GET /api/admin/config` (admin-only). Compose files pass `KIBANA_URL` to the API as `Kibana__Url`.", de: "Admin: Kibana-Dashboard-Link im Admin-Header (en/de/hr lokalisiert). Wird nur angezeigt, wenn `KIBANA_URL` im Server-Env gesetzt ist — gelesen via neuem Endpoint `GET /api/admin/config` (Admin-only). Compose-Files reichen `KIBANA_URL` an die API als `Kibana__Url`." },
  ]},
  { version: "0.73.2", date: "2026-06-03", changes: [
    { en: "Fix (Endless): After \"Play again\" an already finished run could wrongly be resumed again (resume → give up → again → resumable again). \"Play again\" now fully discards the finished run.", de: "Fix (Endlos): Nach „Nochmal spielen\" konnte ein bereits beendeter Run fälschlich erneut fortgesetzt werden (Resume → Aufgeben → Nochmal → wieder fortsetzbar). „Nochmal spielen\" verwirft den beendeten Run jetzt vollständig." },
  ]},
  { version: "0.73.1", date: "2026-06-03", changes: [
    { en: "Fix: The dashboard now shows your actual puzzle Elo — namely that of your most-played level — instead of always the Elo of the normal level (which stays at the default value 1500 with pure visualization/blindfold play). The statistics overview uses the same logic.", de: "Fix: Das Dashboard zeigt jetzt dein tatsächliches Puzzle-Elo — nämlich das deines meistgespielten Levels — statt immer das Elo des Normal-Levels (das bei reinem Visualisierungs-/Blindfold-Spiel auf dem Standardwert 1500 stehen bleibt). Die Statistik-Übersicht nutzt dieselbe Logik." },
  ]},
  { version: "0.73.0", date: "2026-06-03", changes: [
    { en: "New: \"Stay logged in\" option at login — keeps you logged in for 30 days (otherwise the login expires after 1 day).", de: "Neu: „Eingeloggt bleiben\"-Option beim Anmelden — hält dich 30 Tage angemeldet (sonst läuft die Anmeldung nach 1 Tag ab)." },
    { en: "Password requirement simplified: a password now only needs at least 4 characters (previously: 8 characters with upper/lowercase letter + digit).", de: "Passwort-Anforderung vereinfacht: ein Passwort braucht jetzt nur noch mindestens 4 Zeichen (vorher: 8 Zeichen mit Groß-/Kleinbuchstabe + Ziffer)." },
  ]},
  { version: "0.72.0", date: "2026-06-03", changes: [
    { en: "The three puzzle modes (Standard, Book/Daily puzzle, Endless) now behave uniformly:", de: "Die drei Puzzle-Modi (Standard, Buch/Tagespuzzle, Endlos) verhalten sich jetzt einheitlich:" },
    { en: "Giving up plays through the solution from the start in all modes (previously Endless mode only jumped to the end).", de: "Aufgeben spielt in allen Modi die Lösung von vorne durch (vorher sprang der Endlosmodus nur ans Ende)." },
    { en: "After solving, each mode automatically jumps to the next puzzle via a short, visible countdown — skippable anytime via \"Next\" (previously: Standard 3s, Endless immediately, Book not at all).", de: "Nach dem Lösen springt jeder Modus per kurzem, sichtbarem Countdown automatisch zum nächsten Puzzle — jederzeit per „Weiter\" überspringbar (vorher: Standard 3s, Endlos sofort, Buch gar nicht)." },
    { en: "Endless: After \"Analyze\" → \"Back\" it continues directly with the next puzzle of the current run instead of landing in the overview.", de: "Endlos: Nach „Analysieren\" → „Zurück\" geht es direkt beim nächsten Puzzle des laufenden Runs weiter statt in der Übersicht zu landen." },
    { en: "Endless: After a failed attempt there is now \"Retry\" (costs no additional life); settings (board/piece theme + visualization) are also reachable during play.", de: "Endlos: Nach einem Fehlversuch gibt es jetzt „Wiederholen\" (kostet kein weiteres Leben); Einstellungen (Brett-/Figurenthema + Visualisierung) sind auch während des Spiels erreichbar." },
    { en: "Book puzzle now has — like the other modes — the \"Show evaluation\" button (Stockfish assessment).", de: "Buch-Puzzle hat jetzt — wie die anderen Modi — den „Bewertung anzeigen\"-Knopf (Stockfish-Einschätzung)." },
    { en: "The \"mouse slip\" button and the remembered open state of the settings now behave the same in all modes.", de: "Der „Mausrutscher\"-Knopf und der gemerkte Offen-Zustand der Einstellungen verhalten sich nun in allen Modi gleich." },
    { en: "Under the hood: the shared solution/review/countdown logic is now centralized (less duplication), no functional change beyond that.", de: "Unter der Haube: die gemeinsame Lösungs-/Review-/Countdown-Logik liegt jetzt zentral (weniger Duplikat), keine Funktionsänderung darüber hinaus." },
  ]},
  { version: "0.71.1", date: "2026-06-03", changes: [
    { en: "Fix (operations): The Docker healthcheck of the frontend container now checks 127.0.0.1 instead of localhost — otherwise the container was wrongly reported as \"unhealthy\" (busybox-wget chose IPv6 ::1 for \"localhost\", where nginx doesn't listen). Pure healthcheck fix, no functional change to the app.", de: "Fix (Betrieb): Der Docker-Healthcheck des Frontend-Containers prüft jetzt 127.0.0.1 statt localhost — sonst wurde der Container fälschlich als „unhealthy\" gemeldet (busybox-wget wählte für „localhost\" IPv6 ::1, wo nginx nicht lauscht). Reiner Healthcheck-Fix, keine Funktionsänderung an der App." },
  ]},
  { version: "0.71.0", date: "2026-06-03", changes: [
    { en: "Internal code review/refactoring (no intentional functional change): the large puzzle/tournament/admin components were split into templates/styles + reusable building blocks, the notification messages (snackbars) centralized and the book/course/admin logic moved into dedicated services (leaner, more testable controllers). Improves maintainability; 223 frontend + 482 backend tests green.", de: "Internes Code-Review/Refactoring (keine bewusste Funktionsänderung): die großen Puzzle-/Turnier-/Admin-Komponenten wurden in Templates/Styles + wiederverwendbare Bausteine aufgeteilt, die Hinweis-Meldungen (Snackbars) zentralisiert und die Buch-/Kurs-/Admin-Logik in eigene Services ausgelagert (schlankere, testbarere Controller). Verbessert Wartbarkeit; 223 Frontend- + 482 Backend-Tests grün." },
    { en: "Fix: Deleting a book now also correctly removes the recorded daily/book puzzle attempts (previously it could fail on a database relationship).", de: "Fix: Das Löschen eines Buchs entfernt jetzt auch die aufgezeichneten Tagespuzzle-/Buch-Versuche korrekt (vorher konnte es an einer Datenbank-Beziehung scheitern)." },
    { en: "Fix: Anonymous Endless progress is stored uniquely per session (unique index + cleanup of any old duplicates).", de: "Fix: Anonymer Endless-Fortschritt wird je Sitzung eindeutig gespeichert (Unique-Index + Bereinigung etwaiger Alt-Duplikate)." },
    { en: "Minor: The board/piece selection in the settings gear now looks uniform across all puzzle modes (an accidental gray chip background in Standard/Endless mode is gone).", de: "Kleinigkeit: Die Brett-/Figuren-Auswahl im Einstellungs-Zahnrad sieht jetzt in allen Puzzle-Modi einheitlich aus (ein versehentlicher grauer Chip-Hintergrund im Standard-/Endlosmodus ist weg)." },
  ]},
  { version: "0.70.0", date: "2026-06-03", changes: [
    { en: "Puzzle buttons consistent (as in Endless mode): \"Give up\" is now available from the start — even on the book puzzle before the first move. \"Reset\" and \"Mouse slip\" only appear after a move has been made (previously they were already there at the start of the Standard puzzle, even though there was nothing to reset).", de: "Puzzle-Buttons konsistent (wie im Endlosmodus): „Aufgeben\" ist jetzt von Anfang an verfügbar — auch beim Buch-Puzzle schon vor dem ersten Zug. „Zurücksetzen\" und „Mausrutscher\" erscheinen erst, nachdem ein Zug gemacht wurde (vorher waren sie beim Standard-Puzzle schon am Anfang da, obwohl es nichts zurückzusetzen gab)." },
    { en: "The puzzle settings/filters (gear) now stay open when you switch to the next puzzle (the open state is remembered) — previously they collapsed.", de: "Die Puzzle-Einstellungen/Filter (Zahnrad) bleiben jetzt offen, wenn du zum nächsten Puzzle wechselst (der Offen-Zustand wird gemerkt) — vorher klappten sie zu." },
  ]},
  { version: "0.69.0", date: "2026-06-03", changes: [
    { en: "Even without login, solved book/daily puzzles now count: a book puzzle solved anonymously (not logged in) is captured via an anonymous session ID (new endpoint POST `/api/book-puzzles/{id}/attempt/anonymous`). In the daily puzzle display on Discord, logged-in solvers appear by name, anonymous ones as a count (\"+N anonymous\"). Logged-in solves remain as before (by name, via `/attempt`).", de: "Auch ohne Login zählen gelöste Buch-/Tagespuzzles jetzt mit: Ein anonym (nicht eingeloggt) gelöstes Buch-Puzzle wird über eine anonyme Sitzungs-ID erfasst (neuer Endpoint POST `/api/book-puzzles/{id}/attempt/anonymous`). In der Tagespuzzle-Anzeige auf Discord erscheinen eingeloggte Löser namentlich, anonyme als Anzahl („+N anonym\"). Eingeloggte Solves bleiben wie bisher (namentlich, via `/attempt`)." },
  ]},
  { version: "0.68.1", date: "2026-06-03", changes: [
    { en: "Fix: The client diagnostics endpoint now logs routine heartbeats (e.g. bot keep-alives) at Info instead of Warning — otherwise the regular heartbeats triggered a \"warn_spike\" false alarm in the log watcher. Real engine crash/hang messages remain at Warning.", de: "Fix: Client-Diagnose-Endpoint loggt Routine-Heartbeats (z.B. Bot-Lebenszeichen) jetzt auf Info statt Warning — sonst lösten die regelmäßigen Heartbeats im Log-Watcher einen „warn_spike\"-Fehlalarm aus. Echte Engine-Crash-/Hänger-Meldungen bleiben auf Warning." },
  ]},
  { version: "0.68.0", date: "2026-06-03", changes: [
    { en: "Monitoring/operations: The API now sends a \"heartbeat\" keep-alive to Elasticsearch every 60 s (including a short DB self-check, status healthy/degraded). This lets the log watcher detect a dead/hanging service by MISSING heartbeats — instead of not being able to distinguish silence from \"just quiet\". Additionally Docker healthchecks for API (/health), frontend and crawler (container restart on failure). (Interval configurable via `Heartbeat:IntervalSeconds`.)", de: "Überwachung/Betrieb: Die API sendet jetzt alle 60 s ein „Heartbeat\"-Lebenszeichen nach Elasticsearch (inkl. kurzem DB-Selbst-Check, Status healthy/degraded). Damit erkennt der Log-Watcher einen toten/hängenden Dienst an AUSBLEIBENDEN Heartbeats — statt Stille nicht von „gerade ruhig\" unterscheiden zu können. Zusätzlich Docker-Healthchecks für API (/health), Frontend und Crawler (Container-Neustart bei Fehler). (Intervall via `Heartbeat:IntervalSeconds` konfigurierbar.)" },
  ]},
  { version: "0.67.0", date: "2026-06-03", changes: [
    { en: "Kibana dashboard greatly expanded (in sections): Puzzle (accuracy, rating distribution, avg time/puzzle, attempts per viz level, top users by Elo), Endless (runs/day, avg solved puzzles, max rating distribution, leaderboard), Courses (solves/day, per book, per user) and Operations (feature usage by area, HTTP errors over time, slowest endpoints).", de: "Kibana-Dashboard stark erweitert (in Sektionen): Puzzle (Genauigkeit, Rating-Verteilung, Ø Zeit/Puzzle, Attempts je Viz-Level, Top-User nach Elo), Endless (Runs/Tag, Ø gelöste Puzzles, Max-Rating-Verteilung, Leaderboard), Kurse (Solves/Tag, je Buch, je User) und Betrieb (Feature-Nutzung nach Bereich, HTTP-Fehler über Zeit, langsamste Endpoints)." },
    { en: "New \"Unique Visits\" instead of the login panels: unique visitors (logged in via username, anonymous via session ID) as a metric + history per day. \"Recent Logs\" now shows timestamp / level / RequestPath / message / username.", de: "Neu „Unique Visits\" statt der Login-Panels: eindeutige Besucher (eingeloggt via Username, anonym via Session-Id) als Metrik + Verlauf je Tag. „Recent Logs\" zeigt jetzt Zeitstempel / Level / RequestPath / Message / Username." },
    { en: "API: new event EndlessSessionCompleted (Endless session summary: solved puzzles, max rating, duration) and a VisitorId on every request (username if logged in, otherwise anonymous session ID via new X-Visitor-Id header). The course metrics build on the existing CoursePuzzleAttempt log (0.63.0).", de: "API: neues Event EndlessSessionCompleted (Endless-Session-Zusammenfassung: gelöste Puzzles, Max-Rating, Dauer) und eine VisitorId auf jedem Request (Username wenn eingeloggt, sonst anonyme Session-Id via neuem X-Visitor-Id-Header). Die Kurs-Kennzahlen bauen auf dem bestehenden CoursePuzzleAttempt-Log (0.63.0) auf." },
    { en: "Dashboard deploy automated: kibana-init is now a built image (init-kibana.sh baked in) — dashboard updates come along automatically on `docker compose up -d --build`, without manual git pull / container recreate.", de: "Dashboard-Deploy automatisiert: kibana-init ist jetzt ein gebautes Image (init-kibana.sh eingebacken) — Dashboard-Updates wandern beim `docker compose up -d --build` automatisch mit, ohne manuelles git pull / Container-Recreate." },
  ]},
  { version: "0.66.1", date: "2026-06-02", changes: [
    { en: "Security/robustness (code review): Public profiles (by username) no longer reveal sensitive data — only display name + public chess IDs, NO real names/ChessResults ID/Discord link. Course solutions can no longer be lost on parallel saving. Discord linking now reports a collision cleanly (instead of a server error). The analysis engine now also detects a missing engine start in the initialization phase and restarts automatically (no more \"Calculating…\" hang). Endless puzzles solved offline are no longer lost (locally pre-noted + synced on reconnect). The offline fallback picks a puzzle with a matching rating instead of any. Several smaller hardenings (rate limiting behind proxy, log hygiene, more defensive server calls).", de: "Sicherheit/Robustheit (Code-Review): Öffentliche Profile (per Username) geben keine sensiblen Daten mehr preis — nur noch Anzeigename + öffentliche Schach-IDs, KEINE Klarnamen/ChessResults-ID/Discord-Verknüpfung. Kurs-Lösungen können bei parallelem Speichern nicht mehr verloren gehen. Discord-Verknüpfung meldet eine Kollision jetzt sauber (statt Serverfehler). Analyse-Engine erkennt einen ausbleibenden Engine-Start jetzt auch in der Initialisierungsphase und startet automatisch neu (kein „Berechne…\"-Festhängen mehr). Offline gelöste Endless-Puzzles werden nicht mehr verloren (lokal vorgemerkt + bei Reconnect synchronisiert). Offline-Fallback wählt ein Puzzle mit passendem Rating statt irgendeinem. Mehrere kleinere Härtungen (Rate-Limiting hinter Proxy, Log-Hygiene, defensivere Server-Aufrufe)." },
  ]},
  { version: "0.66.0", date: "2026-06-02", changes: [
    { en: "The chess engine (Stockfish) now also works offline: the engine files are pre-cached by the service worker so that analysis, evaluation display and eval bar run without internet. (The earlier \"Calculating…\" hang was not due to offline caching but to the command order to the engine and has been fixed since 0.64.2; should it still go slightly wrong, the self-healing + hang watchdog kicks in.)", de: "Schach-Engine (Stockfish) funktioniert jetzt auch offline: Die Engine-Dateien werden vom Service Worker vorab gecacht, sodass Analyse, Bewertungsanzeige und Eval-Bar ohne Internet laufen. (Der frühere „Berechne…\"-Hänger lag nicht am Offline-Caching, sondern an der Befehlsreihenfolge zur Engine und ist seit 0.64.2 behoben; greift dennoch etwas daneben, springt die Selbstheilung + Hänger-Watchdog ein.)" },
  ]},
  { version: "0.65.0", date: "2026-06-02", changes: [
    { en: "Engine diagnostics: browser Stockfish crashes and hangs are now detected and reported to the server (new endpoint POST /api/client-log → structured into Elasticsearch/Kibana). Captured are worker crash, failed engine initialization, aborted searches (timeout) and — in analysis mode — a \"hang\" watchdog (no computation results after start) including automatic restart. This way you can see in Kibana how often the engine causes problems for real users. (Messages throttled per type.)", de: "Engine-Diagnostik: Browser-Stockfish-Crashes und -Hänger werden jetzt erkannt und an den Server gemeldet (neuer Endpoint POST /api/client-log → strukturiert in Elasticsearch/Kibana). Erfasst werden Worker-Absturz, fehlgeschlagene Engine-Initialisierung, abgebrochene Suchen (Timeout) und – im Analyse-Modus – ein „Hänger\"-Watchdog (keine Berechnungs-Ergebnisse nach Start) inkl. automatischem Neustart. So sieht man in Kibana, wie oft die Engine bei echten Nutzern Probleme macht. (Meldungen pro Art gedrosselt.)" },
  ]},
  { version: "0.64.2", date: "2026-06-02", changes: [
    { en: "Analysis no longer hangs at \"Calculating …\": the Stockfish WASM is no longer loaded via the service worker cache (but directly) — a WASM served from the cache often prevented the engine worker from starting. Additionally clean UCI sequencing: a new position is only analyzed after the engine has confirmed it stopped the previous run (prevents swallowed searches). Note: engine analysis/eval now need a connection (solving puzzles/Endless offline remains unaffected).", de: "Analyse hängt nicht mehr bei „Berechne …\": Der Stockfish-WASM wird nicht mehr über den Service-Worker-Cache geladen (sondern direkt) — ein aus dem Cache serviertes WASM ließ den Engine-Worker oft nicht starten. Außerdem sauberes UCI-Sequencing: eine neue Stellung wird erst analysiert, nachdem die Engine den vorherigen Lauf bestätigt gestoppt hat (verhindert verschluckte Suchen). Hinweis: Engine-Analyse/Eval brauchen jetzt eine Verbindung (Puzzle-/Endless-Lösen offline bleibt unberührt)." },
  ]},
  { version: "0.64.1", date: "2026-06-02", changes: [
    { en: "Stockfish in the browser more stable: if the WASM worker crashes (e.g. memory), it is now automatically restarted instead of disabling the engine for the rest of the session. In analysis mode the current position is seamlessly resumed after a crash (with protection against endless restarts); the hash size is capped to prevent memory crashes during long analyses. A failed engine initialization is retried on the next attempt.", de: "Stockfish im Browser stabiler: Stürzt der WASM-Worker ab (z.B. Speicher), wird er jetzt automatisch neu gestartet, statt die Engine für die restliche Sitzung lahmzulegen. Im Analyse-Modus wird die aktuelle Stellung nach einem Absturz nahtlos wieder aufgenommen (mit Schutz gegen Endlos-Neustarts); die Hash-Größe ist begrenzt, um Speicher-Crashes bei langen Analysen vorzubeugen. Eine fehlgeschlagene Engine-Initialisierung wird beim nächsten Versuch erneut probiert." },
  ]},
  { version: "0.64.0", date: "2026-06-02", changes: [
    { en: "Offline pools are now preloaded at app start (as soon as online) – not only at the first opening of the mode. Standard puzzle (matching Elo + difficulty) and Endless run are cached in the background so that both modes can be started directly offline. Also topped up on reconnect; only fills what is still missing.", de: "Offline-Pools werden jetzt schon beim App-Start vorab geladen (sobald online) – nicht erst beim ersten Öffnen des Modus. Standard-Puzzle (passend zu Elo + Schwierigkeit) und Endless-Run werden im Hintergrund gecacht, sodass beide Modi direkt offline gestartet werden können. Wird auch bei Reconnect nachgezogen; füllt nur, was noch fehlt." },
  ]},
  { version: "0.63.0", date: "2026-06-02", changes: [
    { en: "Structured logging per puzzle (for Elasticsearch/Kibana): every solved/given-up puzzle is logged with start and solution time — across all modes (Standard puzzle, daily puzzle/book, course, Endless). In Endless mode the individual puzzles of a session are reported to the server with their times at session end (logging only, not persisted); course solutions now additionally send the time needed.", de: "Strukturiertes Logging pro Puzzle (für Elasticsearch/Kibana): jedes gelöste/aufgegebene Puzzle wird mit Start- und Lösungszeit geloggt — über alle Modi (Standard-Puzzle, Tagespuzzle/Buch, Kurs, Endless). Im Endless-Modus werden die einzelnen Puzzles einer Session mit ihren Zeiten beim Session-Ende an den Server gemeldet (nur Logging, nicht persistiert); Kurs-Lösungen senden jetzt zusätzlich die benötigte Zeit." },
  ]},
  { version: "0.62.0", date: "2026-06-02", changes: [
    { en: "Endless: After solving a puzzle it can now be analyzed — the button \"Analyze last puzzle\" stays visible (even after the next puzzle has automatically loaded) and opens the just-solved puzzle in analysis mode. When giving up, the solution screen additionally offers \"Analyze\" for the current puzzle. Back leads to Endless mode in each case (the running run can be continued).", de: "Endless: Nach dem Lösen eines Puzzles lässt es sich jetzt analysieren — der Button „Letztes Puzzle analysieren\" bleibt sichtbar (auch nachdem automatisch das nächste Puzzle geladen wurde) und öffnet das gerade gelöste Puzzle im Analysemodus. Beim Aufgeben gibt es im Lösungs-Screen zusätzlich „Analysieren\" für das aktuelle Puzzle. Zurück führt jeweils in den Endless-Modus (laufender Run lässt sich fortsetzen)." },
  ]},
  { version: "0.61.0", date: "2026-06-02", changes: [
    { en: "Real offline operation: the app now has a service worker and caches the app shell, all lazy modules (Puzzle, Endless …) and the translations. This lets you open and start puzzle and Endless mode even without a connection (provided it was loaded online once before) — no longer just continue playing. On a new version a \"Reload\" notice appears.", de: "Echter Offline-Betrieb: Die App hat jetzt einen Service Worker und cacht App-Shell, alle Lazy-Module (Puzzle, Endless …) und die Übersetzungen. Dadurch lassen sich Puzzle- und Endless-Modus auch ohne Verbindung öffnen und starten (sofern vorher einmal online geladen) — nicht mehr nur weiterspielen. Bei einer neuen Version erscheint ein „Neu laden\"-Hinweis." },
    { en: "Puzzles solved offline are no longer lost: solutions/attempts (Standard puzzle, daily puzzle, course puzzle and Endless sessions) are pre-noted locally offline and uploaded automatically as soon as a connection exists again. In the profile, \"Offline\" shows the number of solutions still pending.", de: "Offline gelöste Puzzles gehen nicht mehr verloren: Lösungen/Versuche (Standard-Puzzle, Tagespuzzle, Kurs-Puzzle und Endless-Sessions) werden offline lokal vorgemerkt und automatisch hochgeladen, sobald wieder eine Verbindung besteht. Im Profil zeigt „Offline\" die Anzahl noch wartender Lösungen." },
    { en: "Endless preloads a run already when opening the configuration (online), so that an offline start has data; without a cache there is now a clear note instead of \"Run ended\". The Standard puzzle mode also shows a clear note offline without a cache.", de: "Endless lädt schon beim Öffnen der Konfiguration einen Run vorab (online), damit ein Offline-Start Daten hat; ohne Cache gibt es jetzt einen klaren Hinweis statt „Run beendet\". Auch der Standard-Puzzle-Modus zeigt offline ohne Cache einen verständlichen Hinweis." },
  ]},
  { version: "0.60.0", date: "2026-06-02", changes: [
    { en: "Books offline: in the course list, each book can be saved offline via a cloud button (all puzzles cached locally) or removed again. If a book is saved offline, the direct opening of a book puzzle as well as \"Next in book\"/\"Random from book\" work from the cache without internet. Size + number of cached books are shown in the profile under \"Offline\" (with \"Clear cache\"). New endpoint GET `/api/courses/{bookId}/puzzles`.", de: "Bücher offline: In der Kurs-Liste lässt sich jedes Buch per Wolken-Button offline speichern (alle Puzzles lokal gecacht) bzw. wieder entfernen. Ist ein Buch offline gespeichert, funktionieren ohne Internet der Direktaufruf eines Buch-Puzzles sowie „Nächstes im Buch\"/„Zufällig aus Buch\" aus dem Cache. Größe + Anzahl gecachter Bücher stehen im Profil unter „Offline\" (mit „Cache leeren\"). Neuer Endpoint GET `/api/courses/{bookId}/puzzles`." },
  ]},
  { version: "0.59.0", date: "2026-06-02", changes: [
    { en: "Offline settings in the profile (section \"Offline\", per device): how many Standard puzzles (at the current difficulty) and how many Endless runs are kept offline (default 10 / 2), plus display of the cache size and \"Clear cache\" button.", de: "Offline-Einstellungen im Profil (Abschnitt „Offline\", pro Gerät): wie viele Standard-Puzzles (auf der aktuellen Schwierigkeit) und wie viele Endless-Runs offline vorgehalten werden (Standard 10 / 2), plus Anzeige der Cache-Größe und „Cache leeren\"-Button." },
    { en: "Standard puzzle mode offline: when there is no connection, preloaded puzzles from the local pool are played (refilled online; reset on difficulty change).", de: "Standard-Puzzle-Modus offline: bei fehlender Verbindung werden vorab geladene Puzzles aus dem lokalen Pool gespielt (wird online aufgefüllt; bei Schwierigkeitswechsel neu)." },
    { en: "Endless: multiple runs (default 2, adjustable) are now preloaded instead of just one.", de: "Endless: es werden jetzt mehrere Runs (Standard 2, einstellbar) vorab geladen statt nur einer." },
  ]},
  { version: "0.58.0", date: "2026-06-02", changes: [
    { en: "Foundation for the daily puzzle display: solution attempts on standalone book puzzles are captured for logged-in users (new endpoints POST `/api/book-puzzles/{id}/attempt` + GET `/api/book-puzzles/{id}/results` with solver list including Discord link). This lets the chess bot show who solved the daily puzzle.", de: "Grundlage Tagespuzzle-Anzeige: Lösungsversuche an Standalone-Buch-Puzzles werden für eingeloggte User erfasst (neue Endpoints POST `/api/book-puzzles/{id}/attempt` + GET `/api/book-puzzles/{id}/results` mit Solver-Liste inkl. Discord-Verknüpfung). Der Schach-Bot kann damit anzeigen, wer das Tagespuzzle gelöst hat." },
  ]},
  { version: "0.57.2", date: "2026-06-02", changes: [
    { en: "Fix (Endless): \"Unfinished run\" was shown with `0 lives` — the run was effectively over, but the active state ended up on the server with lives=0 shortly before `endGame()` and was remembered there as \"resumable\" when the user left the page. `loseLife()` and `resetPuzzle()` now write null at 0 lives instead of a zombie state; on load, legacy zombies (lives ≤ 0) are cleaned up locally and on the server; the banner is additionally guarded against lives ≤ 0.", de: "Fix (Endless): „Unfinished run\" wurde mit `0 lives` angezeigt — der Run war faktisch vorbei, aber der Active-State landete kurz vor `endGame()` mit lives=0 auf dem Server und wurde dort als „resumebar\" gemerkt, wenn der User die Seite verlies. `loseLife()` und `resetPuzzle()` schreiben jetzt bei 0 Lives null statt einen Zombie-State; beim Laden werden Legacy-Zombies (lives ≤ 0) lokal und am Server aufgeräumt; das Banner ist zusätzlich gegen lives ≤ 0 abgesichert." },
  ]},
  { version: "0.57.1", date: "2026-06-02", changes: [
    { en: "Fix: The EF Core warning \"FirstOrDefault without OrderBy\" is now fixed at the source — the random puzzle selection determines the ID range via deterministic Min/Max aggregates instead of via GroupBy+FirstOrDefault. The earlier log suppression for it has been removed again.", de: "Fix: Die EF-Core-Warnung „FirstOrDefault ohne OrderBy\" wird jetzt an der Quelle behoben — die zufällige Puzzle-Auswahl ermittelt den ID-Bereich über deterministische Min/Max-Aggregate statt über GroupBy+FirstOrDefault. Die frühere Log-Unterdrückung dafür wurde wieder entfernt." },
  ]},
  { version: "0.57.0", date: "2026-06-02", changes: [
    { en: "Book puzzle (standalone, `/puzzles/book/:id`): two new buttons \"Next in book\" (next puzzle in book order, back to the front at the end) and \"Random from book\". New endpoints GET `/api/book-puzzles/{id}/next` + `/api/book-puzzles/{id}/random`. In course/weekly post view, their own navigation continues.", de: "Buch-Puzzle (Standalone, `/puzzles/book/:id`): zwei neue Buttons „Nächstes im Buch\" (nächstes Puzzle in Buchreihenfolge, am Ende wieder vorne) und „Zufällig aus Buch\". Neue Endpoints GET `/api/book-puzzles/{id}/next` + `/api/book-puzzles/{id}/random`. In Kurs-/Wochenpost-Ansicht weiterhin deren eigene Navigation." },
  ]},
  { version: "0.56.0", date: "2026-06-02", changes: [
    { en: "Statistics \"All\": The Elo curves of all visualization modes are now overlaid in ONE chart (shared scale, color-coded per mode) with a small legend — instead of separate mini charts.", de: "Statistik „Alle\": Die Elo-Kurven aller Visualisierungs-Modi werden jetzt in EINER Grafik überlagert (gemeinsame Skala, je Modus farbkodiert) mit kleiner Legende — statt getrennter Mini-Charts." },
  ]},
  { version: "0.55.2", date: "2026-06-02", changes: [
    { en: "Feature: The book import response now reports three numbers instead of two — Imported / Duplicates / Invalid. Previously \"Skipped\" only counted duplicates; games that the parser discards due to missing Round/FEN/Mainline or starting position without a [%tqu] marker fell out silently. The snackbar only shows the non-zero parts.", de: "Feature: Buch-Import-Antwort meldet jetzt drei Zahlen statt zwei — Importiert / Duplikate / Ungültig. Bisher zählte „Skipped\" nur Duplikate; Spiele, die der Parser wegen fehlender Round/FEN/Mainline oder Grundstellung-ohne-[%tqu]-Marker verwirft, fielen heimlich raus. Snackbar zeigt nur die nicht-null Teile an." },
    { en: "API: BookImportItemDto + BookImportResultDto get a new field `Invalid` / `TotalInvalid`. `PgnImportService.ParsePgn` now returns a `ParseResult` record (Puzzles + Invalid) instead of a flat list.", de: "API: BookImportItemDto + BookImportResultDto bekommen ein neues Feld `Invalid` / `TotalInvalid`. `PgnImportService.ParsePgn` liefert jetzt einen `ParseResult`-Record (Puzzles + Invalid) statt einer flachen Liste." },
  ]},
  { version: "0.55.1", date: "2026-06-02", changes: [
    { en: "Log noise reduced: expected DataProtection startup warnings (key ring in the mounted /keys volume, without XML encryptor) are no longer logged as a warning on every restart; the EF Core \"FirstOrDefault without OrderBy\" message of the random puzzle selection (deterministic aggregate/ID range query) is downgraded to Debug. Real errors remain visible.", de: "Log-Rauschen reduziert: erwartete DataProtection-Startup-Warnungen (Key-Ring im gemounteten /keys-Volume, ohne XML-Encryptor) werden nicht mehr bei jedem Neustart als Warning geloggt; die EF-Core-„FirstOrDefault ohne OrderBy\"-Meldung der zufälligen Puzzle-Auswahl (deterministische Aggregat-/ID-Range-Query) ist auf Debug herabgestuft. Echte Fehler bleiben sichtbar." },
  ]},
  { version: "0.55.0", date: "2026-06-02", changes: [
    { en: "Kibana logging dashboard expanded: new panels \"Logins per Day\" (successful logins per day) and \"Unique Logins\" (unique users per period) alongside the existing puzzle metrics (Puzzles Solved, Puzzles per User).", de: "Kibana-Logging-Dashboard erweitert: neue Panels „Logins per Day\" (erfolgreiche Logins pro Tag) und „Unique Logins\" (eindeutige User pro Zeitraum) neben den bestehenden Puzzle-Kennzahlen (Puzzles Solved, Puzzles per User)." },
    { en: "API: Successful logins now generate a structured log event (\"UserLogin\" with UserId/UserName) — the basis for the login and unique login analysis in Kibana.", de: "API: Erfolgreiche Logins erzeugen jetzt einen strukturierten Log-Event („UserLogin\" mit UserId/UserName) — Grundlage für die Login- und Unique-Login-Auswertung in Kibana." },
  ]},
  { version: "0.54.0", date: "2026-06-02", changes: [
    { en: "Endless offline: when starting a run, matching puzzles for a whole run are now preloaded in the background and stored locally — so you can keep puzzling even without internet. Run size = maximum of the solved puzzles of the last 5 runs + 10 (without history: 30). New endpoint POST `/api/puzzles/random-batch` (one puzzle per rating window, unique).", de: "Endless offline: Beim Start eines Runs werden jetzt im Hintergrund passende Puzzles für einen ganzen Run vorab geladen und lokal gespeichert — so kann man auch ohne Internet weiterpuzzeln. Run-Größe = Maximum der gelösten Puzzles der letzten 5 Runs + 10 (ohne Historie: 30). Neuer Endpoint POST `/api/puzzles/random-batch` (ein Puzzle je Rating-Fenster, eindeutig)." },
  ]},
  { version: "0.53.0", date: "2026-06-02", changes: [
    { en: "Analysis board: arrows/circles (right-click drag) now work reliably — engine updates no longer discard the drawing (the board is only reset on real position changes).", de: "Analysebrett: Pfeile/Kreise (Rechtsklick-Ziehen) funktionieren jetzt zuverlässig — Engine-Updates verwerfen die Zeichnung nicht mehr (Brett wird nur bei echten Stellungsänderungen neu gesetzt)." },
    { en: "Arrows/circles now also on the puzzle boards (Standard, Book, Course, Weekly post, Endless, Blindfold) via right-click drag.", de: "Pfeile/Kreise jetzt auch auf den Puzzle-Brettern (Standard, Buch, Kurs, Wochenpost, Endless, Blind) per Rechtsklick-Ziehen." },
    { en: "Analysis mode: search depth manually adjustable (12–30, default 22, is saved); display \"Depth reached/Max\". The \"Lines\" field now has enough room.", de: "Analysemodus: Such-Tiefe manuell einstellbar (12–30, Standard 22, wird gespeichert); Anzeige „Tiefe erreicht/Max\". Das „Linien\"-Feld hat jetzt genug Platz." },
    { en: "Analysis mode: \"Back to puzzle\" button when you came here via \"Analyze\"/\"View last puzzle\".", de: "Analysemodus: „Zurück zum Puzzle\"-Button, wenn man über „Analysieren\"/„Letztes Puzzle ansehen\" hergekommen ist." },
  ]},
  { version: "0.52.2", date: "2026-06-02", changes: [
    { en: "\"View last puzzle\" now opens the analysis mode directly with the last solved puzzle (position + move sequence + orientation), instead of loading the puzzle again to solve it.", de: "„Letztes Puzzle ansehen\" öffnet jetzt direkt den Analysemodus mit dem zuletzt gelösten Puzzle (Stellung + Zugfolge + Orientierung), statt das Puzzle erneut zum Lösen zu laden." },
  ]},
  { version: "0.52.1", date: "2026-06-02", changes: [
    { en: "Menu items \"Repertoires\" and \"Weekly post\" are for now only visible/accessible to admins (navigation hidden, routes protected via adminGuard, dashboard repertoire tile only for admins). The read API of the weekly posts remains unchanged.", de: "Menüpunkte „Repertoires\" und „Wochenpost\" sind vorerst nur für Admins sichtbar/erreichbar (Navigation ausgeblendet, Routen per adminGuard geschützt, Dashboard-Repertoire-Kachel nur für Admins). Die Lese-API der Wochenposts bleibt unverändert." },
  ]},
  { version: "0.52.0", date: "2026-06-02", changes: [
    { en: "Statistics: At level \"All\", the Elo curve is now shown separately per Visualization mode as its own graph (one graph per mode, provided there are at least 2 entries there) — instead of a single mixed curve across modes. Individual levels continue to show their own curve.", de: "Statistik: Bei Level „Alle\" wird die Elo-Kurve jetzt pro Visualisierungs-Modus getrennt als eigener Graph angezeigt (ein Graph je Modus, sofern dort mind. 2 Einträge vorliegen) — statt einer modus-übergreifenden Mischkurve. Einzelne Level zeigen weiterhin ihre eigene Kurve." },
  ]},
  { version: "0.51.0", date: "2026-06-02", changes: [
    { en: "New: \"Analyze\" button on puzzles (standard and book/course/weekly post puzzles, in the solved/given-up state) — opens analysis mode with the current position and the complete move sequence (via `/analysis?fen=…&moves=…&orientation=…`). There you then have engine lines, your own continued play, and arrows/circles (right-click drag). Analysis mode loads a passed move sequence from the starting position and jumps to the current position.", de: "Neu: „Analysieren\"-Button bei Puzzles (Standard- und Buch-/Kurs-/Wochenpost-Puzzles, im gelösten/aufgegebenen Zustand) — öffnet den Analysemodus mit der aktuellen Stellung und der kompletten Zugfolge (über `/analysis?fen=…&moves=…&orientation=…`). Dort dann Engine-Lines, eigenes Weiterziehen und Pfeile/Kreise (Rechtsklick-Ziehen). Der Analysemodus lädt eine übergebene Zugfolge ab der Startstellung und springt an die aktuelle Stellung." },
  ]},
  { version: "0.50.1", date: "2026-06-02", changes: [
    { en: "Fix (puzzle): \"Give up\" now also switches to the starting position in normal puzzle mode and automatically plays through the solution move by move (previously it behaved like \"Reset\"). Manually clicking forward/back stops playback; \"Again\"/\"Next\" ends it. Analogous to the existing behavior for book/course/weekly post puzzles.", de: "Fix (Puzzle): „Aufgeben\" wechselt jetzt auch im normalen Puzzle-Modus auf die Anfangsstellung und spielt die Lösung automatisch Zug für Zug durch (vorher verhielt es sich wie „Zurücksetzen\"). Manuelles Vor-/Zurückklicken stoppt die Wiedergabe; „Nochmal\"/„Nächstes\" beendet sie. Analog zum bereits bestehenden Verhalten bei Buch-/Kurs-/Wochenpost-Puzzles." },
  ]},
  { version: "0.50.0", date: "2026-06-02", changes: [
    { en: "New: Link your RookHub account with Discord. In the profile under \"Discord\", a linked account is shown plus \"Unlink\". Linking is done via a link signed by the chess bot (`/link` command or welcome DM): immediately when logged in, while anonymously the link is noted and automatically redeemed after login/registration. New endpoints POST `/api/profile/discord/link` + DELETE `/api/profile/discord`; the Discord ID is unique (≤ 1 RookHub user). Secret via `Discord__LinkSecret` (= bot `ROOKHUB_LINK_SECRET`), empty → feature inactive.", de: "Neu: RookHub-Konto mit Discord verknüpfen. Im Profil unter „Discord\" wird ein verknüpftes Konto angezeigt + „Verknüpfung trennen\". Verknüpft wird über einen vom Schach-Bot signierten Link (`/link`-Befehl bzw. Begrüßungs-DM): eingeloggt sofort, anonym wird der Link vorgemerkt und nach Login/Registrierung automatisch eingelöst. Neue Endpoints POST `/api/profile/discord/link` + DELETE `/api/profile/discord`; Discord-ID ist eindeutig (≤ 1 RookHub-User). Secret via `Discord__LinkSecret` (= Bot `ROOKHUB_LINK_SECRET`), leer → Feature inaktiv." },
  ]},
  { version: "0.49.0", date: "2026-06-02", changes: [
    { en: "Statistics expanded: breakdown by topic (hit rate per tactics topic), rating distribution of solved puzzles (bar per 200-point rating band, accuracy on hover), and an activity heatmap (attempts per day, last ~6 months). New endpoint GET `/api/puzzles/stats/breakdown`.", de: "Statistik erweitert: Aufschlüsselung nach Thema (Trefferquote je Taktik-Thema), Rating-Verteilung der gelösten Puzzles (Balken je 200er-Rating-Band, Genauigkeit beim Überfahren) und eine Aktivitäts-Heatmap (Versuche pro Tag, letzte ~6 Monate). Neuer Endpoint GET `/api/puzzles/stats/breakdown`." },
  ]},
  { version: "0.48.0", date: "2026-06-02", changes: [
    { en: "New (statistics): Personal statistics page (menu \"Statistics\", logged in) — puzzle Elo history as a curve (filterable per Visualization level), key figures (Elo, solved, attempts, accuracy, current/best streak), Elo per level, and a list of recently played puzzles (with Δ-Elo, time, link to the puzzle). New endpoint GET `/api/puzzles/elo-history`.", de: "Neu (Statistik): Persönliche Statistikseite (Menü „Statistik\", eingeloggt) — Puzzle-Elo-Verlauf als Kurve (pro Visualisierungs-Level filterbar), Kennzahlen (Elo, gelöst, Versuche, Genauigkeit, aktuelle/beste Serie), Elo je Level und eine Liste der zuletzt gespielten Puzzles (mit Δ-Elo, Zeit, Link zum Puzzle). Neuer Endpoint GET `/api/puzzles/elo-history`." },
  ]},
  { version: "0.47.1", date: "2026-06-02", changes: [
    { en: "Footer: \"Feedback / Report bug\" link to the GitHub issue tracker (github.com/kahalm/rookhub/issues).", de: "Footer: „Feedback / Bug melden\"-Link zum GitHub-Issue-Tracker (github.com/kahalm/rookhub/issues)." },
  ]},
  { version: "0.47.0", date: "2026-06-02", changes: [
    { en: "New (analysis): Standalone analysis mode (menu \"Analysis\", public) à la Lichess — board with free dragging of both sides, local Stockfish engine with configurable number of top lines (1–5, default 3) including eval and SAN move sequence, eval bar, best moves as arrows, your own arrows/circles via right-click drag (chessground). Move list with click-through + keyboard (←/→/Home/End), flip board, load/copy FEN, load PGN. (Phase 1; variation tree & \"Analyze\" button to follow.)", de: "Neu (Analyse): Eigenständiger Analysemodus (Menü „Analyse\", öffentlich) à la Lichess — Brett mit freiem Ziehen beider Seiten, lokale Stockfish-Engine mit konfigurierbarer Anzahl Top-Lines (1–5, Standard 3) inkl. Eval und SAN-Zugfolge, Eval-Bar, beste Züge als Pfeile, eigene Pfeile/Kreise per Rechtsklick-Ziehen (chessground). Zugliste mit Durchklicken + Tastatur (←/→/Pos1/Ende), Brett drehen, FEN laden/kopieren, PGN laden. (Phase 1; Varianten-Baum & „Analysieren\"-Button folgen.)" },
  ]},
  { version: "0.46.2", date: "2026-06-02", changes: [
    { en: "Book/course/weekly post puzzles: \"Give up\" now switches to the starting position and automatically plays through the solution move by move (instead of letting you play it yourself). Manually clicking forward/back stops playback; \"Again\" restarts the puzzle.", de: "Buch-/Kurs-/Wochenpost-Puzzles: „Aufgeben\" wechselt jetzt auf die Anfangsstellung und spielt die Lösung automatisch Zug für Zug durch (statt sie selbst spielen zu lassen). Manuelles Vor-/Zurückklicken stoppt die Wiedergabe; „Nochmal\" startet das Puzzle neu." },
  ]},
  { version: "0.46.1", date: "2026-06-02", changes: [
    { en: "Admin/user list: New column \"Groups\" shows each user's group memberships (as chips). `GET /api/admin/users` now also returns the group names.", de: "Admin/Userliste: Neue Spalte „Gruppen\" zeigt pro User die Gruppen-Mitgliedschaften (als Chips). `GET /api/admin/users` liefert die Gruppennamen mit." },
  ]},
  { version: "0.46.0", date: "2026-06-01", changes: [
    { en: "Localization (i18n): The entire interface is now multilingual — English (default), German and Croatian. Language switcher (globe icon) in the navigation bar, the selection is saved (localStorage). Implemented with ngx-translate; all texts in all areas (navigation, auth, dashboard, profile, friends, repertoires, tournaments, puzzles, Endless mode, books/courses, weekly post, admin, PGN viewer) run through translation keys (`public/i18n/{en,de,hr}.json`). Missing keys fall back to English. (Backend/API messages remain English for now.)", de: "Lokalisierung (i18n): Die gesamte Oberfläche ist jetzt mehrsprachig — Englisch (Standard), Deutsch und Kroatisch. Sprachumschalter (Globus-Icon) in der Navigationsleiste, Auswahl wird gespeichert (localStorage). Umgesetzt mit ngx-translate; alle Texte aller Bereiche (Navigation, Auth, Dashboard, Profil, Freunde, Repertoires, Turniere, Puzzles, Endlosmodus, Bücher/Kurse, Wochenpost, Admin, PGN-Viewer) laufen über Übersetzungs-Keys (`public/i18n/{en,de,hr}.json`). Fehlende Keys fallen auf Englisch zurück. (Backend-/API-Meldungen bleiben vorerst Englisch.)" },
  ]},
  { version: "0.45.0", date: "2026-06-01", changes: [
    { en: "Endless mode simplified: Fasttrack is now the default (no more toggle) — difficulty always rises along the phases. The two thresholds are now called \"1st Threshold\" / \"2nd Threshold\" (previously \"1st/2nd Mistake Rating\"). The \"Step Size\" setting is gone; the width of the rating window in puzzle selection is internally fixed at 40. DB: columns Step + Fasttrack removed from EndlessProgresses (migration); FasttrackThreshold1/2 remain. Old saved sessions/configs still load without issues.", de: "Endlosmodus vereinfacht: Fasttrack ist jetzt der Standard (kein Toggle mehr) — die Schwierigkeit steigt immer entlang der Phasen. Die beiden Schwellen heißen jetzt „1st Threshold\" / „2nd Threshold\" (vorher „1st/2nd Mistake Rating\"). Die „Step Size\"-Einstellung ist entfallen; die Breite des Rating-Fensters bei der Puzzleauswahl ist intern fix 40. DB: Spalten Step + Fasttrack aus EndlessProgresses entfernt (Migration); FasttrackThreshold1/2 bleiben. Alte gespeicherte Sessions/Configs laden weiterhin problemlos." },
  ]},
  { version: "0.44.3", date: "2026-06-01", changes: [
    { en: "Tests/refactor: Test audit of the new features. New tests for `courseAccessGuard` (access gating: login/admin/group/error → 5 cases) and the weekly post schedule logic (last + 7 days / same time / default 19:00, including month rollover) — for this the schedule calculation was extracted into pure, testable functions (`nextWeeklySlot`, `weeklyDatePart`, `weeklyTimePart`). Auto-subscription test expanded with FideId change. Frontend 115, backend 424 tests green.", de: "Tests/Refactor: Test-Audit der neuen Features. Neue Tests für `courseAccessGuard` (Zugriffs-Gating: Login/Admin/Gruppe/Fehler → 5 Fälle) und die Wochenpost-Termin-Logik (letzter + 7 Tage / gleiche Uhrzeit / Default 19:00, inkl. Monatsübergang) — dafür wurde die Termin-Berechnung in reine, testbare Funktionen (`nextWeeklySlot`, `weeklyDatePart`, `weeklyTimePart`) ausgelagert. Auto-Subscription-Test um FideId-Änderung erweitert. Frontend 115, Backend 424 Tests grün." },
  ]},
  { version: "0.44.2", date: "2026-06-01", changes: [
    { en: "Fix (auto-subscription / crawler load): A profile update now only triggers the tournament crawler (`/api/players/tournaments`) when the chess identity (ChessResultsId/LastName/FirstName/FideId) actually changes. Previously EVERY `PUT /api/profile` triggered a crawler run — i.e. also pure settings saves (board theme, pieces, Stockfish depth, difficulty) from the PreferencesService, which led to very frequent `gluetun:8080/api/players/tournaments` calls.", de: "Fix (Auto-Subscription / Crawler-Last): Ein Profil-Update stößt den Turnier-Crawler (`/api/players/tournaments`) jetzt nur noch an, wenn sich die Schach-Identität (ChessResultsId/LastName/FirstName/FideId) tatsächlich ändert. Vorher löste JEDER `PUT /api/profile` den Crawler-Lauf aus — also auch reine Einstellungs-Saves (Brett-Theme, Figuren, Stockfish-Tiefe, Schwierigkeit) aus dem PreferencesService, was zu sehr häufigen `gluetun:8080/api/players/tournaments`-Calls führte." },
  ]},
  { version: "0.44.1", date: "2026-06-01", changes: [
    { en: "Hardening (logging): All log events within a request now carry — if available — UserId, UserName and IpAddress (via LogContext enrichment in a middleware), no longer just the request summary. Anonymous requests remain without UserId/UserName (\"if available\"). Field names unchanged (UserId/UserName/IpAddress) — Kibana dashboards stay compatible.", de: "Härtung (Logging): Alle Log-Events innerhalb eines Requests tragen jetzt — sofern vorhanden — UserId, UserName und IpAddress (per LogContext-Enrichment in einer Middleware), nicht mehr nur die Request-Summary. Anonyme Requests bleiben ohne UserId/UserName („wenn vorhanden\"). Feldnamen unverändert (UserId/UserName/IpAddress) — Kibana-Dashboards bleiben kompatibel." },
  ]},
  { version: "0.44.0", date: "2026-06-01", changes: [
    { en: "New (play through weekly post): \"Play through\" now opens a play page (`/weekly/:id`) instead of a PGN dialog — you solve the weekly post's puzzles one after another (like in Endless mode, but without lives and with any number of retries). The uploaded PGN is parsed server-side on the fly into puzzles (same logic as books); new public endpoint GET `/api/weekly-posts/{id}/puzzles`. Progress (X/Y) + \"Next puzzle\" / \"Skip\" / \"To overview\".", de: "Neu (Wochenpost durchspielen): „Durchspielen\" öffnet jetzt eine Spiel-Seite (`/weekly/:id`) statt eines PGN-Dialogs — man löst die Puzzles des Wochenposts der Reihe nach (wie im Endlosmodus, aber ohne Leben und mit beliebig vielen Retrys). Das hochgeladene PGN wird serverseitig on-the-fly in Puzzles geparst (gleiche Logik wie Bücher); neuer öffentlicher Endpoint GET `/api/weekly-posts/{id}/puzzles`. Fortschritt (X/Y) + „Nächstes Puzzle\" / „Überspringen\" / „Zur Übersicht\"." },
  ]},
  { version: "0.43.0", date: "2026-06-01", changes: [
    { en: "New (weekly post): New public menu item \"Weekly post\" — maps the weekly chess posts onto RookHub. Admins upload a PGN with a schedule (date + time); the date is automatically pre-filled to \"last entry + 7 days\", the time stays the same (default 19:00). Each post is interactively clickable through via the PGN viewer; list/display are public (even without login), uploading/editing/deleting only for admins. Data lives in the DB (new table WeeklyPosts). New API: GET `/api/weekly-posts`(+`/{id}`), POST/PUT/DELETE `/api/admin/weekly-posts`.", de: "Neu (Wochenpost): Neuer öffentlicher Menüpunkt „Wochenpost\" — bildet die wöchentlichen Schach-Posts auf RookHub ab. Admins laden ein PGN mit Termin (Datum + Uhrzeit) hoch; das Datum wird automatisch auf „letzter Eintrag + 7 Tage\" vorbelegt, die Uhrzeit bleibt gleich (Standard 19:00). Jeder Post ist über den PGN-Viewer interaktiv durchklickbar; Liste/Anzeige sind öffentlich (auch ohne Login), Hochladen/Bearbeiten/Löschen nur für Admins. Daten liegen in der DB (neue Tabelle WeeklyPosts). Neue API: GET `/api/weekly-posts`(+`/{id}`), POST/PUT/DELETE `/api/admin/weekly-posts`." },
  ]},
  { version: "0.42.0", date: "2026-06-01", changes: [
    { en: "New (courses · group permission): In the admin area you can set per book which groups may see it as a course (column \"Visible to (courses)\" in the books list). The \"Courses\" menu and the course overview are now no longer admin-only: members of an authorized group see the menu item and exactly the books authorized for them (admins still all). Access is enforced server- and route-side. Authorizations live in the DB (new table BookGroupAccess) and are cleaned up when deleting a book or group. New API: GET/PUT `/api/admin/books/{id}/groups`, GET `/api/courses/access`.", de: "Neu (Kurse · Gruppen-Berechtigung): Im Admin-Bereich lässt sich pro Buch festlegen, welche Gruppen es als Kurs sehen dürfen (Spalte „Sichtbar für (Kurse)\" in der Bücher-Liste). Das „Kurse\"-Menü und die Kurs-Übersicht sind jetzt nicht mehr admin-only: Mitglieder einer freigegebenen Gruppe sehen den Menüpunkt und genau die für sie freigegebenen Bücher (Admins weiterhin alle). Zugriff wird server- und routenseitig erzwungen. Freigaben liegen in der DB (neue Tabelle BookGroupAccess) und werden beim Löschen von Buch oder Gruppe mit aufgeräumt. Neue API: GET/PUT `/api/admin/books/{id}/groups`, GET `/api/courses/access`." },
  ]},
  { version: "0.41.0", date: "2026-06-01", changes: [
    { en: "New (courses, admin-only): New menu item \"Courses\" shows all collections imported as books as an overview with progress bars (solved puzzles / total). Each book can be worked through in two modes — sequential (book order, \"Skip\" jumps onward) or random. Progress is user-specific and stored entirely in the DB (new tables CourseProgress + CoursePuzzleResult); a book has shared progress across both modes. Reset possible per course. New API: GET `/api/courses`, GET `/api/courses/{bookId}/next`, POST `/api/courses/{bookId}/results`, POST `/api/courses/{bookId}/reset`.", de: "Neu (Kurse, admin-only): Neuer Menüpunkt „Kurse\" zeigt alle als Bücher importierten Sammlungen als Übersicht mit Fortschrittsbalken (gelöste Puzzles / gesamt). Jedes Buch lässt sich in zwei Modi durcharbeiten — sequenziell (Buchreihenfolge, „Überspringen\" springt weiter) oder zufällig. Der Fortschritt ist user-bezogen und wird komplett in der DB gespeichert (neue Tabellen CourseProgress + CoursePuzzleResult); ein Buch hat einen geteilten Fortschritt über beide Modi. Reset pro Kurs möglich. Neue API: GET `/api/courses`, GET `/api/courses/{bookId}/next`, POST `/api/courses/{bookId}/results`, POST `/api/courses/{bookId}/reset`." },
  ]},
  { version: "0.40.41", date: "2026-06-01", changes: [
    { en: "Hardening (crawler proxy): (1) `/api/tournaments/crawl` no longer passes through the raw body but builds a validated body (chessResultsId + jobType against a whitelist) — no injection of arbitrary/future fields. (2) All proxy calls pass through `HttpContext.RequestAborted`, so aborted client requests don't keep running at the crawler. (3) `GET /api/book-puzzles/random` now returns 404 instead of an unhandled 500 on a pool shrink between Count and Skip. (Code audit findings, cross-project.)", de: "Härtung (Crawler-Proxy): (1) `/api/tournaments/crawl` reicht nicht mehr den Roh-Body durch, sondern baut einen validierten Body (chessResultsId + jobType gegen Whitelist) — keine Injektion beliebiger/zukünftiger Felder. (2) Alle Proxy-Aufrufe reichen `HttpContext.RequestAborted` durch, sodass abgebrochene Client-Requests nicht am Crawler weiterlaufen. (3) `GET /api/book-puzzles/random` liefert bei einem Pool-Schrumpf zwischen Count und Skip jetzt 404 statt eines unbehandelten 500. (Code-Audit Findings, Projektübergreifend.)" },
  ]},
  { version: "0.40.40", date: "2026-06-01", changes: [
    { en: "Hardening (friendships): New direction-independent unique index on the unordered user pair (STORED computed columns LEAST/GREATEST). Simultaneous A→B and B→A requests can no longer create two rows — the migration safely deduplicates existing duplicates before building the index. (Code audit finding, DB migration.)", de: "Härtung (Freundschaften): Neuer richtungsunabhängiger Unique-Index auf dem ungeordneten Nutzerpaar (STORED computed columns LEAST/GREATEST). Gleichzeitige A→B- und B→A-Anfragen können jetzt keine zwei Zeilen mehr erzeugen — die Migration dedupliziert vorhandene Duplikate sicher vor dem Index-Aufbau. (Code-Audit Finding, DB-Migration.)" },
  ]},
  { version: "0.40.39", date: "2026-06-01", changes: [
    { en: "Fix (random puzzle): With filters set (rating/topics/\"hide solved\"), the ID range is now determined over the *filtered* result set instead of globally — previously the random point almost always landed outside the matches, so the fallback always returned the same puzzle. Additionally wrap-around search (forward/backward) instead of \"always the first\". (Code audit finding.)", de: "Fix (Random-Puzzle): Bei gesetzten Filtern (Rating/Themen/„gelöste ausblenden\") wird die ID-Range jetzt über die *gefilterte* Treffermenge bestimmt statt global — vorher landete der Zufallspunkt fast immer außerhalb der Treffer, sodass der Fallback stets dasselbe Puzzle lieferte. Zusätzlich Wrap-around-Suche (vorwärts/rückwärts) statt „immer das erste\". (Code-Audit Finding.)" },
  ]},
  { version: "0.40.38", date: "2026-06-01", changes: [
    { en: "Fix (profile player search): With exactly one ChessResults and one FIDE match each, the FIDE match no longer overwrites the FIDE ID belonging to the CR player (auto-fill); manual clicking remains unchanged.", de: "Fix (Profil-Spielersuche): Bei je genau einem ChessResults- und FIDE-Treffer überschreibt der FIDE-Treffer nicht mehr die zum CR-Spieler gehörende FIDE-Id (Auto-Fill); manuelles Anklicken bleibt unverändert." },
    { en: "Fix (Endless sync): The \"already migrated\" flag is now per identity (user ID or anonymous session) instead of global — a second account/anon in the same browser migrates its local data again; the old global flag is safely adopted (no double migration).", de: "Fix (Endless-Sync): Das „bereits migriert\"-Flag ist jetzt pro Identität (User-Id bzw. anonyme Session) statt global — ein zweiter Account/Anon im selben Browser migriert seine lokalen Daten wieder; der alte globale Flag wird sicher übernommen (keine Doppel-Migration)." },
    { en: "Fix (tournament detail): `reloadAll` now also clears the display arrays and reloads the active tab — the teams/pairings table no longer shows stale rows after a refresh that contradict the counter.", de: "Fix (Turnier-Detail): `reloadAll` leert jetzt auch die Anzeige-Arrays und lädt den aktiven Tab neu — die Teams/Paarungen-Tabelle zeigt nach einem Refresh keine veralteten Zeilen mehr, die dem Zähler widersprechen." },
    { en: "Fix (puzzle board): The premove execution (setTimeout) is aborted on component destroy and no longer emits onto a destroyed/reset position.", de: "Fix (Puzzle-Brett): Die Premove-Ausführung (setTimeout) wird bei Component-Destroy abgebrochen und emittiert nicht mehr auf eine zerstörte/zurückgesetzte Stellung." },
    { en: "(Code audit findings.)", de: "(Code-Audit Findings.)" },
  ]},
  { version: "0.40.37", date: "2026-06-01", changes: [
    { en: "Hardening (PGN upload): Content validation now requires a real tag pair (`[Event \"…\"]`) or a real first move (`1. e4`/`1. Nf3`/`1. O-O`) instead of just the substrings `[Event`/`1.` anywhere — plain prose like \"Chapter 1. Intro\" is rejected (ReDoS-safe regex with timeout). (Code audit finding.)", de: "Härtung (PGN-Upload): Die Inhaltsvalidierung verlangt jetzt ein echtes Tag-Pair (`[Event \"…\"]`) oder einen echten ersten Zug (`1. e4`/`1. Nf3`/`1. O-O`) statt nur die Teilstrings `[Event`/`1.` irgendwo — reiner Fließtext wie „Chapter 1. Intro\" wird abgelehnt (ReDoS-sicheres Regex mit Timeout). (Code-Audit Finding.)" },
  ]},
  { version: "0.40.36", date: "2026-06-01", changes: [
    { en: "Fix (PGN parser): `stripVariations` now produces brace-balanced output (no stray `}`, an unterminated `{` comment brace is closed at the end) — a game with unbalanced braces is thus parsed instead of silently discarded. The formerly empty `catch{}` block now logs skipped games via `console.warn` (diagnostics). (Code audit findings.)", de: "Fix (PGN-Parser): `stripVariations` erzeugt jetzt brace-balancierte Ausgabe (keine Streu-`}`, eine unterminierte `{`-Kommentarklammer wird am Ende geschlossen) — ein Spiel mit unbalancierten Klammern wird so geparst statt still verworfen. Der vormals leere `catch{}`-Block loggt übersprungene Spiele jetzt per `console.warn` (Diagnose). (Code-Audit Findings.)" },
  ]},
  { version: "0.40.35", date: "2026-06-01", changes: [
    { en: "Hardening/docs: Admin `DeleteUser` catches a DbUpdateException (remaining FK references) and returns 409 instead of an unhandled 500. CORS docs in CLAUDE.md aligned with the actual code (ExtensionPolicy only allows chess.com, not `chrome-extension://*`/localhost). (Code audit findings.)", de: "Härtung/Doku: Admin-`DeleteUser` fängt eine DbUpdateException (verbliebene FK-Referenzen) ab und liefert 409 statt eines unbehandelten 500. CORS-Doku in der CLAUDE.md an den tatsächlichen Code angeglichen (ExtensionPolicy erlaubt nur chess.com, nicht `chrome-extension://*`/localhost). (Code-Audit Findings.)" },
  ]},
  { version: "0.40.34", date: "2026-06-01", changes: [
    { en: "Hardening (tournament monitor): Activation is rejected (502) if the crawler doesn't return a DB ID — otherwise the background monitor would poll `/api/tournaments/0/rounds/check` indefinitely. The monitor loop additionally skips records with DbId ≤ 0 defensively and passes the CancellationToken to all crawler calls (clean shutdown, no blowing up the 30s interval). (Code audit findings.)", de: "Härtung (Turnier-Monitor): Aktivierung wird abgelehnt (502), wenn der Crawler keine DB-Id liefert — sonst pollte der Hintergrund-Monitor dauerhaft `/api/tournaments/0/rounds/check`. Der Monitor-Loop überspringt zudem defensiv Datensätze mit DbId ≤ 0 und reicht den CancellationToken an alle Crawler-Aufrufe weiter (sauberer Shutdown, kein Sprengen des 30s-Intervalls). (Code-Audit Findings.)" },
  ]},
  { version: "0.40.33", date: "2026-06-01", changes: [
    { en: "Fix (puzzle timer): `abortSolver` now also cleans up the Visualization show timer (otherwise its callback fired after teardown/puzzle change); `continueAfterSolve` (Endless) discards a still-running auto-advance timer before continuing (no double advance). (Code audit findings.)", de: "Fix (Puzzle-Timer): `abortSolver` räumt jetzt auch den Visualisierungs-Show-Timer auf (sonst feuerte sein Callback nach Teardown/Puzzlewechsel); `continueAfterSolve` (Endless) verwirft einen noch laufenden Auto-Advance-Timer, bevor es fortfährt (kein Doppel-Advance). (Code-Audit Findings.)" },
  ]},
  { version: "0.40.32", date: "2026-06-01", changes: [
    { en: "Hardening (API): (1) Group creation now catches a DbUpdateException (parallel create with the same name) and returns 400 instead of 500. (2) Password hashing uses an explicit BCrypt work factor (12) instead of the library default (10) — for registration, admin seeder and the timing dummy hash. (Code audit findings.)", de: "Hardening (API): (1) Gruppen-Anlage fängt jetzt eine DbUpdateException (paralleler Create mit gleichem Namen) ab und liefert 400 statt 500. (2) Passwort-Hashing nutzt einen expliziten BCrypt-Workfactor (12) statt des Library-Defaults (10) — für Registrierung, Admin-Seeder und den Timing-Dummy-Hash. (Code-Audit Findings.)" },
  ]},
  { version: "0.40.31", date: "2026-06-01", changes: [
    { en: "Repertoire upload (MEDIUM+LOW bundle): (1) Client-side validation — only .pgn files up to 10 MB are uploaded (drag&drop previously bypassed the accept filter). (2) File input is reset after selection so the same file can be selected again. (3) Download shows a snackbar on error instead of failing silently. (Code audit findings, repertoire-edit.component.spec.ts.)", de: "Repertoire-Upload (MEDIUM+LOW-Bündel): (1) Client-seitige Validierung — nur .pgn-Dateien bis 10 MB werden hochgeladen (Drag&Drop umging bisher den accept-Filter). (2) Datei-Input wird nach Auswahl zurückgesetzt, damit dieselbe Datei erneut wählbar ist. (3) Download zeigt bei Fehler eine Snackbar statt still zu scheitern. (Code-Audit Findings, repertoire-edit.component.spec.ts.)" },
  ]},
  { version: "0.40.30", date: "2026-06-01", changes: [
    { en: "Fix (PGN move list): Move numbering fixedly assumed White-first — games/lines that start with Black to move per FEN were numbered/colored incorrectly. Number and side are now derived per move from the FEN (`move.before`); a Black start is rendered as \"N… move\". (Code audit finding, move-list.component.spec.ts.)", de: "Fix (PGN-Zugliste): Die Zugnummerierung nahm fix Weiß-zuerst an — Partien/Linien, die laut FEN mit Schwarz am Zug beginnen, wurden falsch nummeriert/gefärbt. Nummer und Seite werden jetzt pro Zug aus der FEN (`move.before`) abgeleitet; Schwarz-Start wird als „N… Zug\" dargestellt. (Code-Audit Finding, move-list.component.spec.ts.)" },
  ]},
  { version: "0.40.29", date: "2026-06-01", changes: [
    { en: "Performance (PGN import): During book import, ALL LineIds of all books were loaded into memory for duplicate detection. Now only the lines of the current book/file (LineIds are file-prefix-unique). Behavior identical, significantly less memory/DB load with a large inventory. (Code audit finding.)", de: "Performance (PGN-Import): Beim Buch-Import wurden zur Duplikat-Erkennung ALLE LineIds aller Bücher in den Speicher geladen. Jetzt nur noch die Linien des aktuellen Buchs/der Datei (LineIds sind dateiprefix-eindeutig). Verhaltensgleich, deutlich weniger Speicher/DB-Last bei großem Bestand. (Code-Audit Finding.)" },
  ]},
  { version: "0.40.28", date: "2026-06-01", changes: [
    { en: "Robustness (crawler, chessresults_crawler repo): VPN rotation now passes through the CancellationToken (shutdown can abort) and resets the rate limiter timestamp so the first request over the new connection waits the full minimum interval. (Code audit finding.)", de: "Robustness (Crawler, chessresults_crawler-Repo): VPN-Rotation reicht jetzt den CancellationToken durch (Shutdown kann abbrechen) und setzt den Rate-Limiter-Zeitstempel zurück, damit die erste Anfrage über die neue Verbindung den vollen Mindestabstand abwartet. (Code-Audit Finding.)" },
  ]},
  { version: "0.40.27", date: "2026-06-01", changes: [
    { en: "Security (crawler, chessresults_crawler repo): Remaining SSRF host checks switched from `EndsWith(\"chess-results.com\")` (also matched \"evilchess-results.com\") to exact host comparison; additionally a global 5s regex timeout as ReDoS protection for the untrusted HTML body. (Code audit findings.)", de: "Security (Crawler, chessresults_crawler-Repo): Restliche SSRF-Host-Prüfungen von `EndsWith(\"chess-results.com\")` (matchte auch „evilchess-results.com\") auf exakten Host-Vergleich umgestellt; zusätzlich globales 5s-Regex-Timeout als ReDoS-Schutz für den untrusted HTML-Body. (Code-Audit Findings.)" },
  ]},
  { version: "0.40.26", date: "2026-06-01", changes: [
    { en: "Fix (crawler, chessresults_crawler repo): Team pairings were given a running counter number instead of the real \"No.\" from the table — with skipped/sorted rows the MatchNumber deviated. Now uses the parsed real number. (Code audit finding, HtmlParserServiceTests.)", de: "Fix (Crawler, chessresults_crawler-Repo): Mannschaftspaarungen bekamen eine fortlaufende Zähler-Nummer statt der echten „Nr.\" aus der Tabelle — bei übersprungenen/sortierten Zeilen wich die MatchNumber ab. Nutzt jetzt die geparste echte Nummer. (Code-Audit Finding, HtmlParserServiceTests.)" },
  ]},
  { version: "0.40.25", date: "2026-06-01", changes: [
    { en: "Security (crawler, chessresults_crawler repo): API key middleware checked open paths via `StartsWith` — `/api/healthXYZ` or similar thereby bypassed the API key. Now an exact/segment-precise match (`/api/health`, `/api/health/ip`, `/swagger`, `/swagger/*`). Additionally the key comparison is made length-safe via SHA-256 (no key length leak via comparison time). (Code audit findings, ApiKeyMiddlewareTests.)", de: "Security (Crawler, chessresults_crawler-Repo): API-Key-Middleware prüfte offene Pfade per `StartsWith` — `/api/healthXYZ` o.ä. umging dadurch den API-Key. Jetzt exakter/segment-genauer Match (`/api/health`, `/api/health/ip`, `/swagger`, `/swagger/*`). Zusätzlich wird der Key-Vergleich über SHA-256 längensicher gemacht (keine Key-Längen-Leak über die Vergleichszeit). (Code-Audit Findings, ApiKeyMiddlewareTests.)" },
  ]},
  { version: "0.40.24", date: "2026-06-01", changes: [
    { en: "Hardening (API, MEDIUM bundle): (1) PGN upload now has a RequestSizeLimit (~11 MB) → oversized bodies are rejected before they are buffered. (2) Endless bulk import endpoints (auth + anonymous) with RequestSizeLimit (2 MB) → large payload is not first fully deserialized. (3) Registration returns the same generic message \"Username or email already in use.\" on both username AND email collision → no more enumeration oracle. (Code audit MEDIUM findings, AuthServiceTests.)", de: "Hardening (API, MEDIUM-Bündel): (1) PGN-Upload hat jetzt ein RequestSizeLimit (~11 MB) → übergroße Bodies werden abgewiesen, bevor sie gepuffert werden. (2) Endless-Bulk-Import-Endpunkte (auth + anonym) mit RequestSizeLimit (2 MB) → großer Payload wird nicht erst komplett deserialisiert. (3) Registrierung gibt bei Username- UND E-Mail-Kollision dieselbe generische Meldung „Username or email already in use.\" zurück → kein Enumeration-Oracle mehr. (Code-Audit MEDIUM-Findings, AuthServiceTests.)" },
  ]},
  { version: "0.40.23", date: "2026-06-01", changes: [
    { en: "Fix (repertoire upload): When uploading multiple PGN files, a full repertoire + combo PGN reload was triggered after EACH individual success (N reloads). The uploads are now bundled (forkJoin) and the reload is triggered exactly once after completion; partial failures no longer discard the successful uploads. (Code audit finding, repertoire-edit.component.spec.ts.)", de: "Fix (Repertoire-Upload): Beim Hochladen mehrerer PGN-Dateien wurde nach JEDEM einzelnen Erfolg ein voller Repertoire- + Kombi-PGN-Reload ausgelöst (N Reloads). Die Uploads werden jetzt gebündelt (forkJoin) und der Reload wird genau einmal nach Abschluss ausgelöst; Teilfehler verwerfen die erfolgreichen Uploads nicht mehr. (Code-Audit Finding, repertoire-edit.component.spec.ts.)" },
  ]},
  { version: "0.40.22", date: "2026-06-01", changes: [
    { en: "Fix (crawler, chessresults_crawler repo): The duplicate crawl protection was a TOCTOU race (AnyAsync check + insert not atomic). New EF migration: STORED computed column \"ActiveKey\" (= ChessResultsId only for active jobs, otherwise NULL) with a unique index enforces at most ONE active crawl job per tournament on the DB side; the race is caught as 409. (Code audit finding.)", de: "Fix (Crawler, chessresults_crawler-Repo): Der Duplikat-Crawl-Schutz war eine TOCTOU-Race (AnyAsync-Check + Insert nicht atomar). Neue EF-Migration: STORED Computed Column „ActiveKey\" (= ChessResultsId nur für aktive Jobs, sonst NULL) mit Unique-Index erzwingt DB-seitig höchstens EINEN aktiven Crawl-Job pro Turnier; die Race wird als 409 abgefangen. (Code-Audit Finding.)" },
  ]},
  { version: "0.40.21", date: "2026-06-01", changes: [
    { en: "Security (crawler, chessresults_crawler repo): The tournament fetches followed redirects automatically without checking the final host (SSRF — only the first request was secured). Each fetch now validates the end host exactly against chess-results.com (also excludes \"evilchess-results.com\"/\"…com.attacker.tld\"). (Code audit finding, CrawlerServiceSsrfTests.)", de: "Security (Crawler, chessresults_crawler-Repo): Die Turnier-Fetches folgten Redirects automatisch ohne den finalen Host zu prüfen (SSRF — nur die erste Anfrage war abgesichert). Jeder Fetch validiert jetzt den End-Host exakt gegen chess-results.com (schließt auch „evilchess-results.com\"/„…com.attacker.tld\" aus). (Code-Audit Finding, CrawlerServiceSsrfTests.)" },
  ]},
  { version: "0.40.20", date: "2026-06-01", changes: [
    { en: "Fix (Endless highscore): The highscore sync was blind last-writer-wins — a locally lower value could overwrite a higher highscore achieved on another device/tab. The client now remembers the highest known highscore (from loadFromServer + saves) and never sends a lower one. (Code audit finding, endless-storage.service.spec.ts.)", de: "Fix (Endless-Highscore): Der Highscore-Sync war blindes Last-Writer-Wins — ein lokal niedrigerer Wert konnte einen auf einem anderen Gerät/Tab erreichten höheren Highscore überschreiben. Der Client merkt sich jetzt den höchsten bekannten Highscore (aus loadFromServer + Saves) und sendet nie einen niedrigeren. (Code-Audit Finding, endless-storage.service.spec.ts.)" },
  ]},
  { version: "0.40.19", date: "2026-06-01", changes: [
    { en: "Fix (puzzle/mouseslip): In the Stockfish error path the state is indeed PLAYING, but no opponent move was played — mouseslip nevertheless took back 2 half-moves and thereby also deleted a valid solution move. Now only what was actually played is taken back. (Code audit finding, base-puzzle-solver.spec.ts.)", de: "Fix (Puzzle/Mouseslip): Im Stockfish-Fehlerpfad ist der Zustand zwar PLAYING, es wurde aber kein Gegnerzug gespielt — Mouseslip nahm trotzdem 2 Halbzüge zurück und löschte so einen gültigen Lösungszug mit. Es wird jetzt nur zurückgenommen, was wirklich gespielt wurde. (Code-Audit Finding, base-puzzle-solver.spec.ts.)" },
  ]},
  { version: "0.40.18", date: "2026-06-01", changes: [
    { en: "Security/UX (auth): The JWT expiration was only checked once at app start — an expired session was considered logged in client-side until the next 401. Validity is now re-checked on every access (isLoggedIn/token/currentUser/isAdmin) and automatically logged out on an expired token. (Code audit finding, auth.service.spec.ts. Note: JWT remains in localStorage — switching to an HttpOnly cookie is separately open as a larger rework.)", de: "Security/UX (Auth): Das JWT-Ablaufdatum wurde nur einmal beim App-Start geprüft — eine abgelaufene Session galt clientseitig bis zum nächsten 401 als eingeloggt. Die Gültigkeit wird jetzt bei jedem Zugriff (isLoggedIn/token/currentUser/isAdmin) erneut geprüft und bei abgelaufenem Token automatisch ausgeloggt. (Code-Audit Finding, auth.service.spec.ts. Hinweis: JWT bleibt in localStorage — Umstieg auf HttpOnly-Cookie ist als größerer Umbau separat offen.)" },
  ]},
  { version: "0.40.17", date: "2026-06-01", changes: [
    { en: "Fix (anti-DoS): Anonymous puzzle attempts were stored unbounded per session (table bloat). Per anonymous session only the newest 200 attempts are now kept (older ones are trimmed on recording); the anonymous endpoints are additionally rate-limited. (Code audit finding, AnonymousPuzzleTests.)", de: "Fix (Anti-DoS): Anonyme Puzzle-Versuche wurden pro Session unbegrenzt gespeichert (Tabellen-Bloat). Pro anonymer Session werden jetzt nur die neuesten 200 Versuche behalten (ältere werden beim Aufzeichnen getrimmt); die anonymen Endpunkte sind zusätzlich rate-limitiert. (Code-Audit Finding, AnonymousPuzzleTests.)" },
  ]},
  { version: "0.40.16", date: "2026-06-01", changes: [
    { en: "Fix (Stockfish): The shared (root-wide) Stockfish service was terminated when leaving each puzzle mode and tore off running searches or could trigger a TypeError in the timer (access to the already terminated worker). The worker is now reused app-wide (no destroy() on component teardown) and the search holds the worker locally, so a parallel termination no longer throws an error. (Code audit findings.)", de: "Fix (Stockfish): Der gemeinsame (root-weite) Stockfish-Service wurde beim Verlassen jedes Puzzle-Modus terminiert und riss laufende Suchen ab bzw. konnte einen TypeError im Timer auslösen (Zugriff auf den bereits beendeten Worker). Der Worker wird jetzt App-weit wiederverwendet (kein destroy() beim Komponenten-Teardown) und die Suche hält den Worker lokal fest, sodass ein paralleles Beenden keinen Fehler mehr wirft. (Code-Audit Findings.)" },
  ]},
  { version: "0.40.15", date: "2026-06-01", changes: [
    { en: "Fix (PGN viewer): The PGN parser ran synchronously without a size limit — a very large/combined PGN could freeze the browser tab. Input is now capped (≤2 MB), number of games (≤500) and pathologically large single games are skipped. (Code audit finding, pgn-parser.spec.ts.)", de: "Fix (PGN-Viewer): Der PGN-Parser lief synchron ohne Größenlimit — ein sehr großes/kombiniertes PGN konnte den Browser-Tab einfrieren. Eingabe ist jetzt gedeckelt (≤2 MB), Anzahl Partien (≤500) und pathologisch große Einzelpartien werden übersprungen. (Code-Audit Finding, pgn-parser.spec.ts.)" },
  ]},
  { version: "0.40.14", date: "2026-06-01", changes: [
    { en: "Fix (HTTP retry): The retry interceptor repeated failed requests (status 0/502/503) for ALL methods — an automatic retry of POST/PUT/DELETE could produce duplicate side effects (e.g. duplicate puzzle attempts/requests). Now only idempotent methods (GET/HEAD) are retried. (Code audit finding, retry.interceptor.spec.ts.)", de: "Fix (HTTP-Retry): Der retry-Interceptor wiederholte fehlgeschlagene Requests (Status 0/502/503) für ALLE Methoden — ein automatischer Retry von POST/PUT/DELETE konnte doppelte Seiteneffekte erzeugen (z.B. doppelte Puzzle-Attempts/Anfragen). Es werden jetzt nur noch idempotente Methoden (GET/HEAD) erneut versucht. (Code-Audit Finding, retry.interceptor.spec.ts.)" },
  ]},
  { version: "0.40.13", date: "2026-06-01", changes: [
    { en: "Fix (player search): A single ChessResults match without a `name` field caused `GetProperty(\"name\")` to throw, whereby in the catch the ENTIRE result list was discarded (empty search). Such entries are now skipped (TryGetProperty), the rest is retained. (Code audit finding, PlayerSearchServiceTests.)", de: "Fix (Spielersuche): Ein einzelner ChessResults-Treffer ohne `name`-Feld ließ `GetProperty(\"name\")` werfen, wodurch im catch die GESAMTE Trefferliste verworfen wurde (leere Suche). Solche Einträge werden jetzt übersprungen (TryGetProperty), der Rest bleibt erhalten. (Code-Audit Finding, PlayerSearchServiceTests.)" },
  ]},
  { version: "0.40.12", date: "2026-06-01", changes: [
    { en: "Fix (Endless sync): Two simultaneous progress saves (auth or anonymous) led to a unique constraint violation on insert → HTTP 500 and lost update. On such an insert race, the row created in parallel is now reloaded and the update applied to it. (Code audit finding.)", de: "Fix (Endless-Sync): Zwei gleichzeitige Progress-Speicherungen (auth oder anonym) führten zu einer Unique-Constraint-Verletzung beim Insert → HTTP 500 und verlorenem Update. Bei einem solchen Insert-Race wird jetzt die parallel angelegte Zeile nachgeladen und das Update darauf angewendet. (Code-Audit Finding.)" },
  ]},
  { version: "0.40.11", date: "2026-06-01", changes: [
    { en: "Fix (auto-favorites): Player matching compared last names by substring (`name.Contains`) and thereby favorited wrong players (e.g. \"Ott\" → \"Ottenweller\", \"Scott\"). Now exact token comparison on the \"Last name, First name\" format (first name via first token); pure last-name match only from length ≥3. (Code audit finding, AutoSubscriptionServiceTests.)", de: "Fix (Auto-Favoriten): Der Spielerabgleich verglich Nachnamen per Substring (`name.Contains`) und favorisierte dadurch falsche Spieler (z.B. „Ott\" → „Ottenweller\", „Scott\"). Jetzt exakter Token-Vergleich auf das „Nachname, Vorname\"-Format (Vorname per erstem Token); reiner Nachnamen-Match nur ab Länge ≥3. (Code-Audit Finding, AutoSubscriptionServiceTests.)" },
  ]},
  { version: "0.40.10", date: "2026-06-01", changes: [
    { en: "Security (auth): Login now always performs a BCrypt verify against a dummy hash, even when the username doesn't exist — prevents username enumeration via the timing side channel. Username comparison on login & registration is now case-insensitive (matching the DB collation); colliding registrations cleanly return 409 (including DbUpdateException safeguard against races) instead of 500. (Code audit findings, AuthServiceTests.)", de: "Security (Auth): Login führt jetzt immer einen BCrypt-Verify gegen einen Dummy-Hash aus, auch wenn der Username nicht existiert — verhindert Username-Enumeration über den Timing-Seitenkanal. Username-Vergleich bei Login & Registrierung ist jetzt case-insensitiv (passend zur DB-Collation); kollidierende Registrierungen liefern sauber 409 (inkl. DbUpdateException-Absicherung gegen Races) statt 500. (Code-Audit Findings, AuthServiceTests.)" },
  ]},
  { version: "0.40.9", date: "2026-06-01", changes: [
    { en: "Fix (friends): Accept/Decline/Remove returned `Forbid(message)` on missing permission — the message was interpreted as an auth scheme and led to HTTP 500 instead of 403. Now cleanly returns 403 with an error message. (Code audit finding, FriendControllerTests.)", de: "Fix (Friends): Accept/Decline/Remove gaben bei fehlender Berechtigung `Forbid(message)` zurück — die Meldung wurde als Auth-Scheme interpretiert und führte zu HTTP 500 statt 403. Liefert jetzt sauber 403 mit Fehlermeldung. (Code-Audit Finding, FriendControllerTests.)" },
  ]},
  { version: "0.40.8", date: "2026-06-01", changes: [
    { en: "Security (tournament proxy): The anonymously reachable tournament GETs (public tournament page/sharing) remain usable without login, but are now throttled via a dedicated rate-limit policy (60/min) — previously they could unauthenticatedly load the underlying crawler (chess-results.com) without limit (DoS). (Code audit finding #5, TournamentProxyControllerTests.)", de: "Security (Turnier-Proxy): Die anonym erreichbaren Turnier-GETs (öffentliche Turnierseite/Teilen) bleiben ohne Login nutzbar, sind jetzt aber per dedizierter Rate-Limit-Policy (60/min) gedrosselt — vorher konnten sie unauthentifiziert den dahinterliegenden Crawler (chess-results.com) ungebremst belasten (DoS). (Code-Audit Finding #5, TournamentProxyControllerTests.)" },
  ]},
  { version: "0.40.7", date: "2026-06-01", changes: [
    { en: "Security (admin seeder): The seeder reset the admin password to the ADMIN_PASSWORD value on EVERY startup and re-promoted the account — anyone who knew the env config could take over/lock out an existing account, and a self-changed admin password was overwritten on every restart. The seeder now only creates the admin if it's missing, no longer touches existing accounts, and rejects the placeholder \"change_me\". (Code audit finding #4, AdminSeedTests.)", de: "Security (Admin-Seeder): Der Seeder hat das Admin-Passwort bei JEDEM Start auf den ADMIN_PASSWORD-Wert zurückgesetzt und das Konto re-promotet — wer die Env-Config kannte, konnte so ein bestehendes Konto übernehmen/aussperren, und ein selbst geändertes Admin-Passwort wurde bei jedem Neustart überschrieben. Der Seeder legt den Admin jetzt nur noch an, wenn er fehlt, fasst bestehende Konten nicht mehr an und verweigert den Platzhalter „change_me\". (Code-Audit Finding #4, AdminSeedTests.)" },
  ]},
  { version: "0.40.6", date: "2026-06-01", changes: [
    { en: "UX: After Give Up the puzzle is rebuilt from the start so the player can play through the correct solution themselves (instead of only clicking through in review mode). The failed attempt is registered as lost beforehand; no double recording.", de: "UX: Nach Give Up wird das Puzzle wieder von vorne aufgebaut, damit der Spieler die richtige Lösung selbst durchspielen kann (statt nur im Review-Modus durchklicken). Fehlversuch wird vorher als verloren registriert; keine Doppel-Aufzeichnung." },
    { en: "UX: Board labels — files (a–h) now right-aligned in the bottom-right corner of each column (analogous to the digits at the top left), no longer centered below the board. Override with !important against the chessground defaults.", de: "UX: Brett-Beschriftung — Linien (a–h) jetzt rechtsbündig in der unteren rechten Ecke jeder Spalte (analog zu den Ziffern oben links), nicht mehr zentriert unter dem Brett. Override mit !important gegen die chessground-Defaults." },
  ]},
  { version: "0.40.5", date: "2026-06-01", changes: [
    { en: "Fix: The share puzzle button was practically invisible as an absolutely positioned icon button in the mat card — now a clearly recognizable \"Share puzzle\" block button at the end of the puzzle info in all 3 modes.", de: "Fix: Share-Puzzle-Button war als absolut positionierter Icon-Button in der Mat-Card praktisch unsichtbar — jetzt ein klar erkennbarer \"Puzzle teilen\"-Block-Button am Ende der Puzzle-Info in allen 3 Modi." },
    { en: "Fix: Board labels — chessground renders default coords with top: -20px (ranks too high) and left: 24px (files too far right). A custom override pulls the labels back to the edge of the corner squares.", de: "Fix: Brett-Beschriftung — chessground rendert Default-Coords mit top: -20px (Ränge zu hoch) und left: 24px (Linien zu weit rechts). Custom-Override zieht die Beschriftungen wieder mittig an den Rand der Eck-Squares." },
  ]},
  { version: "0.40.4", date: "2026-06-01", changes: [
    { en: "Fix (viz mode): The promotion dialog was missing on the pawn promotion move — the promotion check looked at the frozen board, which knew nothing about the pawn on the 7th rank. Detection now runs over the actual chess.js position (new actualFen input).", de: "Fix (Viz-Modus): Promotion-Dialog fehlte beim Bauern-Umwandlungszug — der Promotion-Check schaute auf das eingefrorene Brett, das nichts vom Bauer auf der 7. Reihe wusste. Erkennung läuft jetzt über die tatsächliche chess.js-Stellung (neuer actualFen-Input)." },
    { en: "Fix (viz mode): An illegal 2nd click (e.g. a1 → c3 without a legal move) discarded the selection with no effect — now the clicked square becomes the new origin square, so the player no longer loses orientation.", de: "Fix (Viz-Modus): Illegaler 2. Klick (z.B. a1 → c3 ohne legalen Zug) verwarf die Auswahl wirkungslos — jetzt wird das geklickte Feld zum neuen Ausgangsfeld, der Spieler verliert die Orientierung nicht mehr." },
    { en: "Fix: The share puzzle button was hidden (CSS top/right -8px was clipped by Material card clipping) and pushed down by the stats card in Endless mode. Position corrected to 0/0, pulled above the stats card in Endless mode.", de: "Fix: Share-Puzzle-Button war versteckt (CSS top/right -8px wurde durch Material-Card-Clipping abgeschnitten) und im Endless-Modus durch die Stats-Card nach unten verdrängt. Position auf 0/0 korrigiert, im Endless-Modus über die Stats-Card gezogen." },
    { en: "Feature: Share puzzle now also in book puzzle mode (was not available before).", de: "Feature: Share-Puzzle jetzt auch im Buch-Puzzle-Modus (war bisher nicht vorhanden)." },
  ]},
  { version: "0.40.3", date: "2026-06-01", changes: [
    { en: "Fix: After a mouseslip the correct solution move was no longer recognized — onSolutionPath stayed false and the state stayed PLAYING, so every following user move landed in the off-path handler. Mouseslip now cleanly restores the solution path (state=AWAITING_USER_MOVE, the wrong move removed from the move log).", de: "Fix: Nach Mouseslip wurde der korrekte Lösungszug nicht mehr erkannt — onSolutionPath blieb auf false und der State auf PLAYING, dadurch landete jeder folgende User-Zug im off-path-Handler. Mouseslip stellt jetzt den Lösungspfad sauber wieder her (state=AWAITING_USER_MOVE, Fehlzug aus dem Move-Log entfernt)." },
    { en: "Fix: Race condition on reset/mouseslip while Stockfish is still thinking — a late-arriving solver move could pollute the freshly reset board. A solver epoch now invalidates running Stockfish calls, the late move is discarded.", de: "Fix: Race-Condition bei Reset/Mouseslip während Stockfish noch denkt — ein verspätet ankommender Solver-Zug konnte das frisch zurückgesetzte Brett verschmutzen. Eine Solver-Epoch invalidiert jetzt laufende Stockfish-Aufrufe, der späte Zug wird verworfen." },
  ]},
  { version: "0.40.2", date: "2026-06-01", changes: [
    { en: "Fix: Multi-move puzzles no longer ended after the correct solution — the second/last solution move was wrongly treated as an off-path move because the solver switched to PLAYING instead of AWAITING_USER_MOVE after the Stockfish reply (regression from the BasePuzzleSolver refactor v0.38.2).", de: "Fix: Mehrzügige Puzzles beendeten nach korrekter Lösung nicht mehr — der zweite/letzte Lösungszug wurde fälschlich als off-path-Zug behandelt, weil der Solver nach der Stockfish-Antwort auf PLAYING statt AWAITING_USER_MOVE wechselte (Regression aus dem BasePuzzleSolver-Refactor v0.38.2)." },
  ]},
  { version: "0.40.1", date: "2026-06-01", changes: [
    { en: "Fix (infra): The nginx frontend crashed on startup (\"host not found in upstream api\") when the api container wasn't yet in DNS after a reboot → 502 across the whole site. The upstream is now resolved at runtime via Docker's DNS (resolver + variable in proxy_pass), so nginx starts reliably and automatically handles api restarts (new IP).", de: "Fix (Infra): nginx-Frontend stuerzte beim Start ab (\"host not found in upstream api\"), wenn der api-Container nach einem Reboot noch nicht im DNS war → 502 auf der ganzen Seite. Upstream wird jetzt zur Laufzeit ueber Dockers DNS aufgeloest (resolver + Variable im proxy_pass), nginx startet dadurch zuverlaessig und faengt api-Neustarts (neue IP) automatisch ab." },
  ]},
  { version: "0.40.0", date: "2026-05-31", changes: [
    { en: "Feature: Per-visualization-level Elo ratings — each level (0-4) has its own puzzle Elo. Default: 1500/1400/1300/1200/1100. Switching levels in the slider loads stats for the selected level.", de: "Feature: Per-Visualisierungslevel Elo-Ratings — jede Stufe (0-4) hat eigenes Puzzle-Elo. Default: 1500/1400/1300/1200/1100. Level-Wechsel im Slider laedt Stats fuer das gewaehlte Level." },
  ]},
  { version: "0.39.2", date: "2026-05-31", changes: [
    { en: "UI: Countdown 3s instead of 5s at levels 2-4. The Show button reveals pieces for 3 seconds (click instead of hold).", de: "UI: Countdown 3s statt 5s bei Level 2-4. Show-Button zeigt Figuren fuer 3 Sekunden (Klick statt Halten)." },
  ]},
  { version: "0.39.1", date: "2026-05-31", changes: [
    { en: "UI: Viz card in desktop mode to the right of the board (info section), below it on mobile (single-column layout).", de: "UI: Viz-Card im Desktop-Modus rechts neben dem Brett (info-section), auf Mobile darunter (einspaltiges Layout)." },
  ]},
  { version: "0.39.0", date: "2026-05-31", changes: [
    { en: "Feature: Visualization 5-level slider (level 0-4) — Normal, Blindfold, Checker (colored pieces), Dark Checker (black pieces), Invisible (completely invisible). At levels 2-4 the pieces disappear after a 5s countdown; the Show button reveals them temporarily. After the puzzle ends, pieces are shown normally.", de: "Feature: Visualisierung 5-Stufen-Slider (Level 0-4) — Normal, Blindfold, Checker (farbige Spielsteine), Dark Checker (schwarze Steine), Invisible (komplett unsichtbar). Bei Level 2-4 verschwinden Figuren nach 5s Countdown; Show-Button blendet sie temporaer ein. Nach Puzzle-Ende werden Figuren normal angezeigt." },
    { en: "UI: Viz card (move list + Show button + countdown) directly below the board for a better mobile view.", de: "UI: Viz-Card (Zugliste + Show-Button + Countdown) direkt unter dem Brett fuer bessere Mobile-Ansicht." },
  ]},
  { version: "0.38.3", date: "2026-05-31", changes: [
    { en: "Fix: Mouseslip in visualization mode — taken-back moves are now also removed from the SAN move list (previously mouseslip appeared to have no effect, since the board stays frozen).", de: "Fix: Mouseslip im Visualisierungs-Modus — zurückgenommene Züge werden jetzt auch aus der SAN-Zugliste entfernt (vorher schien Mouseslip wirkungslos, da das Brett eingefroren bleibt)." },
  ]},
  { version: "0.38.2", date: "2026-05-31", changes: [
    { en: "Refactor: The shared solving state machine of the 3 puzzle modes (setup, move handling, Stockfish takeover, mouseslip, board presentation, visualization) consolidated into BasePuzzleSolver — previously duplicated ~3x (~750 fewer lines in the components). Mode differences run via hooks. Behaviorally identical, easier to maintain.", de: "Refactor: Gemeinsamer Lös-Automat der 3 Puzzle-Modi (Setup, Zug-Handling, Stockfish-Übernahme, Mouseslip, Brett-Präsentation, Visualisierung) in BasePuzzleSolver zusammengefasst — vorher ~3x dupliziert (~750 Zeilen weniger in den Komponenten). Modus-Unterschiede laufen über Hooks. Verhaltensgleich, leichter wartbar." },
  ]},
  { version: "0.38.1", date: "2026-05-31", changes: [
    { en: "Refactor: Shared chess/board helpers (parseUci, applyUci, tryFreeMove, calcDests, SAN move list) extracted from the 3 puzzle modes into puzzle-move.util.ts (previously duplicated byte-for-byte 3x) — easier to maintain.", de: "Refactor: Gemeinsame Schach-/Brett-Helfer (parseUci, applyUci, tryFreeMove, calcDests, SAN-Zugliste) aus den 3 Puzzle-Modi in puzzle-move.util.ts ausgelagert (vorher byte-gleich 3x dupliziert) — leichter wartbar." },
  ]},
  { version: "0.38.0", date: "2026-05-31", changes: [
    { en: "Feature: Visualization mode (Blindfold) in all 3 puzzle modes (Normal, Endless, Book) — as a toggle in the settings. The board stays on the starting position; your own moves are entered by click (from square → target square) but not shown; the opponent/solution move appears as SAN text. Trains calculating/visualizing in your head.", de: "Feature: Visualisierungs-Modus (Blindfold) in allen 3 Puzzle-Modi (Normal, Endless, Buch) — als Schalter in den Einstellungen. Das Brett bleibt auf der Startstellung; eigene Züge werden per Klick (Von-Feld → Ziel-Feld) eingegeben, aber nicht angezeigt; Gegner-/Lösungszug erscheint als SAN-Text. Trainiert das Rechnen/Visualisieren im Kopf." },
  ]},
  { version: "0.37.2", date: "2026-05-31", changes: [
    { en: "Feature (chess bot): /puzzle shows only a link by default (no board image/solution). Each user can toggle with /puzzle option:showBoard or hideBoard.", de: "Feature (Schach-Bot): /puzzle zeigt standardmaessig nur Link (kein Brettbild/Loesung). Jeder User kann mit /puzzle option:showBoard bzw. hideBoard umschalten." },
  ]},
  { version: "0.37.1", date: "2026-05-31", changes: [
    { en: "Feature: Solution review right after the puzzle ends — arrow keys (left/right) and GUI buttons to click through the solution without a prior \"Show Solution\".", de: "Feature: Loesungs-Review sofort nach Puzzle-Ende — Pfeiltasten (links/rechts) und GUI-Buttons zum Durchklicken der Loesung ohne vorheriges \"Show Solution\"." },
    { en: "Feature: \"View whole game\" only visible once the puzzle is done (SOLVED/FAILED).", de: "Feature: \"Ganze Partie ansehen\" erst sichtbar wenn Puzzle fertig (SOLVED/FAILED)." },
  ]},
  { version: "0.37.0", date: "2026-05-31", changes: [
    { en: "Feature: Theme mode — Normal (as before), Random (random board theme + piece set per puzzle), Crazy (every square a different color, every piece type a different set).", de: "Feature: Theme-Modus — Normal (wie bisher), Random (zufaelliges Brett-Theme + Figurensatz pro Puzzle), Crazy (jedes Feld eine andere Farbe, jede Figurenart ein anderer Satz)." },
    { en: "Feature: Mode selection as chips in the settings of all 3 puzzle modes (Normal, Endless, Book). Board/piece picker only visible in Normal.", de: "Feature: Modus-Auswahl als Chips in den Einstellungen aller 3 Puzzle-Modi (Normal, Endless, Buch). Board-/Figuren-Picker nur bei Normal sichtbar." },
    { en: "Refactor: BOARD_THEMES and PIECE_SETS extracted into shared board-theme.util.ts (previously duplicated 3x).", de: "Refactor: BOARD_THEMES und PIECE_SETS in gemeinsame board-theme.util.ts ausgelagert (vorher 3x dupliziert)." },
  ]},
  { version: "0.36.1", date: "2026-05-31", changes: [
    { en: "Fix: The pawn promotion selection now shows the correct pieces — cburnett SVGs fully vendored locally (previously an external GitHub request that failed and left all 4 options looking empty/identical).", de: "Fix: Bauernumwandlungs-Auswahl zeigt jetzt die korrekten Figuren — cburnett-SVGs komplett lokal gevendort (vorher externer GitHub-Request, der fehlschlug und alle 4 Optionen leer/gleich aussehen liess)." },
  ]},
  { version: "0.36.0", date: "2026-05-31", changes: [
    { en: "Feature: Solution review after the puzzle ends — \"Show Solution\" opens step-through instead of auto-play in all 3 puzzle modes (Normal, Endless, Book).", de: "Feature: Loesungs-Review nach Puzzle-Ende — \"Show Solution\" oeffnet Step-Through statt Auto-Play in allen 3 Puzzle-Modi (Normal, Endless, Buch)." },
    { en: "Feature: Arrow buttons and arrow keys (left/right) to manually step through the solution.", de: "Feature: Pfeil-Buttons und Pfeiltasten (links/rechts) zum manuellen Durchsteppen der Loesung." },
    { en: "Feature: Book puzzle distinguishes \"Show Solution\" (only solution moves from the training start) and \"View whole game\" (all moves).", de: "Feature: Buch-Puzzle unterscheidet \"Show Solution\" (nur Loesungszuege ab Trainingsstart) und \"Ganze Partie ansehen\" (alle Zuege)." },
  ]},
  { version: "0.35.3", date: "2026-05-31", changes: [
    { en: "Fix: Book puzzles with FEN = puzzle position (e.g. \"1001 Chess Exercises\") now start correctly at the first solution move (StartPly=-1) instead of playing away the first move as \"setup\".", de: "Fix: Buch-Puzzles mit FEN = Puzzle-Stellung (z.B. „1001 Chess Exercises\") starten jetzt korrekt beim ersten Lösungszug (StartPly=-1) statt den ersten Zug als „Setup\" wegzuspielen." },
    { en: "Fix: Whole-game entries without a training marker (FEN = starting position, no [%tqu]) are skipped on import (no apparent puzzle from the opening).", de: "Fix: Ganze-Partie-Einträge ohne Trainingsmarker (FEN = Grundstellung, kein [%tqu]) werden beim Import übersprungen (kein scheinbares Puzzle ab Eröffnung)." },
  ]},
  { version: "0.35.2", date: "2026-05-31", changes: [
    { en: "Fix: After retry/reset/mouseslip \"Alternative solution\" was wrongly shown — alternativeSolve is now reset in setupPuzzle (all 3 puzzle modes).", de: "Fix: Nach Retry/Reset/Mouseslip wurde faelschlicherweise \"Alternative Loesung\" angezeigt — alternativeSolve wird jetzt in setupPuzzle zurueckgesetzt (alle 3 Puzzle-Modi)." },
  ]},
  { version: "0.35.1", date: "2026-05-31", changes: [
    { en: "Fix: Book puzzles start at the training comment — the ChessBase [%tqu] marker is recognized on import (new field StartPly); the board fast-forwards to the training position and solves from the marked move (previously the whole opening was \"solved\" from the starting position).", de: "Fix: Buch-Puzzles starten am Trainingskommentar — ChessBase-[%tqu]-Marker wird beim Import erkannt (neues Feld StartPly); das Brett spult bis zur Trainingsstellung vor und löst ab dem markierten Zug (vorher wurde die ganze Eröffnung ab Grundstellung „gelöst\")." },
    { en: "Feature: \"View whole game\" in the book puzzle — the complete game can be clicked through via ◀/▶; the prior history is preserved.", de: "Feature: „Ganze Partie ansehen\" im Buch-Puzzle — komplette Partie per ◀/▶ durchklickbar; Vorgeschichte bleibt erhalten." },
  ]},
  { version: "0.35.0", date: "2026-05-31", changes: [
    { en: "Feature: User preferences server sync — board theme, piece set, Stockfish depth, difficulty and book Stockfish depth are stored server-side and synced across devices.", de: "Feature: User-Preferences Server-Sync — Board Theme, Piece Set, Stockfish Depth, Schwierigkeit und Book Stockfish Depth werden serverseitig gespeichert und geraeteuebergreifend synchronisiert." },
    { en: "Feature: New PreferencesService — central service for all puzzle settings with localStorage + server persistence.", de: "Feature: Neuer PreferencesService — zentraler Service fuer alle Puzzle-Einstellungen mit localStorage + Server-Persistierung." },
    { en: "API: GET/PUT /api/profile now returns/accepts 5 new preference fields (boardTheme, pieceSet, stockfishDepth, puzzleDifficulty, bookStockfishDepth).", de: "API: GET/PUT /api/profile liefert/akzeptiert jetzt 5 neue Preference-Felder (boardTheme, pieceSet, stockfishDepth, puzzleDifficulty, bookStockfishDepth)." },
    { en: "Sync: After login server preferences are loaded automatically and overwrite local values.", de: "Sync: Nach Login werden Server-Preferences automatisch geladen und ueberschreiben lokale Werte." },
    { en: "Fix: cburnett piece preview vendored locally — no more external GitHub request.", de: "Fix: cburnett-Figurenvorschau lokal gevendort — kein externer GitHub-Request mehr." },
  ]},
  { version: "0.34.1", date: "2026-05-31", changes: [
    { en: "Fix: Version + changelog from a shared source (changelog.ts) — the production build now shows the correct version/changelog (environment.prod.ts had been frozen at 0.26.1 since v0.26.0).", de: "Fix: Version + Changelog aus gemeinsamer Quelle (changelog.ts) — der Production-Build zeigt jetzt die korrekte Version/Changelog (environment.prod.ts war seit v0.26.0 auf 0.26.1 eingefroren)." },
  ]},
  { version: "0.34.0", date: "2026-05-31", changes: [
    { en: "Feature: Group system — admin can create/delete groups and assign users (admin tab \"Groups\"). Basis for future group-dependent display (GET /api/my-groups).", de: "Feature: Gruppensystem — Admin kann Gruppen anlegen/löschen und User zuordnen (Admin-Tab „Gruppen\"). Basis für künftige gruppenabhängige Anzeige (GET /api/my-groups)." },
  ]},
  { version: "0.33.0", date: "2026-05-31", changes: [
    { en: "Feature: Admin books — specify a recommended Elo range (from/to) per book (Book.MinElo/MaxElo, migration, input in the books tab).", de: "Feature: Admin-Bücher — pro Buch eine empfohlene Elo-Spanne (von/bis) angeben (Book.MinElo/MaxElo, Migration, Eingabe im Bücher-Tab)." },
  ]},
  { version: "0.32.1", date: "2026-05-31", changes: [
    { en: "UI: Dashboard — puzzles card first (with an additional Endless link), then Tournaments, Friends, Repertoires.", de: "UI: Dashboard — Puzzles-Karte zuerst (mit zusätzlichem Endless-Link), dann Tournaments, Friends, Repertoires." },
  ]},
  { version: "0.32.0", date: "2026-05-31", changes: [
    { en: "UI: The settings gear sits in the status box (top right); on opening, the page scrolls (mobile) to the settings block.", de: "UI: Einstellungs-Zahnrad sitzt in der Status-Box (oben rechts); beim Öffnen scrollt die Seite (mobil) zum Einstellungsblock." },
  ]},
  { version: "0.31.2", date: "2026-05-31", changes: [
    { en: "UI: Settings gear as a compact icon at the top right of the info panel (instead of a button row).", de: "UI: Einstellungen-Zahnrad als kompaktes Icon oben rechts im Info-Panel (statt Button-Zeile)." },
  ]},
  { version: "0.31.1", date: "2026-05-31", changes: [
    { en: "Fix: Filter field spacing — hint text no longer overlaps with the next field", de: "Fix: Filter-Felder Abstand — Hint-Text ueberlappt nicht mehr mit dem naechsten Feld" },
  ]},
  { version: "0.31.0", date: "2026-05-31", changes: [
    { en: "UI: Board/piece selection chips show the actual texture or piece as a preview.", de: "UI: Brett-/Figuren-Auswahl-Chips zeigen die echte Textur bzw. Figur als Vorschau." },
    { en: "UI: Filter and display settings (difficulty, depth, board, pieces) placed behind a gear/\"Settings\" toggle (collapsed by default) — in Normal, Endless and Book puzzle.", de: "UI: Filter- und Darstellungs-Einstellungen (Schwierigkeit, Tiefe, Brett, Figuren) hinter ein Zahnrad/„Einstellungen\"-Toggle gelegt (Default eingeklappt) — in Normal-, Endless- und Buch-Puzzle." },
  ]},
  { version: "0.30.0", date: "2026-05-31", changes: [
    { en: "Feature: Piece selection with mini preview (knight) in the chip; additional sets Celtic, Chessnut, RhosGFX (MIT/Apache/CC0).", de: "Feature: Figuren-Auswahl mit Mini-Vorschau (Springer) im Chip; zusätzliche Sets Celtic, Chessnut, RhosGFX (MIT/Apache/CC0)." },
    { en: "Feature: Additional board textures Leather and Maple.", de: "Feature: Zusätzliche Brett-Texturen Leder und Ahorn." },
  ]},
  { version: "0.29.0", date: "2026-05-31", changes: [
    { en: "Feature: Selectable piece sets — Classic (cburnett), Merida, Fantasy, Spatial (vendored locally, MIT/GPL).", de: "Feature: Figuren-Sets wählbar — Classic (cburnett), Merida, Fantasy, Spatial (lokal gevendort, MIT/GPL)." },
    { en: "Feature: New board textures — Wood, Water, Marble, Metal (genuine Lichess textures).", de: "Feature: Neue Brett-Texturen — Holz, Wasser, Marmor, Metall (echte Lichess-Texturen)." },
    { en: "Selection for pieces + board in all puzzle modes (Normal, Endless, Book), remembered in localStorage.", de: "Auswahl für Figuren + Brett in allen Puzzle-Modi (Normal, Endless, Buch), in localStorage gemerkt." },
  ]},
  { version: "0.28.2", date: "2026-05-31", changes: [
    { en: "Fix: Book puzzle import (legacy JSON) truncates BookFileName to 200 characters and skips empty file names — prevents DB errors + ghost books (code review).", de: "Fix: Buch-Puzzle-Import (Legacy-JSON) kürzt BookFileName auf 200 Zeichen und überspringt leere Dateinamen — verhindert DB-Fehler + Geister-Bücher (Code-Review)." },
    { en: "Fix: Difficulty dropdown — the first puzzle only after the Elo is loaded (no longer default 1500); the rating window is clamped to the actual DB rating range (no empty results/retry loop); invalid localStorage value is ignored (code review).", de: "Fix: Schwierigkeits-Dropdown — erstes Puzzle erst nach Laden der Elo (nicht mehr Default 1500); Rating-Fenster wird auf den echten DB-Rating-Bereich geklemmt (keine leeren Ergebnisse/Retry-Schleife); ungültiger localStorage-Wert wird ignoriert (Code-Review)." },
  ]},
  { version: "0.28.1", date: "2026-05-31", changes: [
    { en: "Fix: Board themes — chessboard rendered correctly as 8x8 instead of 4 quadrants (background-size: 25%)", de: "Fix: Board Themes — Schachbrett korrekt als 8x8 statt 4 Quadranten gerendert (background-size: 25%)" },
    { en: "Fix: Board theme contrast increased — Blue, Green, Gray, Wood with clearly distinguishable square colors", de: "Fix: Board Theme Kontrast erhoeht — Blue, Green, Gray, Wood mit deutlich unterscheidbaren Feldfarben" },
  ]},
  { version: "0.28.0", date: "2026-05-31", changes: [
    { en: "Feature: Difficulty dropdown for the puzzles — delivers puzzles around your own Elo: Normal (±100), Easy (−300), Very easy (−600), Hard (+300), Very hard (+600).", de: "Feature: Schwierigkeits-Dropdown bei den Puzzles — liefert Puzzles rund um die eigene Elo: Normal (±100), Leicht (−300), Sehr leicht (−600), Schwer (+300), Sehr schwer (+600)." },
    { en: "Feature: The rating window is calculated from your own puzzle Elo + difficulty offset (replaces the manual min/max rating fields); the selection is remembered in localStorage.", de: "Feature: Rating-Fenster wird aus eigener Puzzle-Elo + Schwierigkeits-Offset berechnet (ersetzt die manuellen Min/Max-Rating-Felder); Auswahl wird in localStorage gemerkt." },
  ]},
  { version: "0.27.1", date: "2026-05-31", changes: [
    { en: "Fix: Legacy book puzzle import (POST /api/admin/book-puzzles/import) now creates a Book per file and sets BookId — so script-imported puzzles also appear in the pools (random/daily/blind) and in the admin books tab (code review).", de: "Fix: Legacy-Buch-Puzzle-Import (POST /api/admin/book-puzzles/import) legt jetzt pro Datei ein Book an und setzt BookId — so erscheinen auch per Skript importierte Puzzles in den Pools (random/daily/blind) und im Admin-Bücher-Tab (Code-Review)." },
  ]},
  { version: "0.27.0", date: "2026-05-31", changes: [
    { en: "Feature: Puzzle Elo rating system — dynamic Elo number based on puzzle difficulty", de: "Feature: Puzzle Elo Rating System — dynamische Elo-Zahl basierend auf Puzzle-Schwierigkeit" },
    { en: "Feature: K-factor 40 for the first 30 attempts (provisional), then K=20", de: "Feature: K-Faktor 40 fuer erste 30 Versuche (provisorisch), danach K=20" },
    { en: "Feature: Elo change (+N green / -N red) shown after each puzzle", de: "Feature: Elo-Aenderung (+N gruen / -N rot) nach jedem Puzzle angezeigt" },
    { en: "Feature: Elo visible in the stats grid and dashboard card", de: "Feature: Elo in Stats-Grid und Dashboard-Card sichtbar" },
    { en: "API: PuzzleElo on AppUser, EloAfter/EloChange on PuzzleAttempt", de: "API: PuzzleElo auf AppUser, EloAfter/EloChange auf PuzzleAttempt" },
    { en: "Tests: 10 new tests for Elo calculation, K-factor, floor, stats, history", de: "Tests: 10 neue Tests fuer Elo-Berechnung, K-Faktor, Floor, Stats, History" },
  ]},
  { version: "0.26.2", date: "2026-05-31", changes: [
    { en: "Fix: Swagger UI always active (no longer only in Development) — the API is only reachable on the internal network.", de: "Fix: Swagger-UI immer aktiv (nicht mehr nur in Development) — die API ist nur im internen Netz erreichbar." },
  ]},
  { version: "0.26.1", date: "2026-05-31", changes: [
    { en: "Feature: Pawn promotion dialog — on promotion an overlay with 4 pieces (queen, rook, bishop, knight) appears instead of automatic queen selection", de: "Feature: Bauernumwandlungs-Dialog — bei Promotion erscheint ein Overlay mit 4 Figuren (Dame, Turm, Laeufer, Springer) statt automatischer Dame-Wahl" },
    { en: "Feature: Board themes — 5 board color schemes: Brown (standard), Blue, Green, Gray, Wood", de: "Feature: Board Themes — 5 Brett-Farbschemata: Brown (Standard), Blue, Green, Gray, Wood" },
    { en: "Feature: Theme selection in all puzzle modes (Normal, Endless, Book) with color preview chips", de: "Feature: Theme-Auswahl in allen Puzzle-Modi (Normal, Endless, Book) mit Farbvorschau-Chips" },
    { en: "Feature: Board theme is stored in localStorage and persists after refresh", de: "Feature: Board-Theme wird in localStorage gespeichert und bleibt nach Refresh erhalten" },
    { en: "Fix: Promotion moves are compared correctly with UCI notation (incl. promotion letter)", de: "Fix: Promotion-Zuege werden korrekt mit UCI-Notation verglichen (inkl. Promotion-Buchstabe)" },
  ]},
  { version: "0.26.0", date: "2026-05-30", changes: [
    { en: "Feature: Book puzzles as a RookHub feature — admin uploads PGN files as \"books\" (server-side parsing, SAN→UCI via Gera.Chess).", de: "Feature: Buch-Puzzles als RookHub-Feature — Admin lädt PGN-Dateien als \"Bücher\" hoch (serverseitiges Parsing, SAN→UCI via Gera.Chess)." },
    { en: "Feature: Admin tab \"Books\" — list of all imported books with puzzle count and pool switches Daily/Random/Blind per book.", de: "Feature: Admin-Tab \"Bücher\" — Liste aller importierten Bücher mit Puzzle-Count und Pool-Schaltern Daily/Random/Blind pro Buch." },
    { en: "API: GET /api/book-puzzles/random?pool=daily|random|blind&exclude=… — random book puzzle from the pool; daily is deterministic per UTC day (shared daily puzzle).", de: "API: GET /api/book-puzzles/random?pool=daily|random|blind&exclude=… — zufälliges Buch-Puzzle aus dem Pool; daily ist deterministisch pro UTC-Tag (gemeinsames Tagespuzzle)." },
    { en: "API: POST /api/admin/books/import (PGN upload), GET/PUT/DELETE /api/admin/books — book management incl. pool flags.", de: "API: POST /api/admin/books/import (PGN-Upload), GET/PUT/DELETE /api/admin/books — Buch-Verwaltung inkl. Pool-Flags." },
    { en: "DB: New Book entity (flags ForDaily/ForRandom/ForBlind + metadata), BookPuzzle.BookId; migration with backfill for legacy data.", de: "DB: Neue Book-Entität (Flags ForDaily/ForRandom/ForBlind + Metadaten), BookPuzzle.BookId; Migration mit Backfill für Altbestand." },
    { en: "Tests: 27 new tests (PGN parser incl. promotion/castling/variations/skip rules, random pool selection, book management).", de: "Tests: 27 neue Tests (PGN-Parser inkl. Promotion/Rochade/Varianten/Skip-Regeln, random-Pool-Auswahl, Buch-Verwaltung)." },
  ]},
  { version: "0.25.5", date: "2026-05-30", changes: [
    { en: "UI: Mobile optimization — padding, wrapping and layout adjusted for all views", de: "UI: Mobile-Optimierung — Padding, Wrapping und Layout fuer alle Views angepasst" },
    { en: "UI: Endless History — card layout instead of table on mobile devices", de: "UI: Endless History — Card-Layout statt Tabelle auf Mobilgeraeten" },
    { en: "UI: Friends — search field and button stack correctly on mobile", de: "UI: Friends — Suchfeld und Button stacken korrekt auf Mobile" },
    { en: "UI: Profile — form fields stack on mobile, search button full width", de: "UI: Profile — Formularfelder stacken auf Mobile, Suchbutton volle Breite" },
    { en: "UI: Endless puzzle — config/game-over/help overlay mobile-optimized", de: "UI: Endless Puzzle — Config/GameOver/Help-Overlay mobile-optimiert" },
    { en: "Logging: Screen resolution (ScreenWidth/ScreenHeight) is logged to Elasticsearch on puzzle attempts", de: "Logging: Bildschirmaufloesung (ScreenWidth/ScreenHeight) wird bei Puzzle-Attempts nach Elasticsearch geloggt" },
  ]},
  { version: "0.25.4", date: "2026-05-30", changes: [
    { en: "Feature: Endless run resume — on page refresh a running run is offered with a Continue button", de: "Feature: Endless Run Resume — bei Seiten-Refresh wird ein laufender Run mit Continue-Button angeboten" },
    { en: "Feature: Archive & New Game — archive the running run and start over", de: "Feature: Archive & New Game — laufenden Run archivieren und neu starten" },
    { en: "Feature: Archive button on the game-over/exhausted screen for immediate archiving", de: "Feature: Archive-Button auf Game-Over/Exhausted-Screen fuer sofortige Archivierung" },
    { en: "API: recordSessionToServer now returns the session ID", de: "API: recordSessionToServer gibt jetzt Session-ID zurueck" },
  ]},
  { version: "0.25.3", date: "2026-05-30", changes: [
    { en: "Fix: Data Protection persists keys to a mounted volume (/keys, dedicated dataprotection-keys volume) instead of in-memory — no more \"No XML encryptor / ephemeral key\" warnings on API startup.", de: "Fix: Data Protection persistiert Keys auf gemountetes Volume (/keys, eigenes dataprotection-keys-Volume) statt In-Memory — keine \"No XML encryptor / ephemeral key\"-Warnings mehr beim API-Start." },
    { en: "Fix: compose.dev.vpn.yml — API waits for gluetun (service_healthy); ES/Kibana host port defaults 9201/5602 instead of 9200/5601 (no collision with the prod stack on the same host).", de: "Fix: compose.dev.vpn.yml — API wartet auf gluetun (service_healthy); ES/Kibana Host-Port-Defaults 9201/5602 statt 9200/5601 (keine Kollision mit Prod-Stack auf demselben Host)." },
    { en: "Fix: .env.dev.vpn.example new + .gitignore — dev-specific ports and separate bind paths (prevents shared ES data directory/node.lock).", de: "Fix: .env.dev.vpn.example neu + .gitignore — dev-eigene Ports und getrennte Bind-Pfade (verhindert geteiltes ES-Datenverzeichnis/node.lock)." },
    { en: "Fix: Crawler Dockerfile — redundant ASPNETCORE_URLS removed (binding via base image HTTP_PORTS=8080); removes \"Overriding HTTP_PORTS\" warning.", de: "Fix: Crawler-Dockerfile — redundantes ASPNETCORE_URLS entfernt (Bindung via Base-Image HTTP_PORTS=8080); entfernt \"Overriding HTTP_PORTS\"-Warning." },
  ]},
  { version: "0.25.2", date: "2026-05-30", changes: [
    { en: "UI: Puzzle History linked in the user menu (profile picture at top right)", de: "UI: Puzzle History im User-Menue (Profilbild oben rechts) verlinkt" },
  ]},
  { version: "0.25.1", date: "2026-05-30", changes: [
    { en: "Feature: Endless session archiving — sessions can be archived/unarchived", de: "Feature: Endless Session Archivierung — Sessions koennen archiviert/unarchiviert werden" },
    { en: "Feature: Archived sessions no longer feed into the sync response (fasttrack/session count)", de: "Feature: Archivierte Sessions fliessen nicht mehr in Sync-Response (Fasttrack/Session-Count)" },
    { en: "Feature: History filter: All / Active / Archived sessions", de: "Feature: History-Filter: Alle / Aktive / Archivierte Sessions" },
    { en: "Feature: Multi-selection with checkboxes in the history table", de: "Feature: Mehrfachauswahl mit Checkboxen in der History-Tabelle" },
    { en: "API: New endpoint POST /api/endless/archive with bulk archiving", de: "API: Neuer Endpoint POST /api/endless/archive mit Bulk-Archivierung" },
    { en: "API: GET /api/endless/history supports a new query parameter archived (bool)", de: "API: GET /api/endless/history unterstuetzt neuen Query-Parameter archived (bool)" },
    { en: "Tests: 5 new tests for archive/unarchive/filter/sync exclusion", de: "Tests: 5 neue Tests fuer Archive/Unarchive/Filter/Sync-Exclusion" },
  ]},
  { version: "0.25.0", date: "2026-05-30", changes: [
    { en: "Feature: Endless Puzzle History — paginated overview of all past sessions", de: "Feature: Endless Puzzle History — paginierte Uebersicht aller vergangenen Sessions" },
    { en: "Feature: Authenticated users no longer have a 50-session limit (unlimited history)", de: "Feature: Authentifizierte User haben kein 50-Session-Limit mehr (unbegrenzte History)" },
    { en: "API: New endpoint GET /api/endless/history with pagination (page, pageSize)", de: "API: Neuer Endpoint GET /api/endless/history mit Pagination (page, pageSize)" },
    { en: "Tests: 5 new tests for history pagination and trim behavior", de: "Tests: 5 neue Tests fuer History-Pagination und Trim-Verhalten" },
  ]},
  { version: "0.24.1", date: "2026-05-30", changes: [
    { en: "Observability: Puzzle attempts as structured Serilog events to Elasticsearch", de: "Observability: Puzzle-Attempts als strukturierte Serilog-Events nach Elasticsearch" },
    { en: "Kibana: 2 new dashboard panels (Puzzles Solved 24h, Puzzles per User)", de: "Kibana: 2 neue Dashboard-Panels (Puzzles Solved 24h, Puzzles per User)" },
  ]},
  { version: "0.24.0", date: "2026-05-30", changes: [
    { en: "Feature: Puzzle MoveLog — played moves, expected moves and thinking time per move are tracked", de: "Feature: Puzzle MoveLog — gespielte Zuege, erwartete Zuege und Denkzeit pro Zug werden getrackt" },
    { en: "Feature: Endless mode now sends the correct puzzle duration (timeSpentSeconds) instead of 0", de: "Feature: Endless Mode sendet jetzt korrekte Puzzle-Dauer (timeSpentSeconds) statt 0" },
    { en: "API: MoveLog field (JSON) on PuzzleAttempt, in RecordAttempt and history endpoints", de: "API: MoveLog-Feld (JSON) auf PuzzleAttempt, in RecordAttempt und History-Endpoints" },
  ]},
  { version: "0.23.3", date: "2026-05-30", changes: [
    { en: "Fix: init-kibana.sh creates data views with allowNoIndex:true — creation is now timing-independent (works even if the log index doesn't yet exist during the init run). Fixes empty Kibana (no data views/dashboard) on a fresh stack start.", de: "Fix: init-kibana.sh legt Data Views mit allowNoIndex:true an — Erstellung jetzt timing-unabhaengig (funktioniert auch, wenn der Log-Index beim init-Lauf noch nicht existiert). Behebt leeres Kibana (keine Data Views/Dashboard) bei frischem Stack-Start." },
    { en: "Fix: kibana-init as an idle sidecar (restart: unless-stopped, idles after init) instead of one-shot — the stack no longer shows \"partially running\" in Arcane/Portainer. Init runs idempotently on every stack start.", de: "Fix: kibana-init als Idle-Sidecar (restart: unless-stopped, idlet nach dem Init) statt One-Shot — Stack zeigt in Arcane/Portainer nicht mehr \"partially running\". Init laeuft idempotent bei jedem Stack-Start." },
  ]},
  { version: "0.23.2", date: "2026-05-29", changes: [
    { en: "Fix: nginx PID path changed to /tmp/nginx.pid (non-root container fix)", de: "Fix: nginx PID-Pfad auf /tmp/nginx.pid geaendert (non-root Container Fix)" },
  ]},
  { version: "0.23.1", date: "2026-05-29", changes: [
    { en: "Fix: FriendController search minimum from 3 to 2 characters (frontend/API consistency)", de: "Fix: FriendController Search-Minimum von 3 auf 2 Zeichen (Frontend/API-Konsistenz)" },
    { en: "Fix: Email is normalized on registration (lowercase, trimmed)", de: "Fix: Email wird bei Registrierung normalisiert (lowercase, getrimmt)" },
    { en: "Fix: LIKE wildcards (%, _) cleaned in friend search", de: "Fix: LIKE-Wildcards (%, _) in Friend-Suche bereinigt" },
    { en: "Fix: JWT base64url decoding corrected in AuthService", de: "Fix: JWT base64url-Dekodierung in AuthService korrigiert" },
    { en: "Perf: RepertoireService.UpdateAsync — unnecessary Include(Files) replaced by CountAsync", de: "Perf: RepertoireService.UpdateAsync — unnoetige Include(Files) durch CountAsync ersetzt" },
  ]},
  { version: "0.23.0", date: "2026-05-29", changes: [
    { en: "Security: SessionId validation via regex in EndlessController + DTOs (like PuzzleController)", de: "Security: SessionId-Validierung per Regex in EndlessController + DTOs (wie PuzzleController)" },
    { en: "Security: Repertoire limits — max 50 per user, max 100 files per repertoire", de: "Security: Repertoire-Limits — max 50 pro User, max 100 Files pro Repertoire" },
    { en: "Security: nginx runs as non-root user (port 8080 internal)", de: "Security: nginx laeuft als non-root User (Port 8080 intern)" },
    { en: "Security: init-db.sh — GRANT ALL replaced with specific privileges", de: "Security: init-db.sh — GRANT ALL durch spezifische Privileges ersetzt" },
  ]},
  { version: "0.22.9", date: "2026-05-29", changes: [
    { en: "Perf: TournamentDetail — template getters replaced with cached properties (no array rebuild per change detection)", de: "Perf: TournamentDetail — Template-Getter durch gecachte Properties ersetzt (kein Array-Rebuild pro Change Detection)" },
  ]},
  { version: "0.22.8", date: "2026-05-29", changes: [
    { en: "Fix: Missing error handlers on HTTP calls in RepertoireList, TournamentDetail — SnackBar feedback on errors", de: "Fix: Fehlende Error-Handler auf HTTP-Calls in RepertoireList, TournamentDetail — SnackBar-Feedback bei Fehlern" },
  ]},
  { version: "0.22.7", date: "2026-05-29", changes: [
    { en: "Fix: Puzzle loading shows ERROR state with retry button instead of an endless spinner", de: "Fix: Puzzle-Laden zeigt ERROR-State mit Retry-Button statt endlosem Spinner" },
  ]},
  { version: "0.22.6", date: "2026-05-29", changes: [
    { en: "Security: GetCrawlerIp now requires authentication (no more AllowAnonymous)", de: "Security: GetCrawlerIp erfordert jetzt Authentifizierung (kein AllowAnonymous mehr)" },
    { en: "Security: ES+Kibana ports no longer exposed in production compose", de: "Security: ES+Kibana Ports in Production-Compose nicht mehr exponiert" },
    { en: "Cleanup: Removed irrelevant CORS comment about chrome-extension", de: "Cleanup: Irrelevanter CORS-Kommentar zu chrome-extension entfernt" },
  ]},
  { version: "0.22.5", date: "2026-05-29", changes: [
    { en: "Fix: CrawlerExceptionFilter catches TaskCanceledException — timeout yields 504 instead of 500", de: "Fix: CrawlerExceptionFilter faengt TaskCanceledException — Timeout ergibt 504 statt 500" },
    { en: "Fix: CrawlerProxyService — empty response body handled safely, CancellationToken passed through", de: "Fix: CrawlerProxyService — leere Response-Body sicher behandelt, CancellationToken durchgereicht" },
  ]},
  { version: "0.22.4", date: "2026-05-29", changes: [
    { en: "Security: ActiveGameState MaxLength 1MB in the DTO (prevents unbounded writes)", de: "Security: ActiveGameState MaxLength 1MB im DTO (verhindert unbegrenztes Schreiben)" },
    { en: "Security: BookPuzzle import — RequestSizeLimit 50MB + max 10000 puzzles per import", de: "Security: BookPuzzle Import — RequestSizeLimit 50MB + max 10000 Puzzles pro Import" },
  ]},
  { version: "0.22.3", date: "2026-05-29", changes: [
    { en: "Fix: FriendService race condition — single SaveChanges + DbUpdateException handling on re-request after decline", de: "Fix: FriendService Race Condition — Single SaveChanges + DbUpdateException Handling bei Re-Request nach Decline" },
  ]},
  { version: "0.22.2", date: "2026-05-29", changes: [
    { en: "Security: compose.dev.vpn.yml — required-variable checks for critical secrets", de: "Security: compose.dev.vpn.yml — Required-Variable-Checks fuer kritische Secrets" },
  ]},
  { version: "0.22.1", date: "2026-05-29", changes: [
    { en: "Security: Fixed open redirect in login/register — returnUrl is validated against a whitelist", de: "Security: Open Redirect in Login/Register behoben — returnUrl wird gegen Whitelist validiert" },
  ]},
  { version: "0.22.0", date: "2026-05-29", changes: [
    { en: "Endless mode: Give Up shows \"Gave Up\" with flag icon instead of \"Wrong\" (orange instead of red)", de: "Endless Mode: Give Up zeigt \"Gave Up\" mit Flag-Icon statt \"Wrong\" (orange statt rot)" },
    { en: "Endless mode: Show Eval and Give Up available before the first move", de: "Endless Mode: Show Eval und Give Up vor dem ersten Zug verfuegbar" },
    { en: "Infra: Kibana logging dashboard is created automatically (6 panels: Volume, Levels, Apps, Errors, Timeline, log table)", de: "Infra: Kibana Logging-Dashboard wird automatisch erstellt (6 Panels: Volume, Levels, Apps, Errors, Timeline, Log-Tabelle)" },
    { en: "Fix: compose.dev.vpn.yml — corrected kibana-init + healthcheck + service name", de: "Fix: compose.dev.vpn.yml — kibana-init + Healthcheck + Service-Name korrigiert" },
    { en: "Fix: Added CRAWLER_API_KEY in .env.dev", de: "Fix: CRAWLER_API_KEY in .env.dev ergaenzt" },
  ]},
  { version: "0.21.2", date: "2026-05-29", changes: [
    { en: "Infra: Kibana data views are created automatically at startup (rookhub-logs, crawler-logs, alle-logs)", de: "Infra: Kibana Data Views werden beim Start automatisch erstellt (rookhub-logs, crawler-logs, alle-logs)" },
    { en: "Infra: kibana-init one-shot container in both Docker Compose files", de: "Infra: kibana-init One-Shot-Container in beiden Docker-Compose-Files" },
  ]},
  { version: "0.21.1", date: "2026-05-29", changes: [
    { en: "Fix: Cleaned up migration DropRequestLogs — removed duplicated schema operations that prevented API startup", de: "Fix: Migration DropRequestLogs bereinigt — duplizierte Schema-Operationen entfernt die API-Start verhinderten" },
  ]},
  { version: "0.21.0", date: "2026-05-29", changes: [
    { en: "Logging: Elasticsearch + Kibana for centralized log management (replaces DB logging)", de: "Logging: Elasticsearch + Kibana fuer zentrales Log-Management (ersetzt DB-Logging)" },
    { en: "Logging: Serilog with Elasticsearch sink for structured logging in both projects", de: "Logging: Serilog mit Elasticsearch-Sink fuer strukturiertes Logging in beiden Projekten" },
    { en: "Cleanup: Removed RequestLog DB tables, middleware and controller (RookHub + Crawler)", de: "Cleanup: RequestLog DB-Tabellen, Middleware und Controller entfernt (RookHub + Crawler)" },
    { en: "Cleanup: Removed LogRetentionService and CrawlRequestLog in the Crawler", de: "Cleanup: LogRetentionService und CrawlRequestLog im Crawler entfernt" },
    { en: "Infra: ES 8.17 + Kibana 8.17 in all Docker Compose files", de: "Infra: ES 8.17 + Kibana 8.17 in allen Docker-Compose-Files" },
  ]},
  { version: "0.20.1", date: "2026-05-28", changes: [
    { en: "Fix: Stockfish timeout at high search depth no longer shows \"Incorrect\" immediately — user can keep playing", de: "Fix: Stockfish-Timeout bei hoher Suchtiefe zeigt nicht mehr sofort \"Incorrect\" — User kann weiterspielen" },
    { en: "E2E test: New test case for the Stockfish timeout scenario with a complex position", de: "E2E Test: Neuer Testfall fuer Stockfish-Timeout-Szenario mit komplexer Stellung" },
    { en: "E2E: squareCenter/makeMove/dragMove now support black orientation", de: "E2E: squareCenter/makeMove/dragMove unterstuetzen jetzt Black-Orientation" },
  ]},
  { version: "0.20.0", date: "2026-05-28", changes: [
    { en: "Feature: Server-side endless puzzle progress sync — config, highscore and active game are stored server-side", de: "Feature: Server-Side Endless Puzzle Progress Sync — Config, Highscore und Active Game werden serverseitig gespeichert" },
    { en: "Feature: Endless session history is persisted on the server (max 50 sessions)", de: "Feature: Endless Session History wird auf dem Server persistiert (max 50 Sessions)" },
    { en: "Feature: Anonymous and logged-in users can seamlessly continue playing on other devices", de: "Feature: Anonyme und eingeloggte User koennen nahtlos auf anderen Geraeten weiterspielen" },
    { en: "Feature: Claim-session transfers anonymous endless data to the account on login", de: "Feature: Claim-Session uebertraegt anonyme Endless-Daten bei Login auf den Account" },
    { en: "Feature: Bulk import for localStorage migration (one-time on first server sync)", de: "Feature: Bulk-Import fuer localStorage-Migration (einmalig beim ersten Server-Sync)" },
    { en: "API: New endpoints GET/PUT /api/endless/progress, POST /api/endless/sessions, POST /api/endless/claim-session", de: "API: Neue Endpoints GET/PUT /api/endless/progress, POST /api/endless/sessions, POST /api/endless/claim-session" },
    { en: "API: Anonymous variants with rate limiting (anonymous-puzzle policy)", de: "API: Anonyme Varianten mit Rate-Limiting (anonymous-puzzle Policy)" },
    { en: "Tests: 19 new tests for EndlessProgressService (progress, sessions, claim, bulk import)", de: "Tests: 19 neue Tests fuer EndlessProgressService (Progress, Sessions, Claim, BulkImport)" },
  ]},
  { version: "0.19.4", date: "2026-05-28", changes: [
    { en: "Refactor: UnitTest1.cs split into AuthServiceTests, ProfileServiceTests, FriendServiceTests, RepertoireServiceTests", de: "Refactor: UnitTest1.cs aufgeteilt in AuthServiceTests, ProfileServiceTests, FriendServiceTests, RepertoireServiceTests" },
    { en: "Refactor: TeamPlayersDialogComponent extracted into its own file", de: "Refactor: TeamPlayersDialogComponent in eigene Datei extrahiert" },
    { en: "Refactor: Endless puzzle localStorage logic moved out into EndlessStorageService", de: "Refactor: Endless-Puzzle localStorage-Logik in EndlessStorageService ausgelagert" },
    { en: "Feature: Retry interceptor for 502/503/0 errors (1x retry after 1s)", de: "Feature: Retry-Interceptor fuer 502/503/0 Fehler (1x Retry nach 1s)" },
    { en: "Feature: environment.prod.ts + Angular fileReplacements (no more sed in the Dockerfile)", de: "Feature: environment.prod.ts + Angular fileReplacements (kein sed im Dockerfile mehr)" },
    { en: "Crawler: RoundDetectionService with IMemoryCache (60s per tournament)", de: "Crawler: RoundDetectionService mit IMemoryCache (60s pro Tournament)" },
    { en: "Crawler: CancellationToken passed through all crawl methods", de: "Crawler: CancellationToken durch alle Crawl-Methoden durchgereicht" },
    { en: "CI: New test workflow (.github/workflows/test.yml) for dotnet test + ng build", de: "CI: Neuer Test-Workflow (.github/workflows/test.yml) fuer dotnet test + ng build" },
  ]},
  { version: "0.19.3", date: "2026-05-28", changes: [
    { en: "Code review: Crawler GetAllTournaments with pagination (page/pageSize) instead of an unbounded list", de: "Code-Review: Crawler GetAllTournaments mit Pagination (page/pageSize) statt unbegrenzter Liste" },
    { en: "Code review: LogRetentionService for RookHub API and Crawler — deletes request logs older than 30 days", de: "Code-Review: LogRetentionService fuer RookHub API und Crawler — loescht Request-Logs aelter als 30 Tage" },
    { en: "Code review: Frontend tournament list uses the paginated API response", de: "Code-Review: Frontend Tournament-Liste nutzt paginierte API-Response" },
  ]},
  { version: "0.19.2", date: "2026-05-28", changes: [
    { en: "Fix: E2E test puzzle-moves.spec.ts expected German text instead of English UI text", de: "Fix: E2E Test puzzle-moves.spec.ts erwartete deutschen Text statt englischem UI-Text" },
  ]},
  { version: "0.19.1", date: "2026-05-28", changes: [
    { en: "Crawler: Outgoing request logging — all HTTP calls to chess-results.com are persisted in the DB (CrawlRequestLog)", de: "Crawler: Outgoing Request Logging — alle HTTP-Calls zu chess-results.com werden in DB persistiert (CrawlRequestLog)" },
    { en: "Crawler: New endpoint GET /api/crawl-request-logs with filter (URL, status, time range, success) and pagination", de: "Crawler: Neuer Endpoint GET /api/crawl-request-logs mit Filter (URL, Status, Zeitraum, Success) und Pagination" },
    { en: "Crawler: Response body optionally retrievable (includeBody parameter)", de: "Crawler: Response-Body optional abrufbar (includeBody Parameter)" },
  ]},
  { version: "0.19.0", date: "2026-05-28", changes: [
    { en: "Anonymous puzzle stats: Puzzle attempts are tracked even without login (localStorage SessionId)", de: "Anonyme Puzzle-Stats: Puzzle-Attempts werden auch ohne Login getrackt (localStorage SessionId)" },
    { en: "Anonymous stats: Accuracy, streak and best streak for users who are not logged in", de: "Anonyme Stats: Accuracy, Streak und Best Streak fuer nicht eingeloggte User" },
    { en: "Session claim: On login/register, anonymous puzzle data is automatically transferred to the account", de: "Session-Claim: Bei Login/Register werden anonyme Puzzle-Daten automatisch auf den Account uebertragen" },
    { en: "Rate limiting: Dedicated policy for anonymous puzzle endpoints (30 requests/minute)", de: "Rate-Limiting: Eigene Policy fuer anonyme Puzzle-Endpoints (30 Requests/Minute)" },
    { en: "API: New endpoints POST /api/puzzles/{id}/attempt/anonymous, GET /api/puzzles/stats/anonymous, POST /api/puzzles/claim-session", de: "API: Neue Endpoints POST /api/puzzles/{id}/attempt/anonymous, GET /api/puzzles/stats/anonymous, POST /api/puzzles/claim-session" },
  ]},
  { version: "0.18.5", date: "2026-05-28", changes: [
    { en: "Code review: FriendService search with deterministic sorting (OrderBy)", de: "Code-Review: FriendService Suche mit determinisitscher Sortierung (OrderBy)" },
    { en: "Code review: AdminController ClearPuzzles wrapped in a transaction", de: "Code-Review: AdminController ClearPuzzles in Transaktion gewrappt" },
    { en: "Code review: MaxFileSize constant deduplicated (RepertoireService as single source)", de: "Code-Review: MaxFileSize Konstante dedupliziert (RepertoireService als Single Source)" },
    { en: "Code review: BackgroundTaskQueue DropOldest instead of Wait (prevents request blocking)", de: "Code-Review: BackgroundTaskQueue DropOldest statt Wait (verhindert Request-Blockierung)" },
    { en: "Code review: Health endpoint checks DB connectivity (503 on error)", de: "Code-Review: Health-Endpoint prueft DB-Konnektivitaet (503 bei Fehler)" },
    { en: "Code review: PuzzleService min/max ID range with MemoryCache (5min TTL)", de: "Code-Review: PuzzleService Min/Max ID-Range mit MemoryCache (5min TTL)" },
    { en: "Code review: DB index on TournamentSubscription.CrawlerTournamentId", de: "Code-Review: DB-Index auf TournamentSubscription.CrawlerTournamentId" },
    { en: "Code review: Dashboard takeUntilDestroyed for subscription cleanup", de: "Code-Review: Dashboard takeUntilDestroyed fuer Subscription-Cleanup" },
    { en: "Crawler: ApiKey comparison timing-safe (CryptographicOperations.FixedTimeEquals)", de: "Crawler: ApiKey-Vergleich timing-safe (CryptographicOperations.FixedTimeEquals)" },
    { en: "Crawler: CrawlJob ErrorMessage null check", de: "Crawler: CrawlJob ErrorMessage Null-Check" },
  ]},
  { version: "0.18.4", date: "2026-05-28", changes: [
    { en: "Fix: Endless mode last puzzle now appears in the summary (reset on the last life)", de: "Fix: Endless Mode letztes Puzzle erscheint jetzt in der Zusammenfassung (Reset auf letztem Leben)" },
  ]},
  { version: "0.18.3", date: "2026-05-28", changes: [
    { en: "Puzzle mode: Uniform controls in all states (no UI hint whether you are on the solution path)", de: "Puzzle Mode: Einheitliche Controls in allen Zustaenden (kein UI-Hinweis ob am Loesungsweg)" },
    { en: "Puzzle mode: Auto-advance after 3s on a solved puzzle, Show Solution button", de: "Puzzle Mode: Auto-Advance nach 3s bei geloestem Puzzle, Show Solution Button" },
    { en: "Puzzle mode: Review button for the last solved puzzle", de: "Puzzle Mode: Review-Button fuer letztes geloestes Puzzle" },
  ]},
  { version: "0.18.2", date: "2026-05-27", changes: [
    { en: "Endless mode: On a wrong puzzle no auto-advance, instead Show Solution + Continue buttons", de: "Endless Mode: Bei falschem Puzzle kein Auto-Advance, stattdessen Show Solution + Continue Buttons" },
  ]},
  { version: "0.18.1", date: "2026-05-27", changes: [
    { en: "Endless mode: Stockfish depth adjustable via slider during a running streak", de: "Endless Mode: Stockfish Depth waehrend laufender Serie per Slider anpassbar" },
  ]},
  { version: "0.18.0", date: "2026-05-27", changes: [
    { en: "Security: CrawlerProxyService passes through error details (CrawlerRequestException + ExceptionFilter)", de: "Security: CrawlerProxyService Error-Details durchreichen (CrawlerRequestException + ExceptionFilter)" },
    { en: "Security: JWT key minimum 32 bytes for HMAC-SHA256", de: "Security: JWT Key Minimum 32 Bytes fuer HMAC-SHA256" },
    { en: "Security: HSTS header in nginx", de: "Security: HSTS Header in nginx" },
    { en: "Security: Puzzle theme SQL wildcard sanitization", de: "Security: Puzzle-Theme SQL-Wildcard Sanitization" },
    { en: "Security: Password validation in the register form (8+ characters, upper/lower case, digit)", de: "Security: Passwort-Validierung im Register-Formular (8+ Zeichen, Gross/Klein/Zahl)" },
    { en: "Performance: PuzzleService GetStats via DB-level aggregation instead of ToListAsync", de: "Performance: PuzzleService GetStats via DB-Level Aggregation statt ToListAsync" },
    { en: "Performance: Puzzle CSV import with file size limit (500 MB) and CancellationToken", de: "Performance: Puzzle CSV Import mit File-Size-Limit (500 MB) und CancellationToken" },
    { en: "Fix: AdminSeeder no re-hash on an unchanged password", de: "Fix: AdminSeeder kein Re-Hash bei unveraendertem Passwort" },
    { en: "Fix: AutoSubscriptionService race condition on SaveChanges (DbUpdateException handling)", de: "Fix: AutoSubscriptionService Race-Condition bei SaveChanges (DbUpdateException Handling)" },
    { en: "Fix: TournamentMonitor per-user scoping (UserId + FK + migration)", de: "Fix: TournamentMonitor per-User Scoping (UserId + FK + Migration)" },
    { en: "Fix: TournamentProxyController try/catch replaced with CrawlerExceptionFilter", de: "Fix: TournamentProxyController try/catch durch CrawlerExceptionFilter ersetzt" },
    { en: "Fix: AuthService localStorage error handling fully in try/catch", de: "Fix: AuthService localStorage Error Handling komplett in try/catch" },
    { en: "Fix: Dashboard typed HTTP calls (Repertoire[], Friend[], PuzzleStatsDto)", de: "Fix: Dashboard typisierte HTTP-Calls (Repertoire[], Friend[], PuzzleStatsDto)" },
    { en: "Infra: compose.vpn.yml API depends_on Gluetun", de: "Infra: compose.vpn.yml API depends_on Gluetun" },
    { en: "Crawler: BackgroundTaskQueue instead of fire-and-forget Task.Run, 429 on queue-full", de: "Crawler: BackgroundTaskQueue statt fire-and-forget Task.Run, 429 bei Queue-Full" },
    { en: "Crawler: RequestLoggingMiddleware without MemoryStream, background queue instead of Task.Run", de: "Crawler: RequestLoggingMiddleware ohne MemoryStream, Background-Queue statt Task.Run" },
    { en: "Crawler: VPN rotation thread safety (semaphore held during rotation)", de: "Crawler: VPN-Rotation Thread-Safety (Semaphore waehrend Rotation gehalten)" },
    { en: "Crawler: Separate HttpClient for the Gluetun API (5s timeout)", de: "Crawler: Separater HttpClient fuer Gluetun API (5s Timeout)" },
    { en: "Crawler: ResolveTournamentAsync also for non-numeric IDs", de: "Crawler: ResolveTournamentAsync auch fuer nicht-numerische IDs" },
    { en: "Tests: 16 new tests (CrawlerProxyService, BackgroundTaskQueue, AdminSeeder, PuzzleService, TournamentMonitor, AutoSubscription)", de: "Tests: 16 neue Tests (CrawlerProxyService, BackgroundTaskQueue, AdminSeeder, PuzzleService, TournamentMonitor, AutoSubscription)" },
  ]},
  { version: "0.17.0", date: "2026-05-27", changes: [
    { en: "Book puzzles: Import of puzzle books from schach-bot (12 books, 5000+ puzzles)", de: "Book-Puzzles: Import von Puzzle-Buechern aus schach-bot (12 Buecher, 5000+ Puzzles)" },
    { en: "Book puzzles: Dedicated route /puzzles/book/:id with board, move solution and book metadata", de: "Book-Puzzles: Eigene Route /puzzles/book/:id mit Board, Zugloesung und Buch-Metadaten" },
    { en: "Book puzzles: Lookup API GET /api/book-puzzles/by-line-id for schach-bot integration", de: "Book-Puzzles: Lookup-API GET /api/book-puzzles/by-line-id fuer schach-bot Integration" },
    { en: "Book puzzles: Admin import endpoint POST /api/admin/book-puzzles/import", de: "Book-Puzzles: Admin-Import-Endpoint POST /api/admin/book-puzzles/import" },
    { en: "Book puzzles: Book list with puzzle counts GET /api/book-puzzles/books", de: "Book-Puzzles: Buch-Liste mit Puzzle-Counts GET /api/book-puzzles/books" },
    { en: "Book puzzles: Python import script scripts/import_books.py", de: "Book-Puzzles: Python-Import-Script scripts/import_books.py" },
  ]},
  { version: "0.16.2", date: "2026-05-27", changes: [
    { en: "Crawler: Migration upgrade docs for existing DBs (EnsureCreated → Migrate)", de: "Crawler: Migration-Upgrade-Doku fuer bestehende DBs (EnsureCreated → Migrate)" },
    { en: "Deploy: Crawler fix for empty __EFMigrationsHistory on existing databases", de: "Deploy: Crawler-Fix fuer leere __EFMigrationsHistory bei Bestandsdatenbanken" },
  ]},
  { version: "0.16.1", date: "2026-05-27", changes: [
    { en: "Security: Tournament ID validation against SSRF in proxy and monitor controllers", de: "Security: Tournament-ID-Validierung gegen SSRF in Proxy- und Monitor-Controllern" },
    { en: "Security: .gitignore for test artifacts (auth-state, playwright-report)", de: "Security: .gitignore fuer Test-Artifacts (auth-state, playwright-report)" },
    { en: "Refactor: Fire-and-forget Task.Run replaced with Channel+BackgroundService", de: "Refactor: Fire-and-forget Task.Run ersetzt durch Channel+BackgroundService" },
    { en: "Refactor: TypeScript interfaces instead of any for all API responses", de: "Refactor: TypeScript-Interfaces statt any fuer alle API-Responses" },
    { en: "Crawler: EnsureCreated replaced with EF Core Migrations", de: "Crawler: EnsureCreated durch EF Core Migrations ersetzt" },
  ]},
  { version: "0.16.0", date: "2026-05-27", changes: [
    { en: "Endless mode: Puzzle review on the game-over screen shows all played puzzles", de: "Endless Mode: Puzzle-Review auf Game-Over-Screen zeigt alle gespielten Puzzles" },
    { en: "Endless mode: Click on a puzzle in the review navigates to /puzzles/:id", de: "Endless Mode: Klick auf Puzzle im Review navigiert zu /puzzles/:id" },
    { en: "Endless mode: Failed puzzles highlighted in red", de: "Endless Mode: Fehlgeschlagene Puzzles rot hervorgehoben" },
  ]},
  { version: "0.15.9", date: "2026-05-27", changes: [
    { en: "E2E test stack: Isolated Docker stack (compose.e2e.yml) with its own DB, API, frontend", de: "E2E Teststack: Isolierter Docker-Stack (compose.e2e.yml) mit eigenem DB, API, Frontend" },
    { en: "E2E test stack: scripts/e2e.sh starts the stack, seeds puzzles, runs the tests, cleans up", de: "E2E Teststack: scripts/e2e.sh startet Stack, seedet Puzzles, fuehrt Tests aus, raeumt auf" },
    { en: "E2E: API_URL configurable in global-setup.ts and auth.fixture.ts", de: "E2E: API_URL konfigurierbar in global-setup.ts und auth.fixture.ts" },
  ]},
  { version: "0.15.8", date: "2026-05-27", changes: [
    { en: "Fix: User side stays fixed — movable.color always on orientation instead of turnColor", de: "Fix: User-Seite bleibt fest — movable.color immer auf orientation statt turnColor" },
    { en: "Fix: Dests only when the user is to move (prevents Stockfish pieces from moving after premove+wrong move)", de: "Fix: Dests nur wenn User am Zug (verhindert Stockfish-Figuren ziehen nach Premove+Wrong Move)" },
  ]},
  { version: "0.15.7", date: "2026-05-27", changes: [
    { en: "Share puzzle: QR code + link to share puzzles (normal + endless mode)", de: "Share-Puzzle: QR-Code + Link zum Teilen von Puzzles (Normal + Endless Mode)" },
    { en: "Direct link /puzzles/:id loads a specific puzzle", de: "Direkt-Link /puzzles/:id laedt ein bestimmtes Puzzle" },
  ]},
  { version: "0.15.6", date: "2026-05-27", changes: [
    { en: "Fix: Premove orientation bug — illegal premoves are ignored instead of triggering a side switch", de: "Fix: Premove-Orientierungsbug — illegale Premoves werden ignoriert statt Seitenwechsel auszulösen" },
  ]},
  { version: "0.15.5", date: "2026-05-27", changes: [
    { en: "E2E tests: Wrong move + premove in puzzle mode and endless mode", de: "E2E Tests: Falscher Zug + Premove in Puzzle Mode und Endless Mode" },
  ]},
  { version: "0.15.4", date: "2026-05-27", changes: [
    { en: "Puzzle mode: Show Eval and Reset buttons after a wrong move", de: "Puzzle Mode: Show Eval und Reset Buttons nach falschem Zug" },
    { en: "Fix: Endless mode no immediate Wrong on a Stockfish error", de: "Fix: Endless Mode kein sofortiges Wrong bei Stockfish-Fehler" },
  ]},
  { version: "0.15.3", date: "2026-05-27", changes: [
    { en: "E2E tests with Playwright (auth, dashboard, puzzles)", de: "E2E Tests mit Playwright (Auth, Dashboard, Puzzles)" },
  ]},
  { version: "0.15.2", date: "2026-05-27", changes: [
    { en: "Tests: 110 new tests (122 → 232), all controllers fully tested", de: "Tests: 110 neue Tests (122 → 232), alle Controller vollständig getestet" },
    { en: "Tests: TournamentProxyController, PuzzleController, AuthController, ProfileController, FriendController, RepertoireController, ExtensionController, RequestLogController, RoundMonitorService", de: "Tests: TournamentProxyController, PuzzleController, AuthController, ProfileController, FriendController, RepertoireController, ExtensionController, RequestLogController, RoundMonitorService" },
  ]},
  { version: "0.15.1", date: "2026-05-27", changes: [
    { en: "Fix: Premoves in normal puzzle mode now work", de: "Fix: Premoves im normalen Puzzle Mode funktionieren jetzt" },
  ]},
  { version: "0.15.0", date: "2026-05-27", changes: [
    { en: "Puzzle mode: Stockfish takes over on a wrong move (like in endless mode)", de: "Puzzle Mode: Stockfish übernimmt bei falschem Zug (wie im Endless Mode)" },
    { en: "Puzzle mode: Checkmate against Stockfish counts as an alternative solution", de: "Puzzle Mode: Schachmatt gegen Stockfish zählt als alternative Lösung" },
    { en: "Puzzle mode: Mouseslip button (once per puzzle)", de: "Puzzle Mode: Mouseslip-Button (einmal pro Puzzle)" },
    { en: "Puzzle mode: Stockfish depth setting (1–24, persisted)", de: "Puzzle Mode: Stockfish Depth Einstellung (1–24, persistiert)" },
    { en: "Puzzle mode: Premoves while Stockfish is thinking", de: "Puzzle Mode: Premoves während Stockfish denkt" },
  ]},
  { version: "0.14.5", date: "2026-05-27", changes: [
    { en: "Fix: Premove now works (pieces clickable during the opponent's move)", de: "Fix: Premove funktioniert jetzt (Figuren während Gegnerzug anklickbar)" },
  ]},
  { version: "0.14.4", date: "2026-05-27", changes: [
    { en: "Fix: Premove system fully reworked (move is executed automatically after the opponent's move)", de: "Fix: Premove-System komplett überarbeitet (Zug wird nach Gegnerzug automatisch ausgeführt)" },
    { en: "Fix: Help overlay with correct umlauts", de: "Fix: Hilfe-Overlay mit korrekten Umlauten" },
    { en: "Fix: Corrected config grid spacing between fields", de: "Fix: Config-Grid Abstand zwischen Feldern korrigiert" },
    { en: "Help: Documented Stockfish depth setting", de: "Hilfe: Stockfish Depth Einstellung dokumentiert" },
  ]},
  { version: "0.14.3", date: "2026-05-27", changes: [
    { en: "Endless mode: Stockfish depth configurable (1–24, default 16)", de: "Endless Mode: Stockfish-Tiefe konfigurierbar (1–24, Standard 16)" },
  ]},
  { version: "0.14.2", date: "2026-05-27", changes: [
    { en: "Endless mode: Premoves while the opponent is thinking (plan your move ahead like on Lichess/Chess.com)", de: "Endless Mode: Premoves waehrend Gegner denkt (Zug vorausplanen wie auf Lichess/Chess.com)" },
  ]},
  { version: "0.14.1", date: "2026-05-27", changes: [
    { en: "Endless mode: Help overlay explains gameplay, Stockfish, buttons and settings", de: "Endless Mode: Hilfe-Overlay erklaert Spielablauf, Stockfish, Buttons und Einstellungen" },
  ]},
  { version: "0.14.0", date: "2026-05-27", changes: [
    { en: "Endless mode: Mouseslip button undoes the last move for free (once per puzzle)", de: "Endless Mode: Mouseslip-Button macht letzten Zug gratis rueckgaengig (einmal pro Puzzle)" },
  ]},
  { version: "0.13.9", date: "2026-05-27", changes: [
    { en: "Endless mode: Fasttrack enabled by default", de: "Endless Mode: Fasttrack standardmaessig aktiviert" },
  ]},
  { version: "0.13.8", date: "2026-05-27", changes: [
    { en: "Fix: Added missing EF migration for the Puzzles table (prod deployment)", de: "Fix: Fehlende EF-Migration fuer Puzzles-Tabelle hinzugefuegt (Prod-Deployment)" },
  ]},
  { version: "0.13.7", date: "2026-05-27", changes: [
    { en: "Increased Stockfish search depth from 12 to 16 (~2400 Elo)", de: "Stockfish Suchtiefe von 12 auf 16 erhoeht (~2400 Elo)" },
  ]},
  { version: "0.13.6", date: "2026-05-27", changes: [
    { en: "Endless mode: Puzzle reset costs a life", de: "Endless Mode: Puzzle Reset kostet ein Leben" },
  ]},
  { version: "0.13.5", date: "2026-05-27", changes: [
    { en: "Endless mode: Alternative solution pauses, Continue/Show Solution buttons", de: "Endless Mode: Alternative Loesung pausiert, Continue/Show Solution Buttons" },
    { en: "Endless mode: Show Solution plays back the intended move sequence animated", de: "Endless Mode: Show Solution spielt die beabsichtigte Zugfolge animiert ab" },
  ]},
  { version: "0.13.4", date: "2026-05-27", changes: [
    { en: "Fasttrack: Auto thresholds at least startElo+400/+800 (no more pointless low values)", de: "Fasttrack: Auto-Thresholds mindestens startElo+400/+800 (keine sinnlosen niedrigen Werte mehr)" },
    { en: "Fasttrack: Step size clamped to 10–200", de: "Fasttrack: Step Size auf 10–200 geclampt" },
  ]},
  { version: "0.13.3", date: "2026-05-27", changes: [
    { en: "Endless mode: rating range loaded from DB, all inputs validated against actual puzzle range", de: "Endless Mode: Rating-Range aus DB geladen, alle Inputs validiert gegen tatsaechliche Puzzle-Range" },
    { en: "Endless mode: empty rating ranges are automatically skipped", de: "Endless Mode: Leere Rating-Bereiche werden automatisch uebersprungen" },
    { en: "Endless mode: \"Played out\" screen with Stockfish question when max rating exceeded", de: "Endless Mode: \"Ausgespielt\" Screen mit Stockfisch-Frage wenn Max-Rating ueberschritten" },
    { en: "Endless mode: step-size migration from old default 20 to 40", de: "Endless Mode: Step-Size Migration von altem Default 20 auf 40" },
    { en: "Endless mode: stale fasttrack thresholds are cleaned up on load", de: "Endless Mode: Stale Fasttrack-Thresholds werden beim Laden bereinigt" },
    { en: "API: new endpoint GET /api/puzzles/rating-range", de: "API: Neuer Endpoint GET /api/puzzles/rating-range" },
  ]},
  { version: "0.13.2", date: "2026-05-27", changes: [
    { en: "Fasttrack: always available, fallback startElo+400/+800 when no history", de: "Fasttrack: Immer verfuegbar, Fallback startElo+400/+800 wenn keine History" },
    { en: "Fasttrack: thresholds editable with auto-value display and click reset", de: "Fasttrack: Thresholds editierbar mit Auto-Wert-Anzeige und Klick-Reset" },
    { en: "Fasttrack: manual overrides are persisted", de: "Fasttrack: Manuelle Overrides werden persistiert" },
    { en: "Endless mode: range width removed, derived from step size", de: "Endless Mode: Range Width entfernt, ergibt sich aus Step Size" },
    { en: "Endless mode: puzzle tags hidden by default (Show/Hide toggle)", de: "Endless Mode: Puzzle-Tags standardmaessig ausgeblendet (Show/Hide toggle)" },
  ]},
  { version: "0.13.0", date: "2026-05-27", changes: [
    { en: "Endless mode: session history tracks all play-throughs (max 50, localStorage)", de: "Endless Mode: Session-History trackt alle Spieldurchlaeufe (max 50, localStorage)" },
    { en: "Endless mode: fasttrack option skips easy puzzles based on past sessions", de: "Endless Mode: Fasttrack-Option ueberspringt leichte Puzzles basierend auf vergangenen Sessions" },
    { en: "Endless mode: 3-phase fasttrack algorithm (phase 1: up to 1st error avg, phase 2: up to 2nd error avg, phase 3: step 20)", de: "Endless Mode: 3-Phasen Fasttrack-Algorithmus (Phase 1: bis 1. Fehler-Avg, Phase 2: bis 2. Fehler-Avg, Phase 3: Step 20)" },
    { en: "Endless mode: dynamic rating system instead of fixed level calculation", de: "Endless Mode: Dynamisches Rating-System statt fester Level-Berechnung" },
    { en: "Endless mode: error ratings shown in the game-over screen", de: "Endless Mode: Fehler-Ratings im Game-Over-Screen angezeigt" },
    { en: "Endless mode: phase indicator during fasttrack play", de: "Endless Mode: Phase-Indikator waehrend Fasttrack-Spiel" },
  ]},
  { version: "0.12.4", date: "2026-05-27", changes: [
    { en: "Endless mode: uniform UI after the first move (no visible difference between correct/wrong)", de: "Endless Mode: Einheitliches UI nach erstem Zug (kein Unterschied zwischen richtig/falsch sichtbar)" },
    { en: "Endless mode: correct only on a fully solved puzzle, otherwise always Reset/Give Up/Eval buttons", de: "Endless Mode: Correct nur bei komplett geloestem Puzzle, sonst immer Reset/Give Up/Eval Buttons" },
    { en: "Endless mode: eval comparison start vs. current on click on Show Eval", de: "Endless Mode: Eval-Vergleich Start vs. Aktuell beim Klick auf Show Eval" },
  ]},
  { version: "0.12.3", date: "2026-05-27", changes: [
    { en: "Endless mode: Stockfish keeps playing endlessly after a wrong move (no limit)", de: "Endless Mode: Stockfish spielt nach falschem Zug endlos weiter (kein Limit)" },
    { en: "Endless mode: mate against Stockfish counts as an alternative solution", de: "Endless Mode: Matt gegen Stockfish zaehlt als alternative Loesung" },
    { en: "Endless mode: Show Eval, Reset (no loss of life), Give Up buttons", de: "Endless Mode: Show Eval, Reset (kein Lebensverlust), Give Up Buttons" },
    { en: "Endless mode: eval display from White's perspective", de: "Endless Mode: Eval-Anzeige aus Weiss-Perspektive" },
    { en: "Fix: CSP + WASM MIME type for Stockfish web worker", de: "Fix: CSP + WASM MIME-Type fuer Stockfish Web Worker" },
  ]},
  { version: "0.12.2", date: "2026-05-27", changes: [
    { en: "Endless mode: Stockfish refutation on a wrong move", de: "Endless Mode: Stockfish-Refutation bei falschem Zug" },
    { en: "Stockfish 18 Lite (WASM) integrated as a web worker", de: "Stockfish 18 Lite (WASM) als Web Worker integriert" },
  ]},
  { version: "0.12.1", date: "2026-05-27", changes: [
    { en: "Endless mode: default step size and range increased to 40", de: "Endless Mode: Standard Step-Size und Range auf 40 erhoeht" },
  ]},
  { version: "0.12.0", date: "2026-05-27", changes: [
    { en: "Endless puzzle mode: progressive difficulty with configurable start rating, step size and range", de: "Endless Puzzle Mode: Progressive Schwierigkeit mit konfigurierbarem Start-Rating, Step-Size und Range" },
    { en: "Endless mode: 3 lives, at 0 game over with high-score tracking", de: "Endless Mode: 3 Leben, bei 0 Game Over mit Highscore-Tracking" },
    { en: "Endless mode: prefetch for fast loading, session timer, level display", de: "Endless Mode: Prefetch fuer schnelles Laden, Session-Timer, Level-Anzeige" },
    { en: "Endless mode: config is saved in localStorage, high score persistent", de: "Endless Mode: Config wird in localStorage gespeichert, Highscore persistent" },
  ]},
  { version: "0.11.3", date: "2026-05-26", changes: [
    { en: "Performance: puzzle loading optimized from 10+ seconds to under 1 second", de: "Performance: Puzzle-Loading von 10+ Sekunden auf unter 1 Sekunde optimiert" },
    { en: "Backend: ID-range approach instead of COUNT+SKIP on 5.3M rows", de: "Backend: ID-Range-Ansatz statt COUNT+SKIP auf 5.3M Rows" },
    { en: "Frontend: next puzzle is preloaded in the background", de: "Frontend: Naechstes Puzzle wird im Hintergrund vorgeladen" },
  ]},
  { version: "0.11.2", date: "2026-05-26", changes: [
    { en: "Fix: puzzle board renders correctly (critical CSS + inlineCritical disabled)", de: "Fix: Puzzle-Board rendert korrekt (Critical CSS + inlineCritical deaktiviert)" },
    { en: "Fix: puzzle moves work (Chessground viewOnly bug worked around)", de: "Fix: Puzzle-Zuege funktionieren (Chessground viewOnly-Bug umgangen)" },
    { en: "Fix: state order in puzzle setup corrected (dests before board update)", de: "Fix: State-Reihenfolge in Puzzle-Setup korrigiert (dests vor Board-Update)" },
  ]},
  { version: "0.11.1", date: "2026-05-26", changes: [
    { en: "Puzzles playable without login (route + API open)", de: "Puzzles ohne Anmeldung spielbar (Route + API offen)" },
    { en: "Stats and attempts only for logged-in users", de: "Stats und Attempts nur fuer eingeloggte User" },
    { en: "Puzzles link in navbar also visible for non-logged-in users", de: "Puzzles-Link in Navbar auch fuer nicht eingeloggte User sichtbar" },
  ]},
  { version: "0.11.0", date: "2026-05-26", changes: [
    { en: "Puzzle feature: solve Lichess puzzles interactively with a Chessground board", de: "Puzzle-Feature: Lichess-Puzzles interaktiv loesen mit Chessground-Board" },
    { en: "Puzzle filter: rating range and skip-solved", de: "Puzzle-Filter: Rating-Range und Skip-Solved" },
    { en: "Puzzle statistics: accuracy, streak, history", de: "Puzzle-Statistiken: Accuracy, Streak, History" },
    { en: "Admin: CSV import for Lichess puzzle database", de: "Admin: CSV-Import fuer Lichess-Puzzle-Datenbank" },
    { en: "Dashboard: puzzle stats card", de: "Dashboard: Puzzle-Stats-Card" },
  ]},
  { version: "0.10.0", date: "2026-05-26", changes: [
    { en: "Backend: automatic detail crawling (art=9) for favorited players on a new round", de: "Backend: Automatisches Detail-Crawling (art=9) fuer favorisierte Spieler bei neuer Runde" },
    { en: "Crawler: new endpoint POST /api/crawl/player-details for individual player results", de: "Crawler: Neuer Endpoint POST /api/crawl/player-details fuer Spieler-Einzelergebnisse" },
    { en: "Crawler: new endpoint GET /api/tournaments/{id}/players/{snr}/results", de: "Crawler: Neuer Endpoint GET /api/tournaments/{id}/players/{snr}/results" },
    { en: "PlayerResult model extended: OpponentSnr, OpponentName, OpponentElo, Points", de: "PlayerResult-Model erweitert: OpponentSnr, OpponentName, OpponentElo, Points" },
    { en: "Claude settings unified for both projects", de: "Claude-Settings fuer beide Projekte vereinheitlicht" },
  ]},
  { version: "0.9.9", date: "2026-05-21", changes: [
    { en: "Players and friends are automatically marked as favorites on tournament subscriptions", de: "Spieler und Freunde werden bei Turnier-Abos automatisch als Favoriten markiert" },
    { en: "Matching via FIDE ID and name", de: "Matching ueber FIDE-ID und Name" },
  ]},
  { version: "0.9.8", date: "2026-05-21", changes: [
    { en: "Fix: dashboard tournament links now use the correct ID (no longer the ChessResults ID)", de: "Fix: Dashboard-Turnier-Links nutzen jetzt korrekte ID (nicht mehr ChessResults-ID)" },
    { en: "Crawler accepts both internal DB ID and ChessResults ID in tournament routes", de: "Crawler akzeptiert sowohl interne DB-ID als auch ChessResults-ID in Turnier-Routen" },
  ]},
  { version: "0.9.7", date: "2026-05-21", changes: [
    { en: "Info button next to Login/Register shows a quickstart guide for non-logged-in users", de: "Info-Button neben Login/Register zeigt Quickstart-Guide für nicht eingeloggte User" },
    { en: "Quickstart removed from the user menu", de: "Quickstart aus dem User-Menü entfernt" },
  ]},
  { version: "0.9.6", date: "2026-05-21", changes: [
    { en: "Fix: quickstart icons for monitor and ChessResults ID corrected", de: "Fix: Quickstart-Icons für Monitor und ChessResults-ID korrigiert" },
    { en: "Fix: umlauts in the quickstart guide", de: "Fix: Umlaute im Quickstart-Guide" },
  ]},
  { version: "0.9.5", date: "2026-05-21", changes: [
    { en: "Quickstart guide in the user menu explains Subscribe, Monitor, favorites and ChessResults ID", de: "Quickstart-Guide im User-Menü erklärt Subscribe, Monitor, Favoriten und ChessResults-ID" },
    { en: "Quickstart is shown automatically after registration", de: "Quickstart wird automatisch nach Registrierung angezeigt" },
  ]},
  { version: "0.9.4", date: "2026-05-21", changes: [
    { en: "Dashboard: subscribed tournaments are now clickable and lead to the tournament page", de: "Dashboard: Abonnierte Turniere sind jetzt klickbar und fuehren zur Turnierseite" },
  ]},
  { version: "0.9.3", date: "2026-05-20", changes: [
    { en: "Upcoming tournaments are subscribed automatically when a ChessResults ID is set", de: "Anstehende Turniere werden automatisch abonniert wenn ChessResults-ID hinterlegt" },
    { en: "Daily automatic search for new tournament registrations", de: "Taegliche automatische Suche nach neuen Turnier-Anmeldungen" },
  ]},
  { version: "0.9.2", date: "2026-05-20", changes: [
    { en: "Revert: player search again purely by first/last name (ChessResults ID search removed)", de: "Revert: Spielersuche wieder rein per Vor-/Nachname (ChessResults-ID-Suche entfernt)" },
  ]},
  { version: "0.9.1", date: "2026-05-20", changes: [
    { en: "Fix: player search shows only the exact match on an exact name hit (instead of all with the same first name)", de: "Fix: Spielersuche zeigt bei exaktem Namens-Treffer nur diesen an (statt alle mit gleichem Vornamen)" },
  ]},
  { version: "0.9.0", date: "2026-05-20", changes: [
    { en: "Player search in the profile: enter first/last name and search on ChessResults + FIDE", de: "Spielersuche im Profil: Vorname/Nachname eingeben und auf ChessResults + FIDE suchen" },
    { en: "Search results show name, title, Elo, country and IDs", de: "Suchergebnisse zeigen Name, Titel, Elo, Land und IDs an" },
    { en: "Clicking a result automatically applies the ChessResults ID and/or FIDE ID", de: "Klick auf Ergebnis uebernimmt ChessResults-ID und/oder FIDE-ID automatisch" },
    { en: "With exactly one hit per source, the ID is filled in automatically", de: "Bei genau einem Treffer pro Quelle wird die ID automatisch ausgefuellt" },
    { en: "New profile fields: first name and last name", de: "Neue Profil-Felder: Vorname und Nachname" },
  ]},
  { version: "0.8.7", date: "2026-05-19", changes: [
    { en: "Friend search now also searches Chess.com, Lichess, FIDE ID and ChessResults ID", de: "Freunde-Suche durchsucht jetzt auch Chess.com, Lichess, FIDE-ID und ChessResults-ID" },
    { en: "Search results show existing chess identities", de: "Suchergebnisse zeigen vorhandene Schach-Identitaeten an" },
  ]},
  { version: "0.8.6", date: "2026-05-19", changes: [
    { en: "Fix: chessboard rendering completely reworked (JS-based dimensions instead of CSS tricks)", de: "Fix: Schachbrett-Rendering komplett ueberarbeitet (JS-basierte Dimensionen statt CSS-Tricks)" },
    { en: "Chessground now gets an explicit pixel size via JavaScript", de: "Chessground bekommt jetzt explizite Pixel-Groesse via JavaScript" },
    { en: "ResizeObserver keeps the board square on window changes", de: "ResizeObserver haelt das Brett bei Fensteraenderungen quadratisch" },
  ]},
  { version: "0.8.5", date: "2026-05-19", changes: [
    { en: "Fix: Chessground board now renders correctly (padding-bottom trick instead of aspect-ratio)", de: "Fix: Chessground-Board rendert jetzt korrekt (padding-bottom Trick statt aspect-ratio)" },
    { en: "Fix: board wrapper with two DIVs so Chessground gets concrete dimensions", de: "Fix: Board-Wrapper mit zwei DIVs damit Chessground konkrete Dimensionen bekommt" },
  ]},
  { version: "0.8.4", date: "2026-05-19", changes: [
    { en: "Fix: chessboard now renders correctly next to the move list (explicit 400x400px dimensions for Chessground)", de: "Fix: Schachbrett rendert jetzt korrekt neben der Zugliste (explizite 400x400px Dimensionen fuer Chessground)" },
  ]},
  { version: "0.8.3", date: "2026-05-19", changes: [
    { en: "Fix: chessboard layout in the repertoire detail corrected (fixed width 400px, responsive breakpoint)", de: "Fix: Schachbrett-Layout im Repertoire-Detail korrigiert (feste Breite 400px, responsive Breakpoint)" },
  ]},
  { version: "0.8.2", date: "2026-05-19", changes: [
    { en: "PGN comments are shown in the move list (italic, below the move)", de: "PGN-Kommentare werden in der Zugliste angezeigt (kursiv, unter dem Zug)" },
    { en: "Chessbase annotations ([%csl], [%cal], [%tqu]) are removed from comments", de: "Chessbase-Annotationen ([%csl], [%cal], [%tqu]) werden aus Kommentaren entfernt" },
    { en: "PGN viewer dialog also shows comments", de: "PGN-Viewer Dialog zeigt ebenfalls Kommentare an" },
  ]},
  { version: "0.8.1", date: "2026-05-19", changes: [
    { en: "Fix: PGN parser supports Chessbase annotations (RAV variations, labels, NAGs)", de: "Fix: PGN-Parser unterstuetzt Chessbase-Annotationen (RAV-Varianten, Labels, NAGs)" },
    { en: "Fix: header detection now ignores ] in comments like {[%tqu ...]}", de: "Fix: Header-Erkennung ignoriert jetzt ] in Kommentaren wie {[%tqu ...]}" },
  ]},
  { version: "0.8.0", date: "2026-05-19", changes: [
    { en: "Repertoire: new Lines/Tree/Edit layout with inline chessboard", de: "Repertoire: Neues Lines/Tree/Edit-Layout mit Inline-Schachbrett" },
    { en: "Repertoire: Lines view shows all games with move list and navigation", de: "Repertoire: Lines-Ansicht zeigt alle Partien mit Zugliste und Navigation" },
    { en: "Repertoire: Tree view with move tree, frequencies and breadcrumb navigation", de: "Repertoire: Tree-Ansicht mit Zugbaum, Haeufigkeiten und Breadcrumb-Navigation" },
    { en: "Repertoire: Edit view for file upload and management", de: "Repertoire: Edit-Ansicht fuer Datei-Upload und -Verwaltung" },
    { en: "Repertoire: keyboard navigation (arrow keys) in the Lines view", de: "Repertoire: Keyboard-Navigation (Pfeiltasten) in der Lines-Ansicht" },
    { en: "Refactor: PGN parser extracted as a shared module", de: "Refactor: PGN-Parser als geteiltes Modul extrahiert" },
  ]},
  { version: "0.7.0", date: "2026-05-19", changes: [
    { en: "PGN viewer: interactive chessboard with move navigation (chess.js + chessground)", de: "PGN-Viewer: Interaktives Schachbrett mit Zugnavigation (chess.js + chessground)" },
    { en: "PGN viewer: move list with click navigation and keyboard support (arrow keys, Home/End)", de: "PGN-Viewer: Zugliste mit Klick-Navigation und Keyboard-Support (Pfeiltasten, Home/End)" },
    { en: "PGN viewer: multi-game support for PGN files with multiple games", de: "PGN-Viewer: Multi-Game-Support fuer PGN-Dateien mit mehreren Partien" },
  ]},
  { version: "0.6.8", date: "2026-05-19", changes: [
    { en: "Admin: log section with filters (path, method, status, user)", de: "Admin: Log-Bereich mit Filtern (Pfad, Methode, Status, Benutzer)" },
    { en: "Admin: colored status codes, method badges, IP column", de: "Admin: Farbige Status-Codes, Methoden-Badges, IP-Spalte" },
    { en: "Admin: slow requests (>1s) highlighted", de: "Admin: Langsame Requests (>1s) hervorgehoben" },
  ]},
  { version: "0.6.7", date: "2026-05-19", changes: [
    { en: "Checklist: the current version is reported after every commit", de: "Checkliste: Nach jedem Commit wird die aktuelle Version mitgeteilt" },
  ]},
  { version: "0.6.6", date: "2026-05-19", changes: [
    { en: "Fix: Copy button in the share dialog now also works without HTTPS (fallback)", de: "Fix: Copy-Button im Share-Dialog funktioniert jetzt auch ohne HTTPS (Fallback)" },
  ]},
  { version: "0.6.5", date: "2026-05-19", changes: [
    { en: "Fix: typo in the repository name corrected (chessreslults → chessresults)", de: "Fix: Typo im Repository-Namen korrigiert (chessreslults → chessresults)" },
  ]},
  { version: "0.6.4", date: "2026-05-19", changes: [
    { en: "Security: input validation and length limits on all search fields", de: "Security: Input-Validierung und Laengenbegrenzungen auf allen Suchfeldern" },
    { en: "Security: password policy increased to 8 characters + complexity", de: "Security: Passwort-Policy auf 8 Zeichen + Komplexitaet erhoeht" },
    { en: "Security: crawler body validation in the proxy controller", de: "Security: Crawler-Body-Validierung im Proxy-Controller" },
    { en: "Fix: HealthController returns 503 instead of a wrong 200 on IP error", de: "Fix: HealthController gibt 503 statt falsches 200 bei IP-Fehler" },
    { en: "Fix: empty catch blocks replaced by logging", de: "Fix: Leere Catch-Bloecke durch Logging ersetzt" },
    { en: "Performance: response compression enabled", de: "Performance: Response Compression aktiviert" },
  ]},
  { version: "0.6.3", date: "2026-05-19", changes: [
    { en: "Security: API key authentication for crawler endpoints", de: "Security: API-Key-Authentifizierung fuer Crawler-Endpoints" },
    { en: "Security: TournamentMonitorController now requires authentication", de: "Security: TournamentMonitorController erfordert jetzt Authentifizierung" },
    { en: "Security: .env.dev removed from Git tracking", de: "Security: .env.dev aus Git-Tracking entfernt" },
  ]},
  { version: "0.6.2", date: "2026-05-19", changes: [
    { en: "Dev badge in the footer: shows \"dev\" next to the version in the dev build", de: "Dev-Badge im Footer: zeigt \"dev\" neben der Version im Dev-Build" },
    { en: "CI/CD: :dev and :latest tag scheme for Docker images", de: "CI/CD: :dev und :latest Tag-Schema fuer Docker-Images" },
  ]},
  { version: "0.6.1", date: "2026-05-19", changes: [
    { en: "CI/CD: GitHub Actions build Docker images on push (latest) and tag (versioned)", de: "CI/CD: GitHub Actions bauen Docker-Images bei Push (latest) und Tag (versioniert)" },
    { en: "Prod compose uses IMAGE_TAG for tagged versions", de: "Prod-Compose nutzt IMAGE_TAG fuer getaggte Versionen" },
  ]},
  { version: "0.6.0", date: "2026-05-19", changes: [
    { en: "Admin system: user management with admin role (IsAdmin)", de: "Admin-System: Benutzerverwaltung mit Admin-Rolle (IsAdmin)" },
    { en: "Admin seed on startup via ADMIN_USERNAME/ADMIN_PASSWORD env variables", de: "Admin-Seed beim Start ueber ADMIN_USERNAME/ADMIN_PASSWORD Env-Variablen" },
    { en: "Admin panel with user management and request log view", de: "Admin-Panel mit User-Verwaltung und Request-Log-Ansicht" },
    { en: "RequestLogController now accessible only to admins", de: "RequestLogController jetzt nur fuer Admins zugaenglich" },
  ]},
  { version: "0.5.2", date: "2026-05-19", changes: [
    { en: "Commit checklist anchored in both CLAUDE.md (version, changelog, tests)", de: "Commit-Checkliste in beiden CLAUDE.md verankert (Version, Changelog, Tests)" },
  ]},
  { version: "0.5.1", date: "2026-05-19", changes: [
    { en: "73 new unit tests for both projects (crawler + RookHub)", de: "73 neue Unit-Tests fuer beide Projekte (Crawler + RookHub)" },
    { en: "Test requirement anchored in CLAUDE.md: every feature/endpoint/bugfix needs tests", de: "Test-Pflicht in CLAUDE.md verankert: jedes Feature/Endpoint/Bugfix braucht Tests" },
  ]},
  { version: "0.5.0", date: "2026-05-19", changes: [
    { en: "Public tournament page /t/:id (viewable without login)", de: "Oeffentliche Turnierseite /t/:id (ohne Login einsehbar)" },
    { en: "Share button with QR code and copyable link", de: "Share-Button mit QR-Code und kopierbarem Link" },
    { en: "localStorage favorites for the public view", de: "localStorage-Favoriten fuer oeffentliche Ansicht" },
  ]},
  { version: "0.4.0", date: "2026-05-18", changes: [
    { en: "Extract tournament date and location from chess-results.com", de: "Turnier-Datum und Ort von chess-results.com extrahieren" },
    { en: "VPN IP rotation every 20 requests (via Gluetun API)", de: "VPN IP-Rotation alle 20 Requests (via Gluetun API)" },
    { en: "HTTP retry on transient network errors", de: "HTTP-Retry bei transienten Netzwerkfehlern" },
  ]},
  { version: "0.3.0", date: "2026-05-18", changes: [
    { en: "Browser notifications on a new round (Round Monitor)", de: "Browser-Benachrichtigungen bei neuer Runde (Round Monitor)" },
  ]},
  { version: "0.2.0", date: "2026-05-18", changes: [
    { en: "Changelog added (click on the version in the footer)", de: "Changelog hinzugefuegt (Klick auf Version im Footer)" },
    { en: "Crawler IP endpoint (VPN verification)", de: "Crawler IP-Endpoint (VPN-Verifizierung)" },
    { en: "Tournament Round Monitor (1h auto-check)", de: "Tournament Round Monitor (1h Auto-Check)" },
    { en: "Request logging middleware", de: "Request Logging Middleware" },
    { en: "No default values in compose example files", de: "Keine Default-Werte in Compose-Example-Dateien" },
  ]},
  { version: "0.1.0", date: "2026-05-18", changes: [
    { en: "Version number in the footer (desktop)", de: "Versionsnummer im Footer (Desktop)" },
  ]},
];
