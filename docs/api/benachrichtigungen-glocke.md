# Benachrichtigungen / Glocke (auth) — generischer In-App-Strom

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Benachrichtigungen / Glocke (auth) — generischer In-App-Strom“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Eine zentrale Navbar-Glocke mit „!"-Indikator. `Notifications`-Tabelle (`UserId`, `Type`, `DataJson` = i18n-Parameter, `Link`, `SeenAt?`), Text wird im Frontend über `notifications.type.<type>` lokalisiert. `NotificationService.CreateAsync` wird per fire-and-forget von den Domänen-Services aufgerufen. Trigger-Typen: `chessable_import_completed`/`_failed` (ChessableImportService), `friend_request_received`/`friend_request_accepted` (FriendService), `challenge_received`/`challenge_resolved` (ChallengeService), `revenge_performed` (RevengeNotificationService, Dual-Write). Frontend: `InAppNotificationService` + Glocke in der Navbar (löste den Freunde-Badge ab); 60-s-Poll für den Zähler; Browser-`NotificationService` (Web-Notification-API) bleibt separat für späteres Push. Mail/Push sind Phase 2/3.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/notifications?take=20` | Letzte Benachrichtigungen (neueste zuerst) |
| GET | `/api/notifications/history?page=&pageSize=` | Vollständige History (paginiert, neueste zuerst) + Gesamtzahl — für die `/notifications`-Seite |
| GET | `/api/notifications/count` | Anzahl ungelesener (Glocken-Badge) |
| POST | `/api/notifications/seen` | Alle als gelesen markieren (beim Öffnen der Glocke) |

**Aufbewahrung** (Codereview 2026-09-29, A9-003): gelesene Benachrichtigungen verfallen nach 180 Tagen
(`NotificationRetentionScheduler`, täglich 05:00 UTC). Die Kontolöschung ersetzt den eigenen Namen in FREMDEN
Benachrichtigungen (z. B. Freundschaftsanfrage, Herausforderung) durch `deleted_<id>`.

## „Analyse fertig" — Glocke, wenn eine Partie tief analysiert ist (0.904.0)

Wunsch 2026-11-08: „wenn ein registrierter user eine partie hochlädt informier ihn via glocke wenn fertig tief analysiert
(egal ob rookhub oder leaguehub). ausserdem wenn man bei einer partie analyse klickt auch benachrichtigen wenn tief durch".

- **Typ** `game_analysis_finished`, Daten `white`, `black`, Link auf die Partie DIESES Nutzers: `/games/{id}` (eigene
  Partie), `/g/{token}` (Gast auf einem Teilen-Link), `/club-games/{id}` (LeagueHub-Vereinspartie — RookHub-Route; LeagueHub
  hat keine eigene Glocke, dasselbe Konto sieht sie in RookHub).
- **„Tief durch"** = endgültig fertig: erster Durchgang `Done` UND keine Vertiefung offen (`RefineDepth` leer oder
  `RefinedAt` gesetzt) — `GameAnalysisWatchers.IsFinal`. Nach dem schnellen ersten Durchgang (Tiefe 20/1 Linie) kommt noch
  KEINE Glocke; die Vertiefung (30/5) ändert die Zahlen noch. Gemeldet wird aus der Pumpe (`GameAnalysisPump`) an genau den
  beiden Stellen, an denen eine Analyse endgültig wird: Ende des ersten Durchgangs ohne Vertiefung, Ende der Vertiefung.
- **Wer will es wissen** steht in `GameAnalysisWatchers` (Entität `GameAnalysisWatcher`): je Zeile ein Nutzer und ENTWEDER
  die Analyse (`GameAnalysisId`) ODER die Vereinspartie (`LeagueClubGameId`, deren Analyse der Stapel erst später anlegt).
  Eingetragen von:
  - „Partie analysieren" (`POST /api/games/{id}/analyze`, `…/shared/{token}/analyze`, Erweiterung
    `POST /api/extension/games/{id}/analyze`) — in jedem Zweig von `SavedGameAnalysisLink.AnalyzeCoreAsync` (verknüpfte,
    eigene, Vereins- oder neu angelegte Analyse);
  - Speichern über die Erweiterung (`POST /api/extension/games`, analysiert seit 0.667.0 gleich mit);
  - PGN-Upload in „Meine Partien" (`POST /api/games/import`), wenn die Partie dabei die laufende Analyse einer Vereinspartie
    übernimmt (ohne Klick wird eine hochgeladene Partie nicht gerechnet — die Glocke kommt dann mit „Partie analysieren");
  - LeagueHub-Upload mit Konto (PGN-Import, Partie aus einem Formular hinzufügen: `ClubGameWriter.SaveAsync`) und das
    nachträgliche Zuordnen eines ohne Konto hochgeladenen Uploads (`ClaimAsync`) — auch bei „Schwaz"-Partien, die den
    Hochladenden sonst nicht speichern: die Zeile ist intern, kein DTO trägt sie, sie geht mit der Glocke.
- **Kein Ping**: Analyse schon endgültig fertig (die Seite zeigt sie), gescheitert, Stapel-Analysen ohne Interessenten
  (Meister-, Liga-, Vereinspartien ohne Hochladenden), die **Lc0-Zweitanalyse** (`ClubSecondEngineScheduler`, Origin Club mit
  `EngineId`) — über die Vereinspartie zählt nur die Hauptanalyse.
- **Einmal je (Analyse, Nutzer)**: eindeutige Indizes `(GameAnalysisId, UserId)` und `(LeagueClubGameId, UserId)`; hat ein
  Nutzer dieselbe Analyse zweimal (Kopie + Vereinspartie), geht eine Glocke mit dem Link der älteren Zeile. Mehrere Nutzer
  an einer geteilten Analyse (Zug-Schlüssel, Teilen-Link) bekommen je ihre.
- **Aufbewahrung**: die Zeilen verschwinden mit der Glocke; liegengebliebene (Analyse nie fertig) räumt der tägliche
  `NotificationRetentionScheduler` nach 60 Tagen (`GameAnalysisWatchers.StaleAfter`); die Kontolöschung nimmt sie mit
  (`AnalysisDataEraser`); Analyse, Vereinspartie oder Konto gelöscht → Cascade.

## „Formular wartet auf Freigabe" — Glocke an die Vereinsverwalter (0.904.0)

Wunsch 2026-11-08: „wenn jemand eine Partie hochlädt und sie wartet auf Freigabe durch mich, dann gib mir einen
Glockenping". Der Fall: ein Partieformular über einen LeagueHub-**Teilen-Link ohne Konto** (`POST /api/league/s/{token}/club/scans`)
— nach dem Lesen (Modell oder Leser von außen) liegt die Einlesung offen, und übernimmt der Hochladende sie nicht selbst als
Vereinspartie, muss ein Verwalter sie freigeben (LeagueHub „Offene Einlesungen", `GET …/club/admin/scans`).

- **Typ** `league_scan_awaiting`, Daten `white`, `black` (aus der Lesung), `club`; an Admins und alle mit `league.manage`,
  die zum Verein der Einlesung gehören (Regel von `LeagueClubResolver.CanManageAsync`), in EINEM `CreateManyAsync`.
- **Link** absolut auf die Einlesung in LeagueHub: `App:LeagueHubBaseUrl` + `/verein/formular/{scanId}`; ohne Konfiguration
  kommt die Glocke ohne Link. Die Glocke öffnet absolute Ziele per Seitenwechsel (`openNotificationLink`).
- Ausgelöst in `ScoresheetProcessor.FinishAsync` (Liga-Zweig), nur bei `UserId == null`: wer mit Konto hochlädt, übernimmt
  selbst. Kein Doppel zu `league_batch_uploaded` (Stapel ohne Konto, Bilder werden dort nicht gelesen) oder zur
  Admin-Nachricht beim Stapel mit Konto.
