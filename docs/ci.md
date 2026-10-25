# CI/CD (GitHub Actions)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „Arbeitsweise“).

- **CI/CD**: Docker-Images werden nach Push automatisch gebaut (GitHub Actions). Kein manueller Build nötig.
  Seit 0.434.2 laufen Test- und Build-Jobs **pfadgefiltert** (`.github/filters.yml`, von `test.yml` UND
  `docker.yml` gelesen): ein Push startet nur, was er berührt. Zwei Regeln hängen an Tests
  (`CiWorkflowTests`): ein **Tag-Lauf baut immer alle sechs Images** (`:latest` entsteht nur dort), und der
  `turnier`-, `kidhub`-, `leaguehub`- und `clubhub`-Filter enthalten den GETEILTEN Frontend-Code (alle Angular-Projekte importieren aus `src/app`).
  Ein neuer Job braucht also einen Filter — ein Tippfehler im Namen ist ein leerer Output und damit ein
  Job, der ab da nie mehr läuft.
  **Nur Release-Tags** (Codereview W2, I1-004): `docker.yml` startet nur bei `vX.Y.Z` (vorher `v*`), und der
  `changes`-Job bricht jeden Tag-Lauf ab, dessen Commit nicht auf `origin/master` liegt (`git merge-base
  --is-ancestor`). `:latest` hängt allein an dessen Output `release` — ein Sicherungs-Tag wie `vorher-umbau`
  auf einem Branch baut also nichts mehr und landet nie per Watchtower auf Prod. Also: erst master pushen,
  dann taggen.
  **Handstart** (seit 0.453.3): `gh workflow run docker.yml` baut ALLE Images und lässt vorher ALLE
  Tests laufen — bei `workflow_dispatch` bleibt der Filter-Schritt aus (dorny hielte master gegen master
  und setzte jeden Filter auf `false`), die Job-Bedingungen fangen den Fall über `github.event_name` ab.
  Gebraucht für den Fall, den die Pfadfilter selbst erzeugen: master ist rot (hier fremdverschuldet
  geerbt), der reparierende Push berührt nur Frontend-Pfade, und damit hat `build-api` zwei Versionen
  lang nicht gebaut — master grün, Code gepusht, und auf Dev läuft trotzdem der Stand von vorgestern
  (2026-09-09, Dev hing auf 0.452.1). Der Handstart auf master schiebt `:dev`, nicht `:latest`.
  **Vorbau + Umhaengen** (seit 0.494.1): `prebuild-api`/`-frontend`/`-turnier`/`-kidhub`/`-leaguehub`/`-clubhub` in `docker.yml` bauen die
  Images schon parallel zu den Tests und pushen sie unter einem Hilfs-Tag `ci-<run_id>`. Die Jobs hinter
  dem Gate bauen GAR NICHT mehr — sie haengen per `docker buildx imagetools create` nur die echten Tags
  (`:dev`/`:latest`/Semver) an dasselbe Image, eine Registry-Operation von Sekunden. Der Zwischenschritt
  ueber einen Schichten-Cache (v0.493.3) war gemessen wirkungslos: der Export der .NET-SDK-Stage nach
  `type=gha,mode=max` kostete mehr als der Build (Vorlauf 4:00 fuer einen 1:44-Build), der Cache war
  1:12 nach dem Start von `build-api` fertig, und der Lauf blieb bei 4:43 statt 5:04.
  Vier Regeln haengen an `CiWorkflowTests`: der Vorbau pusht NUR `ci-*` (niemals `:dev`/`:latest`/Semver —
  sonst zeigten die Tags, die Watchtower und der Deploy lesen, auf ungetesteten Code), er wartet NICHT auf
  `tests`, er laeuft bei Tag UND Handstart mit (sonst faellt der Build-Job mangels `needs` aus → kein
  `:latest`, kein Release, obwohl alles gruen ist), und hinter dem Gate steht `imagetools create` statt
  `build-push-action`. Ein uebriggebliebener `ci-*`-Tag ist ein Lauf mit roten Tests; Wegraeumen ist
  Kosmetik und gehoert in einen eigenen zeitgesteuerten Workflow, nicht hierher.
  **Test-Gate parallel** (seit 0.803.5/0.804.0, gemessen 7:10 → 5:44 vor der zweiten Stufe): die Integrations-DB
  (`test-rookhub-integration`) läuft per `--tmpfs` im RAM — jede Fixture spielt alle Migrationen ein, InnoDB
  fsynct bei jedem DDL; die Migrations-Tests stehen in drei Klassen (xUnit: Tests einer Klasse seriell, Klassen
  parallel). `build-frontend` prüft nur `app` + Linter, `build-frontend-hubs` die vier Hubs. `test-rookhub` ist
  eine Matrix aus zwei Hälften: `scoresheet-accounts` (die langsamsten Klassen, je 30–70 s) und `rest` = deren
  exakte Verneinung. Wächter in `CiWorkflowTests`: jedes Angular-Projekt wird in einem der Frontend-Jobs getestet
  und gebaut, und die zweite Unit-Hälfte ist die Verneinung der ersten (deren Bereiche müssen als Testordner
  existieren). Ein neuer langsamer Bereich → in die erste Hälfte aufnehmen, die zweite wird mitgeändert.
  **GitHub-Release je Tag** (seit 0.804.0): `github-release` in `docker.yml` legt nach allen Image-Jobs einen
  Release an, Text = Changelog-Einträge seit dem vorigen Versions-Tag (`scripts/release-notes.mjs`, Test in
  `scripts/tests/test_release_notes.sh`). APK + AAB wandern vom bisherigen Latest-Release mit — `/install` lädt
  `releases/latest/download/app-release-signed.apk`, ein Latest ohne die Datei wäre ein toter Link.
  **Form aller Workflows** (Codereview I1-018, `CiWorkflowTests` hält sie fest): oben `permissions: {}`, Rechte nur je
  Job; jede Action per Commit-SHA mit Versionskommentar, eine Action = ein SHA in allen Dateien (beim Anheben alle
  gemeinsam); `node-version` = Major aus `src/frontend/Dockerfile`.
  **.NET-SDK aus dem Build-Feed** (0.838.1): die Test- und Audit-Jobs holen das SDK über die eigene Composite-Action
  `.github/actions/dotnet-sdk` (Eingabe `channel`, Vorgabe `10.0`) statt über `actions/setup-dotnet`. Dessen Skript löst
  `10.0.x` über den Kurzlink `aka.ms/dotnet/10.0/…` auf, und der zeigte am 25.10.2026 auf `dotnetorigin.blob.core.windows.net`
  mit „409 Public access is not permitted“ — vier Test-Jobs rot, bevor ein Test lief, Tag v0.838.0 ohne Images. Die Action
  nimmt ein vorinstalliertes SDK des Kanals, sonst liest sie `Sdk/<Kanal>/latest.version` von `builds.dotnet.microsoft.com`
  und ruft `dotnet-install.sh --version <genau>` auf — mit genannter Version steht aka.ms nicht mehr in dessen Quellenliste
  (Feed + `ci.dot.net/public`). Framework-Bump = `channel` in `test.yml` (3×) und `audit.yml` heben;
  `DeploymentConfigTests.TestWorkflow_PinsDotnet10_AndRunsFrontendSpecs` hält Action, Kanal und Quelle fest; `.github/actions/**`
  steht in jedem Pfadfilter mit CI-Dateien.
  **Skript-Tests** (Codereview I1-015): Job `test-scripts` in `test.yml` führt jeden Test unter `scripts/tests/` aus
  (bash bzw. python3, ohne Container/Netz, `timeout-minutes: 5`). Ein neuer Skript-Test braucht dort eine eigene
  `- run:`-Zeile — `CiWorkflowTests.EveryScriptTest_RunsInTheTestWorkflow` prüft das.
  **Werkzeuge mitbauen** (Codereview A6-011, Zerlegung Z17): `test-rookhub` baut in der Hälfte `rest` nach den
  Tests `tools/LibraryImport` und `tools/ScoresheetBench` (`dotnet build`, Sekunden — die API ist dort schon gebaut).
  Beide referenzieren die API, gehen aber in kein Image; vorher fiel ein Bruch erst beim nächsten Lauf von Hand auf.
  Ihre Ordner stehen im `api`-Filter (eine Änderung NUR am Werkzeug startet damit Tests und `prebuild-api` — das Image
  bleibt inhaltlich gleich). `CiWorkflowTests.EveryDotnetTool_IsBuiltInTheTestGate`: jedes `tools/<Name>/*.csproj`
  braucht eine `- run: dotnet build tools/<Name>`-Zeile im Job und `'tools/<Name>/**'` im `api`-Filter.
  **Crawler-Vertrag** (Codereview I2-010): Job `test-crawler-contract` (zuletzt getaggter Crawler,
  `CrawlerContractTests` mit `CRAWLER_REPO`) ersetzt `test-crawler`; der Filter `crawler` = die Leser des Vertrags.
  **Melden statt still** (Codereview I1-014): der nächtliche E2E-Lauf und der Montags-Audit melden sich über
  `.github/scripts/ci_alert.sh` in Discord (Secret `DISCORD_CI_WEBHOOK`). Die Audit-Scans werden bei Befund rot (der
  Job bleibt dank `continue-on-error` grün), der Job `melden` läuft nur beim `schedule`-Lauf. Geprüft wird jede
  `requirements*.txt` im Repo (pip-audit): die aiohttp-Fassung des Engine-Providers steht in
  `engine-provider/requirements.txt` (Image, CI und Audit lesen sie dort, nicht mehr im Dockerfile), die der
  Wartungs-Skripte in `scripts/requirements.txt` (Codereview I1-011).
  **Laufende API-Fassung** (Codereview I1-017): das API-Image trägt `BUILD_GIT_SHA`/`BUILD_GIT_REF` (Build-Args aus
  `prebuild-api`); `GithubActionsService` meldet für rookhub `ApiSha`/`ApiRef` getrennt vom Frontend (`RunningSha`).
  **Frontend-Linter** (Codereview F8-004): `npm run lint` in `src/frontend/app` (`eslint.config.mjs`, alle Regeln
  `'warn'`; die CI hält die Zahl je Regel mit der Ratsche unten). Worktrees mit `node_modules`-Symlink auf eine andere
  Kopie brauchen dafür erst `npm ci`.

## ESLint-Ratsche (Frontend, Codereview F8-004 Phase 2)

`build-frontend` in `test.yml` prüft nach `npm ci` mit `node scripts/eslint-ratchet.mjs` (aus `src/frontend/app`),
dass **keine ESLint-Regel mehr Meldungen hat als in `src/frontend/app/eslint-baseline.json`** steht. Die Regeln selbst
bleiben `'warn'` (`eslint.config.mjs`), der Bestand (Stand 24.10.2026: 1 222 Meldungen, davon 522 `prefer-inject`,
383 `prefer-signals`, 137 `prefer-on-push`) bleibt stehen — er darf nur nicht wachsen. Der Schritt hat bewusst KEIN
`continue-on-error`; die Tabelle Regel | Baseline | jetzt | Δ landet in der Job-Summary. Davor läuft
`node --test scripts/eslint-ratchet.test.mjs` (die Vergleichslogik, Sekunden). `CiWorkflowTests` hält fest: Schritt
ohne `continue-on-error`, Baseline gültig (ganze Zahlen ≥ 0, sortiert, im Skript-Format), Konfiguration, Baseline und
`scripts/**` im `frontend`-Filter, und `'error'` nur für Regeln mit Baseline 0.

- **Rot?** Die Ausgabe nennt Regel, Baseline → jetzt und alle Dateien mit Treffern dieser Regel (die neue Stelle ist
  meist die eigene geänderte Datei). **Die Stelle fixen, nicht die Baseline heben.** Lokal: `npm run lint:ratchet`.
- **Warnungen abgebaut?** `npm run lint:ratchet:update` schreibt die kleineren Zahlen in die Baseline (senkt nur; eine
  gestiegene Regel bleibt stehen, das Skript endet dann mit 1) und gehört in denselben Commit — sonst kann der nächste
  den Abbau unbemerkt wieder auffüllen. Gesunkene Zahlen ohne Update bleiben grün (Hinweis „Baseline kann gesenkt werden").
- **Anheben** nur mit `node scripts/eslint-ratchet.mjs --update --force` und Begründung im Commit (z. B. eine neue
  Regel mit Bestand bewusst aufgenommen). Eine Regel ohne Baseline-Eintrag zählt wie Baseline 0; Meldungen ohne Regel
  (Parse-Fehler) laufen unter `(Parse-Fehler)` und sind damit immer rot.
- **Phase 3:** erreicht eine Regel Baseline 0, darf sie in `eslint.config.mjs` auf `'error'`.
