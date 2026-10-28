# Hintergrundjobs pausieren (Admin)

Ein Schalter hält alle **pausierbaren** Hintergrunddienste auf einmal an — zur Laufzeit, ohne Neustart, wirkt sofort
und übersteht Neustarts. Gedacht für Prod und Dev, wenn Rechenzeit, Engines oder externe Dienste frei sein sollen
(früher ging das nur je Dienst über Konfiguration + Neustart, z. B. `MasterAnalysis:Enabled=false`; der
Analyse-Worker hatte gar keinen Schalter).

## Zustand

- **Datenbank:** Tabelle `SystemSettings` (Schlüssel → Wert, dazu `UpdatedAt`, `UpdatedByUserId`, `UpdatedByName`),
  Zeile `BackgroundJobs.Paused` = `true`/`false`. Allgemein gehalten — weitere Laufzeit-Einstellungen gehören dorthin.
- **Konfiguration:** `BackgroundJobs:Paused=true` (ENV `BackgroundJobs__Paused`) **erzwingt** die Pause unabhängig von
  der Datenbank — Notbremse, wenn die Datenbank selbst das Problem ist. Der Schalter kann sie dann nicht aufheben
  (die Oberfläche sagt es).
- **Dienst:** `Services/BackgroundPause.cs` (Singleton): `IsPausedAsync()` liest höchstens alle 5 s aus der Datenbank
  (jeder Takt aller Dienste fragt hier), `IsPaused`/`Since`/`By` ohne Datenbankzugriff, `SetAsync()` schreibt und wirkt
  sofort (Merker ersetzt, Ereignis `Changed`). Ist die Datenbank nicht lesbar, gilt der letzte Stand weiter (eine
  Warnung beim Wechsel, nicht je Takt).

## Wirkung

- **`PeriodicWorker`** fragt vor jedem Takt. Pausiert → Takt fällt aus, nach höchstens 30 s (`PauseGate.Recheck`,
  bzw. dem eigenen kürzeren Takt, mindestens 1 s) wird wieder nachgesehen; ein in die Pause gefallener Tageslauf
  kommt so gleich nach dem Fortsetzen. Der Wechsel wird je Dienst EINMAL geloggt („…: Hintergrundjobs pausiert —
  Takte ruhen" / „…: Hintergrundjobs laufen wieder"). Nicht pausierbare Dienste überschreiben
  `protected virtual bool Pausable => false`.
- **Andere `BackgroundService`s** nutzen `PauseGate` (gleiche Prüfung + Wechsel-Log + letzter Takt), aufgelöst über
  ihre Scope-Fabrik — kein Konstruktor ändert sich.
- **`AnalysisJobWorker`**: startet bei Pause keine neuen Läufe, und **laufende Aufträge werden sofort angehalten** —
  derselbe Weg wie beim Live-Vorrang: Status `Paused`, KEIN Fehlversuch, Ergebnis und erreichte Tiefe bleiben, nach dem
  Fortsetzen geht es weiter. Bewusst nicht „zu Ende rechnen lassen": ein Auftrag darf Stunden dauern, die Pause soll
  aber sofort Rechenzeit freigeben.
- **`CourseTranslationWorker`**: kein neuer Auftrag; ein laufender wird wie bei der Sperrzeit abgebrochen und zurück in
  die Schlange gestellt (`CourseTranslationStop.Paused`).
- **`ScoresheetScanWorker`**: keine neue Einlesung; eine laufende (höchstens 10 min) wird fertig.
- **`ChessableImportResumeService`** (einmal beim Start): bei Pause werden unterbrochene Importe nur auf „wartend"
  gestellt, nicht angestoßen — der Watchdog holt sie nach dem Fortsetzen.

| Pausierbar | Dienste |
|---|---|
| ja | AnalysisJobWorker, GameAnalysisPumpService, MoveComparisonPumpService, MasterAnalysisScheduler, ClubSecondEngineScheduler, CourseTranslationWorker, ScoresheetScanWorker, EbookCoverWorker, EbookDiagramWorker, EbookDjvuConverter, EbookSolutionCheckWorker, EbookFingerprintWorker, TacticHarvestScheduler, ClubCopyLinkScheduler, PlayerHistoryScheduler, TournamentDirectoryScheduler, AutoSubscriptionScheduler, RoundMonitorService, LeagueOnlineSyncScheduler, PlayTimeSyncService, CalcSeriesAnnounceScheduler, ChessableCourseRefreshScheduler, ChessableImportFastLaneService, ChessableImportWatchdogService |
| nein | HeartbeatService (Gesundheit/log-watcher), AnonymousDataRetentionService, NotificationRetentionScheduler, EbookAnonymousCleanupWorker (Aufbewahrungsfristen, Speicher, Datenschutz), EngineBrokerMaintenanceService (schreibt Engine-Lebenszeichen aus dem Speicher in die DB), BackgroundTaskWorker/WebhookTaskWorker/HintTaskWorker (Warteschlangen NUR im Arbeitsspeicher, Folgearbeit von Nutzeraktionen — pausiert stauten sie sich und gingen beim Neustart verloren), DailyPuzzleScheduler (Tagespuzzle für alle sichtbar, billig), einmalige Startläufe (TournamentGroupingBackfillService, RepertoireCleanupBackfillService, KidsPuzzleSeeder) |

## Endpunkte (nur Admin-Rolle, `[Authorize(Roles = "Admin")]`)

| Methode | Route | Beschreibung |
|---|---|---|
| GET | `/api/admin/background-jobs/pause` | Stand + Dienste |
| PUT | `/api/admin/background-jobs/pause` | Rumpf `{ "paused": true }` — Schalter umlegen, Antwort wie GET |

Antwort: `{ paused, since, by, forcedByConfig, pausedInDb, services: [{ name, pausable, lastTick }] }` —
`paused` ist die WIRKSAME Pause (Schalter ODER Konfiguration), `pausedInDb` der Schalter selbst, `since`/`by` nur bei
Pause per Schalter. `services` = alle eigenen Hosted Services (pausierbare zuerst), `lastTick` = Ende des letzten
erfolgreichen Takts (UTC; `null` bei nicht pausierbaren Diensten ohne `IBackgroundJobInfo` oder vor dem ersten Takt).

## Oberfläche

Admin → Reiter „Hintergrundjobs" (`/admin?tab=jobs`, `features/admin/tabs/admin-background-jobs.component.ts`):
Schalter „Hintergrundjobs pausieren", Zustand Läuft/Pausiert, „Pausiert seit … von …", Hinweis bei erzwungener Pause,
Liste der Dienste mit „pausierbar"/„läuft immer" und letztem Lauf.
