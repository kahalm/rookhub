# Kurs-Kommentare mehrsprachig (Stufe C, 0.549.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Oberfläche zur Kurs-Übersetzung (Server: Haupt-CLAUDE.md „Anmerkungen in mehreren Sprachen" → KURSE). Drei Bausteine
in `features/courses/`:

- **`course-language.util.ts`** — die REINEN Regeln, mit literalen Vektoren getestet. Sprachliste eines Kurses = die
  QUELLE zuerst („Original"), dann die Übersetzungen alphabetisch (dieselbe Form wie `commentLanguages` je Linie).
  **Wirksame Sprache** = die gewünschte, wenn der Kurs sie hat, sonst die Quelle (`effectiveLanguage`). „Original" wird
  als KÜRZEL der Quelle gemerkt, nicht als eigenes Wort — `?lang=<Quelle>` liefert das Original und trotzdem
  `commentLanguages`. `labelOr(label, original)` ist die EINE Anzeige-Regel für Kapitel und Titel.
- **`CourseLanguageService`** (root) — die Wahl je Kurs im localStorage `rookhub_course_lang` (`{ c: { b<id>|f<datei>:
  lang }, f: { datei: id } }`, beim Abmelden geräumt), ohne Wahl gilt die Oberflächensprache (`requestLang` → das geht als
  `?lang=` raus). Bekannte Sprachen je Kurs liegen in einem SIGNAL (Übersicht + `commentLanguages` gesehener Linien),
  die Übersicht holt `ensureLanguages` je Kurs und Sitzung höchstens einmal, still. **Die Einzel-Linie im Buch
  (`/puzzles/book/:id`) kennt keine Kurs-Id** — dort hängt die Wahl am Dateinamen, `rememberFile` verknüpft beide.
- **`CourseLangPickerComponent`** (Menü-Knopf, unsichtbar bei EINER Sprache; Namen aus `LocaleService.languages`),
  **`MachineNoteComponent`** („maschinell übersetzt", Klick = Original) und **`CourseTranslationsComponent`** (Kasten
  auf der Kursseite: Sprachen mit Fortschritt, offene Aufträge „wartet · Platz n" / „läuft · n %" / „pausiert bis …"
  über `formatQuietUntil`, „Übersetzen in …" über die 25 Sprachen ohne Quelle, vollständige und offene; Limit-Hinweis
  mit Link auf den Kurs, der läuft; eigenen wartenden Auftrag zurückziehen, Admin jeden offenen; ohne Anmeldung
  lesend; 404 → ausgeblendet). Alles aus HTTP in Signalen (OnPush). Er fragt alle `PollMs` (15 s) nach, SOLANGE ein
  Auftrag wartet oder läuft.
- **`CourseTranslateMenuComponent`** (0.564.0, Wunsch 2026-09-27) — „Übersetzung anfordern" mit Sprach-Untermenü im
  ⋮-Menü des Kurs-Lösers (`PuzzleActionBarComponent.translateBookId`, nur angemeldet im Kursmodus). Holt die Übersicht
  erst beim Aufklappen, zeigt offene Aufträge als gesperrte Zeilen und dieselben Regeln wie der Kasten — die Helfer
  (`requestableLanguages`, `translationJobStatusText`, `translationReasonText`, `isOpenJob`) stehen EINMAL in
  `course-translations.component.ts`. **Restdauer** (`jobEtaMinutes`, ebenda): aus dem Tempo DIESES Laufs — der Server
  setzt `startedAt` bei jedem Anlauf neu und die Zähler auf 0 —, erst ab `EtaMinLines` (10, der Server schreibt den
  Stand nur alle zehn Linien) und `EtaMinElapsedMs` (1 min); eher zu hoch, weil gegen Ende viele Linien nur schon
  übersetzte Texte übernehmen. Schreibweise über `shared/eta.util.ts`.

Regeln, die dabei nicht kippen dürfen:
- **`chapter` und `title` bleiben SCHLÜSSEL** (`?chapter=`, `groupByChapter`, Umbenennen, Kapitel-PGN,
  `course-detail.key()`, Kalkulations-Gruppen) — nur die ANZEIGE nimmt `chapterLabel ?? chapter` bzw. `label ?? name`.
  `CourseNextPuzzleDto.chapterName` ist ebenfalls das Original; der Solver zeigt dafür `puzzle.chapterLabel`, wenn das
  Kapitel zum aktuellen Puzzle gehört (`courseChapterDisplay`).
- **Ein Sprachwechsel tauscht im Solver und in der Kalkulation nur die TEXTE** (`applyTexts` bzw. `refreshTexts`) —
  ein laufender Versuch, Baum, Uhr und Bewertung bleiben. Wer dort `setupPuzzle`/`applyPosition` ruft, startet die
  Aufgabe neu.
- **Tagespuzzle und Wochenpost bleiben Original** (`langEnabled` im Solver); ebenso Favoriten, Verlauf, Bearbeiten und
  Aufgabenblätter.
- **Offline-Kopie merkt sich ihre Sprache** (`rookhub_book_lang_<Datei>`, NICHT unter dem Buch-Präfix — sonst zählte
  sie als gecachtes Buch). Verglichen wird „übersetzt in X" gegen „Original" (`offlineLanguageStale`); eine alte Kopie
  ohne Vermerk gilt als Original. Die anonyme Arbeitskopie wird bei abweichender Sprache im Hintergrund ERSETZT und erst
  am Ende der Seiten-Kette geschrieben.
