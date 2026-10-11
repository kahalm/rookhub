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
