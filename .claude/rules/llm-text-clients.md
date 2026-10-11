---
paths:
  - "**/Services/*JsonClient*.cs"
  - "**/Services/*Translation*.cs"
  - "**/Services/Hint*.cs"
  - "**/Services/*VisionClient*.cs"
  - "**/Services/OpenAiChat*.cs"
  - "**/tools/LibraryImport/**"
---

# LLM-Aufrufe: Schlüssel, eigene Hardware, Tipp-Queue

> Lädt automatisch, sobald eine passende Datei gelesen/bearbeitet wird. Ausgelagert aus `CLAUDE.md` „Wichtige Konventionen“ (2026-10-11).

- **Zwei Anthropic-Schluessel, und der Konto-Schluessel gehoert allein dem Formular-Einlesen** (seit 2026-09-25,
  Nutzer: „ausser Scoresheet soll nichts ueber den Key laufen"). `Anthropic:ApiKey` (Compose `ANTHROPIC_API_KEY`) liest
  NUR `ClaudeScoresheetVisionClient` (Foto → Partie, Kostenbremse `ScoresheetBudget`). Puzzle-Tipps (`HintGenerationService`)
  und Kommentar-Uebersetzung (`CommentTranslationService`, `CourseTranslationService`, `tools/LibraryImport translate`) gehen ueber den
  `ClaudeJsonClient`, und der liest AUSSCHLIESSLICH `Anthropic:TextApiKey` (`ANTHROPIC_TEXT_API_KEY`) — ungesetzt sind
  beide aus, auch wenn der Konto-Schluessel da ist. Wer einen neuen Claude-Aufruf einbaut, entscheidet sich fuer einen
  der beiden und schreibt es hier dazu; `ClaudeJsonClientTests` haelt fest, dass der Konto-Schluessel den Text-Client NICHT
  einschaltet. Der Discord-Bot (`CLAUDE_API_KEY` im Bot-Stack) und der log-watcher (eigener Schluessel) haengen nicht an
  RookHub — der Bot-Chat wurde am 2026-09-25 durch Entfernen des Schluessels abgeschaltet.
  **Seit 0.533.3 gibt es fuer Tipps und Uebersetzung einen dritten Weg: eigene Hardware.** `TextLlm:BaseUrl`
  (Compose `TEXT_LLM_BASE_URL`, OpenAI-kompatibel bis `/v1`, z. B. vLLM auf dem DGX Spark) + `TextLlm:ApiKey`
  (`TEXT_LLM_API_KEY`) + `TextLlm:Model` (`TEXT_LLM_MODEL`, leer = erstes Modell unter `/models` — auf dem Spark
  wechselt das Modell) → `OpenAiJsonClient`, kostenlos je Aufruf. Die EINE Auswahlregel ist `TextJsonClients.Create`
  (API und `tools/LibraryImport`): eigene Hardware VOR `Anthropic:TextApiKey`, ohne beides aus. Nachdenken ist dort aus
  (`chat_template_kwargs.enable_thinking=false` UND `reasoning_effort: low`, `TextLlm:Thinking=true` schaltet beides
  ab) — Qwen3.5 hört auf den Vorlagen-Schalter und braucht auf dem Spark sonst Minuten je Tipp; gpt-oss ignoriert ihn
  und hört auf `reasoning_effort` (0.597.2: mit seiner Vorgabe 73 s/696 Tokens je Drei-Satz-Absatz, mit `low`
  12,7 s/116 Tokens bei gleicher Übersetzung; der jeweils fremde Schalter wird vom Server ignoriert) —, gestreamt wegen
  des 90-s-Proxys vor dem Spark (`OpenAiChat.SendAsync`), dasselbe
  JSON-Schema wie der Claude-Weg (vLLM erzwingt es per Grammatik; lehnt ein Server es ab, einmal ohne und dann dabei
  bleiben). **Höchstens `TextLlm:MaxConcurrent` Anfragen gleichzeitig** (Vorgabe 8, 1..64; Codereview A6-005):
  ein Semaphor im `OpenAiJsonClient` (Singleton) für ALLE Zwecke zusammen — Erklärungen, Nacherzählung, Roast, Tipps,
  Übersetzung, Zugvergleich; Wartende reihen sich ein. Gilt auch für `tools/LibraryImport` (`--parallel` über 8 nur
  mit `TextLlm__MaxConcurrent`). Jede Antwort verliert vor dem Zerlegen die unsichtbare Typografie von gpt-oss
  (`OpenAiJsonClient.PlainTypography`, 0.597.3: U+2010/2011/2012/2212 → „-", U+00AD weg, U+00A0/2009/202F → Leerzeichen;
  Gedankenstriche bleiben) — keine Quelle enthält sie, Suche und Kopieren stolpern darüber. Den Bestand (alle
  Maschinentexte in CommentTexts, GameRecaps, GameRoasts, GameMoveExplanations) hat die SQL-Migration
  `NormalizeLlmTypography` einmal bereinigt (von Hand geschrieben, Designer = Kopie des vorigen, Modell unverändert).
  **Ausfälle der Spark (0.624.1):** (1) *Verstummter Strom* — der HttpClient-Timeout deckt bei `ResponseHeadersRead` nur
  die Kopfzeilen; `OpenAiChat.ReadStreamAsync` gibt deshalb auf, wenn `TextLlm:StreamIdleSeconds` (Vorgabe 120, über der
  90-s-Kappung des Proxys) lang KEINE Zeile kommt (01.10.2026: sieben Stunden Stillstand ohne Fehlerzeile). (2) *Modell
  weg* — `OpenAiChat.IsTransportFailure` (keine Antwort, Strom abgerissen/verstummt, 502/503/504) zählt der
  `OpenAiJsonClient`; ab drei in Folge ist `IClaudeJsonClient.IsUnreachable` wahr, 60 s nach dem letzten Fehler verfällt
  es wieder (wer anhält, fragt nicht mehr — sonst käme es nie zurück), jede Antwort des Servers setzt es zurück. Ein
  500 oder ein unbrauchbarer Text ist KEIN Ausfall. Serien-Arbeit hält dann an, statt Arbeit als gescheitert zu
  verbuchen: Kurs-Auftrag siehe „Hintergrunddienst", `tools/LibraryImport translate` legt die Partie zurück in die
  Reihe und wartet 75 s.
- **Kurs-Übersetzung: zwei Einstellungen, Automatik nur auf Prod** (0.548.0) – `CourseTranslation:Parallel` (Vorgabe 4,
  Linien je Lauf gleichzeitig) und `CourseTranslation:AutoLanguages` (Compose `COURSE_TRANSLATION_AUTO_LANGUAGES`,
  Komma-Liste, nur die 25 Oberflächensprachen, Vorgabe LEER = aus). Prod bekommt `de,en` erst auf Zuruf in der `.env`;
  Dev bleibt aus — beide teilen sich die Spark, und denselben Bestand zweimal zu übersetzen kostete Wochen Nachtarbeit.
  Die Sperrzeiten (`TextLlm:QuietHours`) gelten für JEDEN Auftrag, auch angeforderte; das Werkzeug
  (`tools/LibraryImport translate --course`) kennt sie nicht, dort hält die Schaltuhr sie ein.
- **Puzzle-Tipps laufen auf der EIGENEN Tipp-Queue** (`HintTaskQueue` + `HintTaskWorker`, Codereview 2026-09-29, A4-002) –
  nie auf der allgemeinen `IBackgroundTaskQueue`: dort hielt ein grosser persoenlicher Kurs (je Linie Stockfish bis 30 s +
  drei LLM-Aufrufe) den einzigen Consumer stundenlang fest. Der Import reiht je Buch EINEN Lauf ein
  (`TryEnqueueBook(bookId, owner)`, hoechstens einer wartend, volle Queue verwirft statt zu warten), der Lauf
  (`HintGenerationService.GenerateForBookAsync`) holt die Linien ohne aktuelle Tipps selbst (ohne Info-Linien).
  Persoenliche Kurse: 100 Puzzles je Lauf, 300 je Besitzer und UTC-Tag (Arbeitsspeicher); Admin-/Pool-Buecher ungedeckelt.
  Ein NEUES Buch traegt seinen Besitzer erst nach dem Import — Aufrufer eines persoenlichen Imports reichen deshalb
  `ownerUserId` an `ImportFileAsync`, sonst gaelte der Kurs als Admin-Buch.
