---
name: rh-commit
description: Commit und Push im RookHub-Repo nach Hausregel — Version + zweisprachiger Changelog, nur eigene Dateien, Build, Rebase, Push, andere Sitzung informieren. Verwenden bei JEDEM Commit in rookhub/ (auch reine Doku). Aufruf „/rh-commit" oder automatisch.
---

# RookHub: committen und pushen

Gilt für jede Arbeitskopie (`~/claude/rookhubstack*/rookhub`). Reihenfolge einhalten; jeder Schritt verhindert einen
Fehler, der schon einmal passiert ist.

1. **Lock prüfen.** `cat ../.agent-lock` muss DEINE Sitzung nennen. Fremder Lock → nicht hier committen
   (Lock-Protokoll in `CLAUDE.md`).
2. **Tests.** Jede Änderung braucht einen Test (Ausnahme: reine Doku). Gezielt laufen lassen (Befehle in
   `CLAUDE.md` → „Tests gezielt"), bei Änderungen am geteilten Frontend-Code alle betroffenen Projekte namentlich.
3. **Frischer Stand.** `git fetch -q && git pull --rebase --autostash` — ERST JETZT die Version vergeben, sonst verbraucht die
   andere Kopie dieselbe Nummer.
4. **Version + Changelog.** `APP_VERSION` in `src/frontend/app/src/environments/changelog.ts` erhöhen (Fix/Doku =
   Patch, Feature = Minor) und oben in `changelog-data.ts` einen Eintrag `{ version, date, changes: [{ en, de }] }`
   einfügen. Im Text nur typografische Anführungszeichen `„…“` (der Hook `post-edit.sh` meldet gerade `"`).
5. **Build.** `cd src/frontend/app && npx ng build app` (repliziert die CI; `ng test` fängt Changelog-Fehler nicht).
   Bei Backend-Änderungen `dotnet build` bzw. die betroffenen Tests.
6. **Nur eigene Dateien stagen.** `git status -s` LESEN, dann `git add <pfade>` — nie `git add -A`/`commit -a`
   (fremde, ungebundene Änderungen anderer Sitzungen landen sonst in deinem Commit).
7. **Commit.** Betreff `typ(bereich): was (X.Y.Z)`, Rumpf: warum. Abschluss mit der Co-Authored-By-Zeile.
8. **Push.** `git pull --rebase --autostash` (kamen fremde Commits: neu bauen + testen; Konflikte in `changelog*.ts` nach
   INHALT lösen, eigene Version über die fremde heben) → `git push`. Nie auf einen roten master pushen.
9. **Melden.** Dem User die Version nennen. Läuft eine andere Sitzung (`ListAgents`), ihr per `SendMessage`
   Commit + Version + nächste freie Version sagen.
10. **Lock freigeben** (`rm ../.agent-lock`), wenn die Arbeit in dieser Kopie fertig ist — direkt nach dem Push,
    NICHT auf die CI warten (Nutzerwunsch; er meldet sich, wenn sie rot wird). **Nicht** taggen, **nicht** deployen,
    außer der User sagt es ausdrücklich.
