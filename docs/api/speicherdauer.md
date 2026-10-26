# Speicherdauer — Fristen je Datenart

Übersicht aller heute laufenden Aufbewahrungsfristen der RookHub-API (Stand 2026-10-25, Codereview 2026-09-29 O-7) —
Grundlage für den Abschnitt „Speicherdauer“ der Datenschutzerklärung. Neue Frist ⇒ hier eine Zeile.

Der tägliche Aufräumlauf ist `AnonymousDataRetentionService` (PeriodicWorker, Startlauf sofort, dann alle 24 h, **nicht**
pausierbar; jede Datenart in eigenem Versuch). Konfiguration im Abschnitt `Retention` (`RetentionOptions`, Umgebung
`Retention__AnonymousDays=…`); fehlender, ungültiger oder Wert ≤ 0 = Vorgabe. Gelöscht wird portionsweise (≤ 1 000 Zeilen
je DELETE), ohne große Spalten zu laden.

## Daten ohne Konto (anonym)

| Datenart | Frist / Deckel | Job | Config-Schlüssel (Vorgabe) |
|---|---|---|---|
| Endless-Spielstände + -Läufe (`EndlessProgresses`, `EndlessSessions`, `UserId` leer) | 60 Tage ohne Berührung; Gesamtdeckel beim Schreiben | Aufräumlauf | fest (`AnonymousEndlessMaxAge`) |
| Punktepartien ohne Konto (`GuessSessions` + `GuessMoves`) | 90 Tage ab Ende (sonst Start); je Senke höchstens N, älteste zuerst | Aufräumlauf | `Retention:AnonymousDays` (90), `Retention:AnonymousCap` (200 000) |
| Buch-/Tagespuzzle-Versuche ohne Konto (`BookPuzzleAttempts`) | 90 Tage; Deckel wie oben | Aufräumlauf | dieselben |
| Versuche an geteilten Puzzles ohne Konto (`SharedPuzzleAttempts`, Kennung `s:…`) | 90 Tage; Deckel wie oben | Aufräumlauf | dieselben |
| Puzzle-Versuche ohne Konto (`PuzzleAttempts`) | 90 Tage; Deckel wie oben | Aufräumlauf | dieselben |
| getReview-Linien der Extension ohne Konto (`AnonymousChessableReviewLines`) | 90 Tage; uids ohne verknüpftes Konto 14 Tage | Aufräumlauf | fest |
| Liga-Entwürfe über Teilen-Link (`LeagueClubDrafts`, `UserId` leer) | 30 Tage ohne Bewegung; 5 je Anschluss (IPv6: /64), Gesamtdeckel | beim Anlegen/Auflisten | `Retention:AnonymousLeagueDraftsCap` (500); Frist fest |
| Formular-Einlesungen ohne Konto (Liga, Teilen-Link), nie übernommen | Foto + Lesung nach 14 Tagen geleert (Zeile bleibt fürs Kontingent) | Aufräumlauf | `Retention:ScoresheetOpenAnonymousDays` (14) |
| IP-Vermerk der Einlesungen ohne Konto (`ScoresheetScans.AnonIpHash`, HMAC) | 2 Tage | beim nächsten anonymen Upload | fest |
| E-Book-Uploads ohne Konto (Datei, Titelbild, Lesestände) | 30 Tage | `EbookAnonymousCleanupWorker` (stündlich) | `Ebooks:AnonymousRetentionDays` (30) |

## Daten mit Konto

| Datenart | Frist | Job | Config-Schlüssel (Vorgabe) |
|---|---|---|---|
| Benachrichtigungen (gelesen) | 180 Tage | `NotificationRetentionScheduler` (05:00 UTC) | fest (`NotificationService.SeenRetention`) |
| Admin-/Direktnachrichten (gelesen) | 365 Tage; ungelesene bleiben | Aufräumlauf | `Retention:MessagesDays` (365) |
| Formular-Einlesungen, gescheitert | Foto + Lesung nach 30 Tagen geleert (Zeile bleibt fürs Kontingent) | Aufräumlauf | `Retention:ScoresheetFailedDays` (30) |
| Formular-Einlesungen, übernommen (RookHub) | Foto bleibt bei der Partie; geht mit der Partie, dem Konto oder per `DELETE /api/scoresheets/{id}` | — | — |
| Formular-Archiv der Liga-Einlesungen (`ScoresheetScanArchives`) | 365 Tage | Aufräumlauf (und beim Abschluss einer Liga-Einlesung) | fest (`ScoresheetScanService.ArchiveRetention`) |
| Gemeinsame Anmeldung (`SharedSessionService`) | 30 Tage | Ablauf beim Prüfen | fest |
| Discord-Verknüpfungs-Token | 30 Tage | Ablauf beim Prüfen | `Discord:LinkTokenMaxAgeDays` (30) |
| ClubHub-Verknüpfungscode | 14 Tage | Ablauf beim Prüfen | fest |
| Konto selbst | bis zur Löschung durch den Nutzer (Kontolöschung räumt alles Zugehörige) | — | — |

## Außerhalb der Datenbank

| Datenart | Frist | Wo | Einstellung |
|---|---|---|---|
| Logs (IP, Nutzername, User-Agent) in Elasticsearch: `rookhub`, `crawler`, `piratechess` | 90 Tage nach Rollover (+ ≤ 7 Tage) | ILM-Policy `rookhub-logs-retention` — **Prod: erst nach Betreiber-Schritt aktiv** ([log-retention.md](../log-retention.md)) | `ILM_DAYS` in `scripts/es/ilm-apply.sh` |
| Datenbank-Sicherungen | 14 Tage (Standard) | `scripts/backup-db.sh` ([backup.md](../backup.md)) | `RETENTION_DAYS` |

Verwandt: [hintergrundjobs-pause.md](hintergrundjobs-pause.md) (welche Dienste der Admin-Schalter anhält — die Fristen nicht),
[partieformular-einlesen.md](partieformular-einlesen.md) (Löschweg der Formular-Fotos).
