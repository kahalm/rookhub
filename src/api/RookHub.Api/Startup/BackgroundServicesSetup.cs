using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Club;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Kids;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Library;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.Tactics;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Startup;

/// <summary>
/// ALLE Hosted Services an EINER Stelle (Codereview 2026-09-29, A8-016 / Zerlegung Z10). Die Reihenfolge unten ist die
/// Startreihenfolge (der Host startet in Registrierungsreihenfolge und stoppt rückwärts) — beim Einfügen nicht umsortieren.
/// Die Fachdienste, die ein Worker braucht (Signale, Warteschlangen, Singletons), registriert der jeweilige Bereich in
/// <see cref="DomainServicesSetup"/>; hier steht nur, WELCHE Schleifen laufen.
///
/// <para>Schalter: „immer" = ohne Registrierungsbedingung; ein interner Schalter beendet den Dienst nach dem Start selbst
/// (PeriodicWorker <c>Enabled</c> bzw. eigene Prüfung). Nur <c>Chessable:Enabled</c> entscheidet schon hier — der Wert wird
/// in Program.cs EINMAL gelesen (<see cref="ChessableSwitch"/>) und hereingereicht.</para>
///
/// <code>
/// Dienst                              Schalter                              Grund
/// EbookCoverWorker                    intern: Ebooks:Root                   Titelbilder der E-Books
/// EbookDiagramWorker                  intern (EbookOptions)                 Diagramme aus Schach-Font-PDFs je Buch
/// EbookAnonymousCleanupWorker         intern: Ebooks:Root                   anonyme E-Book-Uploads aufräumen
/// EbookDjvuConverter                  intern: Ebooks:Root                   DjVu → PDF
/// EbookSolutionCheckWorker            intern: Ebooks:EngineCheck            Stockfish prüft/löst Buchaufgaben
/// EbookFingerprintWorker              intern: Ebooks:Root/LibraryRoot       ISBN + Text-Fingerabdruck je Datei (Dubletten)
/// ClubCopyLinkScheduler               immer                                 Kopien der Vereinspartien verknüpfen
/// ClubSecondEngineScheduler           intern: ClubSecondEngine:EngineName   Vereinspartien zusätzlich auf Lc0
/// TacticHarvestScheduler              intern: TacticHarvest:Enabled         Taktik-Ernte aus fertigen Analysen
/// MoveClassHarvestWorker              immer                                 Zug-Klassen vorrechnen (Version)
/// LineStudyWorker                     immer                                 Linien „ohne Engine" rechnen
/// LeagueOnlineSyncScheduler           intern: LeagueOnline:Enabled          Online-Partien der Ligaspieler
/// AnonymousDataRetentionService       immer (auch ohne Chessable)           Aufbewahrungsfristen (anonyme Senken, Retention:*)
/// NotificationRetentionScheduler      immer                                 Benachrichtigungen 180 Tage
/// EngineBrokerMaintenanceService      immer                                 Engine-Lebenszeichen in die DB
/// AnalysisJobWorker                   immer (Singleton, s. Engines)         Hintergrund-Analyseaufträge
/// CourseTranslationWorker             immer (QuietHours)                    Kurs-Übersetzungsaufträge
/// GameAnalysisPumpService             immer                                 Partie-Analysen nachfüttern
/// MoveComparisonPumpService           immer                                 „Züge vergleichen"
/// MasterAnalysisScheduler             intern: MasterAnalysis:Enabled        Meisterpartien analysieren
/// ScoresheetScanWorker                immer                                 Partieformulare einlesen
/// BackgroundTaskWorker                immer                                 allgemeine Warteschlange
/// WebhookTaskWorker                   immer                                 schach-bot-Webhooks
/// HintTaskWorker                      immer                                 Tipp-Generierung
/// ChessableImportWatchdogService      immer (Lanes intern per Schalter)     Extension-Pflichten + Lane-Netz
/// ChessableImportFastLaneService      immer                                 voll gecachte Importe (netzfrei)
/// ChessableImportResumeService        Chessable:Enabled                     unterbrochene Importe fortsetzen
/// AutoSubscriptionScheduler           immer                                 Turnier-Abos + Auto-Favoriten (24 h)
/// RoundMonitorService                 immer                                 Rundenmonitor
/// HeartbeatService                    immer                                 Lebenszeichen für den log-watcher
/// DailyPuzzleScheduler                immer                                 Tagespuzzle 00:00 UTC
/// ChessableCourseRefreshScheduler     Chessable:Enabled                     Kurslisten-Refresh 04:00 UTC
/// CalcSeriesAnnounceScheduler         immer                                 Kalkulations-Serie ankündigen
/// TournamentDirectoryScheduler        intern: TournamentDirectory:Enabled   Turnierverzeichnis-Sweep 03:00 UTC
/// PlayerHistoryScheduler              intern: PlayerHistory:Enabled         „Meine Turniere" 04:30 UTC
/// TournamentGroupingBackfillService   immer (einmalig)                      Gruppenschlüssel im Altbestand
/// RepertoireCleanupBackfillService    immer (einmalig)                      Repertoire-Bereinigung im Altbestand
/// KidsPuzzleSeeder                    immer (einmalig)                      Kinder-Leiter aufbauen
/// PlayTimeSyncService                 immer                                 Spielzeit Lichess/chess.com
/// </code>
/// </summary>
public static class BackgroundServicesSetup
{
    public static IServiceCollection AddRookHubBackgroundServices(this IServiceCollection services, bool chessableEnabled)
    {
        // Was aus dem ExecuteAsync eines Hintergrunddienstes fliegt, beendet den GANZEN Host. Das ist der .NET-Default,
        // hier ausdrücklich und bewusst: ein still toter Worker bliebe bis zum nächsten Deploy tot, der Container dagegen
        // startet neu (restart: unless-stopped). Deshalb darf ein Dienst nur beim ECHTEN Shutdown werfen — genau diese
        // eine Fehlerpolitik setzt PeriodicWorker um (Codereview 2026-09-29, A8-008).
        services.Configure<HostOptions>(o =>
            o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

        // E-Book-Bibliothek (0.764.0) und Diagramme aus Schach-Font-PDFs (0.766.0), im Hintergrund je Buch.
        services.AddHostedService<EbookCoverWorker>();
        services.AddHostedService<EbookDiagramWorker>();
        services.AddHostedService<EbookAnonymousCleanupWorker>();
        services.AddHostedService<EbookDjvuConverter>();
        // Stockfish prüft die Buchlösungen und löst Aufgaben ohne Buchlösung (0.772.0); Ebooks__EngineCheck=false schaltet ab.
        services.AddHostedService<EbookSolutionCheckWorker>();
        // Fingerabdrücke (ISBN + Text-MinHash) für die Upload-Dublettenprüfung nachholen (0.863.0).
        services.AddHostedService<EbookFingerprintWorker>();
        services.AddHostedService<ClubCopyLinkScheduler>();
        services.AddHostedService<ClubSecondEngineScheduler>();
        // Taktik-Ernte (0.657.0): fertige Analysen → Aufgaben in Kursen
        services.AddHostedService<TacticHarvestScheduler>();
        // Zug-Klassen ernten (0.854.0): fertige Analysen ohne oder mit altem Regelstand (GameMoveClasses.Version) nachrechnen.
        services.AddHostedService<MoveClassHarvestWorker>();
        // „Ohne Engine analysieren" (0.855.0): Stellungen der eigenen Linien gedrosselt rechnen und übernehmen.
        services.AddHostedService<LineStudyWorker>();
        services.AddHostedService<LeagueOnlineSyncScheduler>();
        // Retention der anonymen Endless-Spielstände (offener Pfad, frei wählbare Session-Id → sonst
        // unbegrenztes Wachstum mit je bis zu 1 MB Spielstand) und der anonymen getReview-Senke der Extension
        // (90 bzw. 14 Tage). IMMER registriert, unabhängig von `Chessable:Enabled`: der Extension-Endpunkt nimmt
        // auch mit Schalter aus an — hinter dem Schalter (im Kurslisten-Refresh) lief die Retention auf PROD nie.
        // Seit O-7 (2026-10-25) der tägliche Aufräumlauf aller Fristen ohne eigenen Dienst: anonyme Versuchs-Senken
        // (Retention:AnonymousDays/AnonymousCap) und
        // gelesene Admin-/Direktnachrichten (Retention:MessagesDays), Formular-Fotos
        // (Retention:ScoresheetFailedDays/ScoresheetOpenAnonymousDays) und das Formular-Archiv. Übersicht: docs/api/speicherdauer.md.
        services.AddHostedService<AnonymousDataRetentionService>();
        // Benachrichtigungen: gelesene nach 180 Tagen weg, Namen gelöschter Konten in fremden Glocken ersetzen (A9-003).
        services.AddHostedService<NotificationRetentionScheduler>();
        services.AddHostedService<EngineBrokerMaintenanceService>();
        // Hintergrund-Analyseaufträge: derselbe Singleton, den AnalysisJobService zum Unterbrechen benutzt.
        services.AddHostedService(sp => sp.GetRequiredService<AnalysisJobWorker>());
        // Kurs-Übersetzungsaufträge (0.548.0): einer nach dem anderen, Sperrzeiten der Spark (QuietHours).
        services.AddHostedService<CourseTranslationWorker>();
        // Hält die Partie-Analysen in Bewegung (fertige Aufträge einsammeln, neue nachfüttern).
        services.AddHostedService<GameAnalysisPumpService>();
        // Züge vergleichen (0.602.0): Kandidatenzüge über Hintergrund-Aufträge durchrechnen.
        services.AddHostedService<MoveComparisonPumpService>();
        // Meisterpartien im Hintergrund analysieren — zu den Zeiten der Uebersetzung (QuietHours), auf den Engines der
        // Haus-Engine, jeder andere Auftrag hat Vorrang (MasterAnalysis:Enabled=false schaltet ab).
        services.AddHostedService<MasterAnalysisScheduler>();
        // Partieformular einlesen (0.529.0): eigener DB-gestützter Worker.
        services.AddHostedService<ScoresheetScanWorker>();
        services.AddHostedService<BackgroundTaskWorker>();
        // Eigene Queue + Consumer NUR für schach-bot-Webhooks, damit Solver-Updates nicht
        // hinter/unter einem Chessable-Import-Schwung in der allgemeinen Queue verhungern.
        services.AddHostedService<WebhookTaskWorker>();
        // Eigene Queue + Consumer fuer die Tipp-Generierung (Stockfish + LLM je Linie): ein grosser Kurs-Upload hielt den
        // einzigen allgemeinen Consumer sonst stundenlang fest; persoenliche Kurse sind dort gedeckelt (A4-002).
        services.AddHostedService<HintTaskWorker>();
        // Der RookHub-EIGENE Chessable-Weg (Bearer hinterlegen, Kurse ueber piratechess holen) laesst
        // sich abschalten: `Chessable:Enabled=false`. Dann fallen der Drain des Watchdogs, der Resume-Dienst und
        // der naechtliche Kurslisten-Refresh weg, und `/api/chessable/*` antwortet 404 (ausser `/api/chessable/admin/*`,
        // siehe Fast-Lane unten). Die Download-Lane laeuft BEWUSST weiter: ein nicht gecachter Admin-Import („Kurse
        // von Usern holen") reiht per `EnqueueNextAsync` ein Ticket ein, der BackgroundTaskWorker ruft `RunNextAsync`
        // ohne Schalterpruefung, und der Kurs wird dabei echt von Chessable geholt. Der Weg ueber die
        // RepCheck-EXTENSION (`/api/extension/*`) ist davon UNBERUEHRT — genau darum geht es beim
        // Abschalten: alle sollen vorerst die Extension benutzen (Entscheidung 2026-09-09).
        // Der Watchdog laeuft IMMER: neben dem Lane-Sicherheitsnetz (beim Neustart verworfene Tickets /
        // fehlende Nachreihung nach Abschluss) erledigt er die Pflichten des EXTENSION-Wegs — abgelaufene
        // Browser-Sitzungen und verwaiste Browser-Importe schliessen. Genau die fielen aus, solange er hinter
        // `Chessable:Enabled` stand: auf PROD (Flag seit 2026-09-09 aus) blieb jeder abgebrochene Browser-Import
        // fuer immer auf „laeuft". Was zu den eigenen Lanes gehoert, schaltet er selbst ab (LanesEnabled).
        services.AddHostedService<ChessableImportWatchdogService>();
        // Schnelle Lane: treibt voll-gecachte Importe sofort, parallel zur Download-Lane. Laeuft IMMER: sie ist
        // netzfrei (kein Chessable-Abruf, der Kurs kommt aus dem piratechess-Cache), und der Admin-Tab „Kurse von
        // Usern holen" bleibt auch mit `Chessable:Enabled=false` bewusst in Betrieb — ein voll gecachter
        // Admin-Import bekommt kein Queue-Ticket und stand ohne sie fuer immer auf „wartend" (Codereview 2026-09-29).
        services.AddHostedService<ChessableImportFastLaneService>();
        if (chessableEnabled)
        {
            // Beim Start unterbrochene Chessable-Importe ("running") fortsetzen.
            services.AddHostedService<ChessableImportResumeService>();
        }
        services.AddHostedService<AutoSubscriptionScheduler>();
        services.AddHostedService<RoundMonitorService>();
        // Periodisches Lebenszeichen nach ES (Standard 60 s) → log-watcher erkennt toten Dienst.
        services.AddHostedService<HeartbeatService>();
        // Taegliche Tagespuzzle-Zuordnung um 00:00 UTC (siehe DailyPuzzles-Tabelle).
        services.AddHostedService<DailyPuzzleScheduler>();
        // Taeglicher Chessable-Kurslisten-Refresh (04:00 UTC): aktualisiert alle hinterlegten Bearer,
        // sperrt tote Tokens, benachrichtigt Admins bei neuen Kursen. Nur wenn der eigene
        // Chessable-Weg ueberhaupt an ist — sonst liefe er jede Nacht gegen eine abgeschaltete
        // Funktion und sperrte dabei womoeglich Bearer. (Die Retention der anonymen getReview-Senke gehoert
        // deshalb NICHT hierher, sondern in den immer laufenden AnonymousDataRetentionService.)
        if (chessableEnabled)
            services.AddHostedService<ChessableCourseRefreshScheduler>();
        // Kalkulations-Serie: kündigt freigegebene Wochen an den Verteiler an (Standard alle 5 min).
        services.AddHostedService<CalcSeriesAnnounceScheduler>();
        // Turnierverzeichnis: naechtlicher Sweep der chess-results-Turniersuche (03:00 UTC),
        // Nachbarlaender taeglich, alle uebrigen Foederationen rotierend ueber die Woche.
        services.AddHostedService<TournamentDirectoryScheduler>();
        // „Meine Turniere": frischt Trefferlisten und Spielerkarten taeglich (04:30 UTC) im
        // Hintergrund auf, damit der Verlauf nicht erst beim Ansehen entsteht.
        services.AddHostedService<PlayerHistoryScheduler>();
        // Traegt Basisname/Gruppenschluessel im Altbestand nach (einmalig, idempotent) — ohne das
        // bleibt die Gruppen-Zusammenfassung bis zum naechsten Sweep unsichtbar.
        services.AddHostedService<TournamentGroupingBackfillService>();
        services.AddHostedService<RepertoireCleanupBackfillService>();
        // Baut die Kinder-Leiter, wenn sie fehlt oder aus einem aelteren Lehrplan stammt.
        services.AddHostedService<KidsPuzzleSeeder>();
        // Externe Spielzeit (Lichess/chess.com) → Kategorie „Spielen" im Trainingsziele-Tracker.
        services.AddHostedService<PlayTimeSyncService>();
        return services;
    }
}
