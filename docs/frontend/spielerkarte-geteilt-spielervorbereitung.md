# Spielerkarte geteilt + Spielervorbereitung (`shared/player-card/`, `features/prep/`, 0.636.0–0.639.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


- `src/app/shared/player-card/`: player-card, opening-tree, tree-filter-bar, game-replay, online-accounts,
  account-checks, account-suggestions (Komponenten) und tree-filter, account-format, account-checks (Logik). LeagueHub
  bindet sie über `@rh/shared/player-card/…` ein; der Umzug (0.636.0) war ein reiner `git mv` mit Importpfaden.
- Daten über das Token `PLAYER_CARD_API` (`player-card-api.ts`); Vorgabe ist der `LeagueApiService`, die
  Spielervorbereitung stellt `PrepCardApi` bereit. `PlayerCard.key` ist der API-Schlüssel, wenn es nicht die FIDE-ID ist.
  Eingaben der Karte: `inline` (Teil einer Seite) und `note` (eigener Quellen-Hinweis); `tree-filter-bar` hat `unsure`.
- Prep-Oberfläche: `/prep` (Suche, `?q=`) und `/prep/:id` (`prep.view`, `permissionGuard`). Die Spielerseite stellt
  `PrepCardApi` (als `PLAYER_CARD_API`) und `PrepLeagueApi` (als `LeagueApiService`, `useExisting`) bereit:
  `PrepLeagueApi` lenkt Übernehmen/Verwerfen/(i) der geteilten Bausteine `lh-account-suggestions`/`lh-account-checks`
  auf `/api/prep/*` und merkt sich in `checkNote`, warum eine (i)-Prüfung abgesagt wurde (409 `busy`, 503
  `rateLimited`) — den Grund zeigt der Abschnitt „Online-Konten suchen“ (`prep-accounts.component.ts`), denn der
  geteilte Baustein sagt nur „ließ sich gerade nicht laden“. Darunter „Eingetragene Konten“ mit Umstufen und Entfernen
  nach einer Rückfrage im Abschnitt (0.639.0); bei einem Ligaspieler nur ansehen. Geteilte Komponenten bleiben dafür
  unverändert.
- Gestaltung: `features/prep/prep-card.scss` lädt `leaguehub.scss` per `meta.load-css`, begrenzt auf
  `.prep-card-scope`, als nicht eingebundenes Style-Bündel `prep-card.css` (`angular.json`, `inject: false`), das die
  Spielerseite nachlädt — sonst sprengte die LeagueHub-Gestaltung das Komponenten-Budget. Klassen in Prep-Komponenten
  nicht wie LeagueHub-Klassen nennen (`.acc-list` gibt es dort schon → `prep-acc-list`). Am Handy (≤ 520 px) steht in
  der Prep-Karte die Eröffnung der letzten Partien unter dem Gegner (nur `prep-card.scss`).
- Trainingslinien (0.702.0): `training-lines.component` (Abschnitt „Trainingslinien", aufklappbar, lädt erst beim
  Öffnen) erscheint nur, wenn `PLAYER_CARD_API.trainingLines` existiert, jemand angemeldet ist und kein Teilen-Token
  gilt; deutsch wie die übrige Karte. `trainerParams` liefert den Gegner für den Trainer (`prep:<id>` + `all`/`twin`,
  Vorgabe `league:<fide>`); `LeagueApiService` hat dafür genau EINE Methode. Trainer: `?opponent=prep:<id>|league:<fide>
  &color=…` (+ Filter) holt alle Trainingslinien, sortiert danach statt zu mischen, nur Kapitel der Farbe; leer → einmal
  anderer Modus. Ohne den Parameter unverändert. Hilfen in `shared/player-card/training-lines.ts`.
- „Show me lines to train" (0.706.0): Knopf `button.tl-create` im Abschnitt (i18n `prep.trainingRepertoire.*` in
  en/de/hr/hu; in LeagueHub ohne gesetzte Sprache die deutschen Texte aus der Komponente), Rückfrage über
  `ConfirmService` (nennt den Namen, sagt „gleichnamiges wird ersetzt"), dann zur neuen Seite `/repertoires/{id}
  ?trainColor=w|b` (LeagueHub per `jumpToRookHub`). `repertoire-detail` setzt mit `?trainColor=` einmal die
  Trainingsfarbe ALLER Kapitel (nur eigenes Repertoire) — die Auto-Erkennung könnte an einer Auswahl von Linien kippen.
  Die Liste zeigt nicht voll getroffene Linien als „bis 5…a6 dabei" + „Anfang p %".
- Alle markierten (0.709.0): Vorgabe der Karte ist `repertoire = null` (alle markierten); oben der Farbumschalter „Ich
  habe Weiß / Schwarz" (`.tl-color`), daneben das Menü `.tl-rep` mit „Alle markierten" + den einzelnen Repertoires
  (gemerkt in `lh-training-lines` = `{ repertoire, color }`). Jede Linie zeigt „Repertoire · Kapitel"; „Trainieren"
  springt in den Trainer von `line.repertoireId`. „Alle in dieser Reihenfolge trainieren" (`.tl-all`) nur bei
  Einzelwahl, sonst der Hinweis `.tl-all-hint` auf „Trainings-Repertoire anlegen". Eigene Kapitelfarben gehen je
  Repertoire mit (beim ersten Laden über alle markierten einmal nachgeladen, weil die Ids erst dann bekannt sind).
- Schätzung (0.715.0): Kopfzeile `.tl-estimate` („Von Huber nur 3 passende Partien — Lücken mit Lichess-Partien der Stufe
  … geschätzt", ohne Partien bzw. bei vielen andere Fassung), `.tl-incomplete` bei `explorerIncomplete`; je Linie
  `.tl-tag` „geschätzt" (nur Lichess) bzw. „ab 2…d5 geschätzt (Lichess …)" (`plyLabel` aus `lichessFrom`) oder
  „Schätzung unvollständig" (`pending`). Linien ohne Quelle zeigen weiter die Auffüll-Anzeige. Texte
  `prep.trainingRepertoire.*` (en/de/hr/hu, in LeagueHub die deutschen aus der Komponente).
- Widerspruch (0.715.1): `source = deviates` → `.tl-deviates` „weicht ab: er spielt hier {{move}} ({{n}} Partie/n)"
  (`deviationLabel` setzt seinen Zug an `deviationPly` ein) und „≈ p %" (Schätzung ab dort).
- Anlegen (0.718.0): erst ohne `replace`; nur bei 409 `exists` die Rückfrage („Es gibt schon ein Repertoire … ersetzen?",
  Knöpfe „Ersetzen"/common.cancel — `ConfirmService.ask(text, params, { confirm, cancel })`), dann mit `replace: true`.
- Trainer mit `?opponent=` (0.718.0): `?line=` ist der EINSTIEG (`opponentStart`) — die Linie sofort, die Reihung im
  Hintergrund (`loadOpponentOrder(false)` → `extendOpponentQueue`), danach die Linien nach ihr; ohne Fälligkeit und ohne
  Modus-Wechsel (`opponentLines`); Ende `DONE` mit „Alle {{n}} Linien gegen diesen Gegner durch" (`opponentTotal`),
  „Von vorn" (`restart`, ganze Reihung) und „Zurück zur Spielerkarte" (`history.back()`). Ohne `?opponent=` unverändert.
- Unvollständige Schätzung (0.725.1): `.tl-incomplete` „Schätzung unvollständig — {{n}} Stellungen offen" (`explorerPending`)
  mit `.tl-continue` „Weiter rechnen" (dieselbe Abfrage); automatisch nach 3 s, höchstens 3 Runden
  (`AutoContinueMs`/`AutoContinueRounds`, Wecker außerhalb der Zone, damit `whenStable` nicht wartet; Wahl/Öffnen setzt
  die Runden zurück).
- Seit 0.725.2 ersetzt das Nachfragen die automatischen Runden: solange `explorerRunning`, alle 3 s dieselbe Abfrage
  (`PollMs`, ohne Rundenlimit), `.tl-running` „Schätzung läuft — noch N von M Stellungen" mit `progress.tl-progress`
  (M = `pendingStart`); `.tl-continue` nur noch als Rückfall bei `explorerIncomplete` ohne Lauf.
- Farbe aus Sicht des Gegners (0.733.1): der Umschalter `.tl-color` heißt „Gegner hat Weiß → ich spiele Schwarz" /
  „Gegner hat Schwarz → ich spiele Weiß" (i18n `heHasWhite`/`heHasBlack`, Gruppe `whoHasWhich`; „Gegner hat Weiß" zuerst),
  intern bleibt `color` = MEINE Farbe. Eingabe `opponentColor` ('w'|'s' = SEINE Farbe): `player-card` reicht `color()` nur
  durch, wenn ein Brett bekannt ist (`open(fide, color, board, …)` aus `fixture-view`/`round-lineups`; Vereinspartien
  geben eine Farbe ohne Brett = kein Zusammenhang); Vorgabe meine Farbe = das Gegenteil (`wanted()`), sonst wählt der
  Server. `lh-training-lines` merkt nur noch `{ repertoire }` — eine gemerkte Farbe des letzten Gegners wäre still falsch.
  Hat die Begegnungsfarbe keine markierten Linien (`colors` ohne sie), zeigt `.tl-other` „Für Schwarz hast du keine
  markierten Linien — gezeigt werden deine Linien mit Weiß". Knopf und Ersetzen-Rückfrage nennen die Farbe
  (`{{side}}` = `asWhite`/`asBlack`, „ich mit Schwarz"; Parameter NICHT `as` nennen — Schlüsselwort im Template).
