import { ParamMap } from '@angular/router';
import { BookPuzzleDto } from './puzzle.service';
import { CourseMode, CourseScopeStats, CourseLineEbookRef } from '../courses/course.service';
import { CourseEbookRefs } from './course-ebook-refs';
import { labelOr, offlineLanguageStale } from '../courses/course-language.util';
import {
  getBookOfflineByBookId, getBookOfflineLanguageByBookId, isBookCacheComplete, markBookCacheComplete, saveBookOffline,
  loadCourseLocalSolved, saveCourseLocalSolved, clearCourseLocalSolved,
} from './book-offline.util';
import { clearSolveElapsed } from './solve-elapsed.util';
import { formatEtaShort, estimateRemainingSeconds } from './book-nav.util';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { BookModeHost, BookModeStrategy } from './book-mode-host';

// Seitengröße fürs anonyme öffentliche Kurs-Laden: erste Seite → sofort spielbar, Rest im Hintergrund.
const ANON_COURSE_PAGE_SIZE = 300;

/**
 * Kursmodus des Buch-Lösers (/courses/:bookId/:mode[/:chapterIndex], Zerlegung Z18): arbeitet ein Buch puzzleweise
 * durch; Fortschritt liegt user-bezogen in der DB. Anonym (öffentlicher Kurs) wird rein clientseitig aus den
 * öffentlichen Puzzles bedient, Fortschritt lokal; offline aus dem gespeicherten Buch.
 */
export class BookCourseMode implements BookModeStrategy {
  bookId: number | null = null;
  modeKind: CourseMode = 'sequential';
  /** Im Kapitel-Modus gesetzt: 0-basierter Kapitel-Index → Pool + Fortschritt aufs Kapitel beschränkt. */
  chapterIndex: number | null = null;
  solved = 0;
  total = 0;
  completed = false;
  /** Fortschritt/Zeit/Trefferquote fürs ganze Buch bzw. das aktuelle Kapitel (vom Backend). */
  bookStats: CourseScopeStats | null = null;
  chapterStats: CourseScopeStats | null = null;
  chapterName: string | null = null;
  /** Offline gelöste/übersprungene Kurs-Puzzles dieser Sitzung — vermeidet Dauerschleife auf
   *  demselben Puzzle, solange der Server-Fortschritt nicht erreichbar ist. */
  offlineSolvedIds = new Set<number>();
  /** Die Offline-Kopie, aus der gerade serviert wird, hat eine andere Sprache als die gewählte. */
  offlineLangStale = false;
  /** Kurs, dessen anonyme Kopie gerade in der neuen Sprache nachgeladen wird (nur EINE Kette zugleich). */
  private anonRefreshBook: number | null = null;
  /** Verknüpfter Partner-Kurs (Buch↔Workbook) für den Schnellwechsel; null = keiner. */
  linkedBookId: number | null = null;
  linkedName: string | null = null;
  /** Kurslinie → E-Book-Diagramm (Wunsch 2026-10-15), je Linie der erste Verweis. */
  readonly ebookRefs = new CourseEbookRefs();
  /** pro Puzzle-Durchgang nur ein Kurs-Versuch melden (Zeit-Inflation vermeiden) */
  attemptRecorded = false;

  constructor(private readonly h: BookModeHost) {}

  /** Anonymer (nicht eingeloggter) Nutzer in einem öffentlichen Kurs: der Kurs wird rein
   *  clientseitig aus den öffentlichen Puzzles bedient, Fortschritt bleibt lokal im Browser
   *  (keine auth-pflichtigen Kurs-Endpoints aufrufen). */
  get isAnon(): boolean { return this.h.inCourse && !this.h.isLoggedIn; }

  /** localStorage-Schlüssel der gemerkten Kurs-Zwischenzeit des aktuellen Puzzles (nur Kursmodus). */
  get elapsedKey(): string | null {
    return this.h.inCourse && this.h.puzzle ? `course:${this.h.puzzle.id}` : null;
  }

  get percent(): number {
    return this.total > 0 ? Math.round(100 * Math.min(this.solved, this.total) / this.total) : 0;
  }

  /** Wirklich alle Kurs-Puzzles gelöst (nicht nur „Random-Pool leer"). Im Random-Modus signalisiert
   *  `completed` lediglich, dass der aktuelle Durchgang vorbei ist — gescheiterte/ungelöste
   *  Puzzles bleiben übrig, bis ein Reset den Pool wieder auffüllt. Erst wenn solved == total ist
   *  das Buch tatsächlich durch. */
  get fullyDone(): boolean { return this.total > 0 && this.solved >= this.total; }

  /** Im Random-Modus übrige (nicht gelöste) Puzzles, wenn der Pool des aktuellen Durchgangs leer ist. */
  get remaining(): number { return Math.max(0, this.total - this.solved); }

  /** Verweis der aktuellen Linie ins E-Book (Knopf „Im Buch“), sonst null. */
  get currentEbookRef(): CourseLineEbookRef | null {
    return this.h.inCourse ? this.ebookRefs.get(this.h.puzzle?.id) : null;
  }

  /** Kapitel-Statistik: `chapterName` ist der Original-Schlüssel — gehört er zum aktuellen Puzzle,
   *  zeigt die Anzeige dessen Übersetzung. */
  get chapterDisplay(): string | null {
    const name = this.chapterName;
    const p = this.h.puzzle;
    if (name && p && (p.chapter ?? null) === name) return labelOr(p.chapterLabel, name);
    return name;
  }

  /** Kurs-Params übernehmen (Erst-Load und Schnellwechsel Buch↔Workbook / Browser zurück/vor). */
  start(pm: ParamMap): void {
    const bid = Number(pm.get('bookId'));
    if (!bid) return;
    this.bookId = bid;
    this.modeKind = pm.get('mode') === 'random' ? 'random' : 'sequential';
    const ch = pm.get('chapterIndex');
    this.chapterIndex = ch != null ? Number(ch) : null;
    // Anonym: lokal gemerkten Fortschritt DIESES Buchs übernehmen (übersteht Reload; ersetzt den
    // Set-Inhalt, damit beim Wechsel auf ein anderes Kurs-Buch keine fremden Ids stehen bleiben).
    if (this.isAnon) this.offlineSolvedIds = new Set(loadCourseLocalSolved(bid));
    // Welche Sprachen der Kurs hat (für die Auswahl) — still, die Linien nennen sie ohnehin mit.
    this.h.courseLang.ensureLanguages(bid, () => this.h.courseService.getTranslations(bid));
    this.loadNext();
    this.autoCache();       // Kurs im Hintergrund offline vorhalten (ohne manuelles ☁)
    this.loadLink();        // verknüpften Partner-Kurs für den Schnellwechsel laden
    this.loadEbookRefs();   // wo die Linien im E-Book stehen („Im Buch“)
  }

  loadEbookRefs(): void {
    const ok = this.bookId != null && !this.isAnon;
    this.ebookRefs.load(ok ? this.h.courseService.getEbookRefs(this.bookId!) : null);
  }

  /** Lädt den verknüpften Partner-Kurs (falls vorhanden) für den Schnellwechsel-Knopf. */
  loadLink(): void {
    this.linkedBookId = null;
    this.linkedName = null;
    if (this.bookId == null || this.isAnon) return;   // Verknüpfung ist auth-only
    this.h.courseService.getLink(this.bookId).subscribe({
      next: l => { this.linkedBookId = l.linkedBookId; this.linkedName = l.linkedDisplayName; },
      error: () => {}
    });
  }

  /** Wechselt zum verknüpften Kurs (Buch↔Workbook) im selben Modus; die Param-Subscription lädt neu. */
  switchToLinked(): void {
    if (this.linkedBookId == null) return;
    this.h.router.navigate(['/courses', this.linkedBookId, this.modeKind]);
  }

  /** Holt das nächste Puzzle des Kurses (sequential: after=, random: exclude=). */
  loadNext(after?: number, exclude?: number): void {
    const h = this.h;
    if (this.bookId == null) return;
    const epoch = ++h.loadEpoch;
    const hadPuzzle = h.puzzle != null;
    h.loadError = false;
    h.retryFn = () => this.loadNext(after, exclude);
    if (!hadPuzzle) h.state = 'LOADING';

    // Anonym (öffentlicher Kurs): rein clientseitig aus den öffentlichen Puzzles bedienen.
    if (this.isAnon) { this.loadAnonNext(after, exclude, hadPuzzle); return; }

    // Offline → direkt aus dem lokal gespeicherten Buch bedienen (kein Netz-Roundtrip).
    // Kein Cache vorhanden → Fehlerzustand (mit „Erneut versuchen") statt endlosem Spinner.
    if (!navigator.onLine) {
      if (!this.loadOffline(after, exclude, hadPuzzle)) {
        h.state = 'LOADING'; h.loadError = true;
        h.snackbar.info(h.translate.instant('book.offlineUnavailable'), { action: 'common.ok', duration: 3500 });
      }
      return;
    }

    const lang = h.requestLang;
    h.courseService.getNext(this.bookId, this.modeKind, after, exclude, this.chapterIndex ?? undefined, lang).subscribe({
      next: res => {
        if (epoch !== h.loadEpoch) return;
        this.offlineLangStale = false;
        this.solved = res.solvedCount;
        this.total = res.total;
        this.applyStats(res.book, res.chapter, res.chapterName);
        if (res.completed || !res.puzzle) {
          this.completed = true;
          // Letztes Puzzle gerade gelöst: gelöstes Brett stehen lassen; sonst leeres Done-Panel.
          if (!hadPuzzle) { h.puzzle = null; h.state = 'COURSE_DONE'; }
          return;
        }
        this.completed = false;
        h.gaveUp = false;
        h.puzzle = res.puzzle;
        h.afterLineLoaded(res.puzzle, lang);
        h.setupPuzzle(res.puzzle);
      },
      error: () => {
        if (epoch !== h.loadEpoch) return;
        // Netzfehler trotz onLine → letzter Versuch aus dem Offline-Cache.
        if (this.loadOffline(after, exclude, hadPuzzle)) return;
        h.state = 'LOADING'; h.loadError = true;
      }
    });
  }

  /**
   * Anonymer öffentlicher Kurs: die öffentlichen Puzzles einmal laden + lokal vorhalten, dann rein
   * clientseitig (wie offline) bedienen. Fortschritt lebt in {@link offlineSolvedIds} und wird
   * pro Buch im localStorage persistiert. Nicht öffentlich / kein Cache → „nicht verfügbar".
   */
  private loadAnonNext(after: number | undefined, exclude: number | undefined, hadPuzzle: boolean): void {
    if (this.bookId == null) return;
    const cached = getBookOfflineByBookId(this.bookId);
    if (cached?.length) {
      if (!this.loadOffline(after, exclude, hadPuzzle)) { this.showUnavailable(); return; }
      const online = typeof navigator === 'undefined' || navigator.onLine;
      const meta = getBookOfflineLanguageByBookId(this.bookId);
      // Die lokale Kopie ist hier der ARBEITSBESTAND: weicht ihre Sprache von der Wahl ab, wird sie
      // online im Hintergrund in der neuen Sprache ersetzt (die alte bleibt spielbar, bis die neue da ist).
      if (online && this.offlineLangStale) { this.refreshAnonCache(this.bookId); return; }
      // TORSO nachladen: brach die Seiten-Kette beim ersten Besuch ab (Netzfehler auf einer
      // Folgeseite, Tab geschlossen), lag hier nur die erste Seite — und der Kurs galt mit 300 von
      // 3000 Linien als „abgeschlossen". Der Marker unterscheidet beides; ist er nicht gesetzt,
      // wird die Kette im Hintergrund ab der gecachten Länge fortgesetzt (spielbar bleibt es sofort) —
      // in der Sprache der Kopie, sonst stünden zwei Sprachen in einem Bestand.
      if (!isBookCacheComplete(this.bookId) && online)
        this.fetchAnonPages(this.bookId, [...cached], cached.length, undefined, undefined, false,
          false, meta?.lang ?? this.h.requestLang);
      return;
    }
    // Kein lokaler Cache + offline → nicht ins Netz laufen, sondern „nicht verfügbar" zeigen.
    if (typeof navigator !== 'undefined' && !navigator.onLine) { this.showUnavailable(); return; }
    // Seitenweise laden: erste (kleine) Seite → sofort spielbar; restliche Seiten im Hintergrund in
    // den Offline-Cache nachladen. Große öffentliche Kurse (Tausende Puzzles) starten so in ~1 s
    // statt erst nach dem kompletten (mehrere MB großen) Download.
    this.fetchAnonPages(this.bookId, [], 0, after, exclude, hadPuzzle, true);
  }

  /**
   * Seiten-Kette des anonymen Kurses (auch zum FORTSETZEN eines Torsos aufrufbar: `acc` mit dem
   * gecachten Stand vorbelegen und `skip` auf dessen Länge setzen).
   *
   * Cache-Schreiben ist teuer (JSON.stringify über den GANZEN bisherigen Stand): nach JEDER Seite
   * geschrieben wüchse der Aufwand quadratisch (300+600+…+3000 statt 3000 serialisierte Puzzles —
   * mehrere MB Main-Thread-Blockade auf Mobilgeräten). Deshalb wird nur EINMAL nach der ersten
   * Seite geschrieben (sofort spielbar) und dann erst am ENDE der Kette (letzte Seite bzw. Fehler
   * einer Folgeseite, damit das Geladene einen Reload übersteht).
   */
  private fetchAnonPages(bookId: number, acc: BookPuzzleDto[], skip: number,
    after: number | undefined, exclude: number | undefined, hadPuzzle: boolean,
    serveFirstPage = false, lang: string | null = this.h.requestLang, replace = false): void {
    const flushCache = (): boolean => {
      const fileName = acc[0]?.bookFileName;
      return fileName ? saveBookOffline(fileName, acc, bookId, lang) : false;
    };
    const fetchPage = (pageSkip: number, first: boolean): void => {
      this.h.courseService.getPublicCourse(bookId, pageSkip, ANON_COURSE_PAGE_SIZE, lang).subscribe({
        next: page => {
          if (this.bookId !== bookId) return;   // zwischenzeitlich weitergewechselt
          if (page?.length) acc.push(...page);
          if (page?.length) this.h.courseLang.noteLines({ bookId }, page);
          // Volle Seite → es gibt vermutlich mehr; sonst ist die Kette hier zu Ende.
          const hasMore = !!page && page.length === ANON_COURSE_PAGE_SIZE;
          // ERSETZT die Kette eine vollständige Kopie (Sprachwechsel), wird erst am Ende geschrieben —
          // eine halb neue Kopie mit dem alten „vollständig"-Marker meldete sonst nach 300 Linien „fertig".
          if ((first && !replace) || !hasMore) {
            const written = flushCache();
            // Vollständig NUR melden, wenn der letzte Stand auch wirklich im Speicher liegt — und ein
            // Zwischenstand (es kommt noch mehr) nimmt einen alten Marker zurück: blieb der nach dem
            // Räumen des Caches stehen, galt ein Abbruch nach Seite 1 sonst als ganzer Kurs (F1-009).
            markBookCacheComplete(bookId, !hasMore && written);
            if (!hasMore && written) this.offlineLangStale = false;
            if (!hasMore && replace) this.anonRefreshBook = null;
          }
          if (first && serveFirstPage) {
            // Cache-Schreiben kann fehlschlagen (Quota/Privatmodus) — der anonyme Kurs wird NUR
            // aus dem Cache bedient: dann ehrlich „nicht verfügbar" statt endlosem Spinner.
            if (!acc.length || !this.loadOffline(after, exclude, hadPuzzle)) {
              this.showUnavailable();
              return;
            }
          }
          if (hasMore) fetchPage(pageSkip + ANON_COURSE_PAGE_SIZE, false);
        },
        error: () => {
          if (this.bookId !== bookId) return;
          // Nur wenn schon die erste Seite scheitert (nichts geladen) → „nicht verfügbar".
          // Ein Fehler bei einer Folgeseite lässt das bereits Geladene spielbar — dazu den
          // aufgelaufenen Stand festschreiben (die Zwischenseiten werden nicht mehr geschrieben);
          // der Marker bleibt AUS, damit der nächste Besuch die Kette fortsetzt.
          if (first && serveFirstPage && !acc.length) { this.showUnavailable(); return; }
          // Beim Ersetzen bleibt die alte (vollständige) Kopie liegen, statt einen Torso zu schreiben.
          if (!replace) flushCache();
          else this.anonRefreshBook = null;
        },
      });
    };
    fetchPage(skip, skip === 0);
  }

  /** Anonymer Kurs: die lokale Kopie in der gewählten Sprache neu holen (nur online, im Hintergrund). */
  refreshAnonCache(bookId: number): void {
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    if (this.anonRefreshBook === bookId) return;   // läuft schon — jede weitere Linie würde sonst eine Kette starten
    this.anonRefreshBook = bookId;
    this.fetchAnonPages(bookId, [], 0, undefined, undefined, true, false, this.h.requestLang, true);
  }

  /** Öffentlicher Kurs anonym nicht (mehr) verfügbar (nicht public / gelöscht). */
  private showUnavailable(): void {
    this.h.state = 'LOADING';
    this.h.loadError = true;
    this.h.snackbar.info(this.h.translate.instant('book.course.unavailable'), { action: 'common.ok', duration: 4000 });
  }

  /**
   * Offline-Fallback fürs Kurs-Laden: serviert Puzzles aus dem lokal gespeicherten Buch
   * (über die bookId aufgelöst). Versuche werden via Offline-Queue nachgemeldet; der
   * Server-Fortschritt („nächstes ungelöstes") lässt sich offline nicht exakt nachbilden,
   * darum meiden wir nur die in DIESER Sitzung schon gelösten Puzzles. Liefert true, wenn
   * ein Puzzle (oder das „fertig"-Panel) gesetzt wurde, sonst false (kein Cache vorhanden).
   */
  private loadOffline(after: number | undefined, exclude: number | undefined, hadPuzzle: boolean): boolean {
    const h = this.h;
    if (this.bookId == null) return false;
    const book = getBookOfflineByBookId(this.bookId);
    if (!book || !book.length) return false;
    // Weicht die Sprache der Kopie von der Wahl ab, sagt die Ansicht es („neu herunterladen").
    this.offlineLangStale = offlineLanguageStale(getBookOfflineLanguageByBookId(this.bookId),
      h.requestLang ?? '', h.courseLang.languages(h.langRef));

    // Fortschritt/Zähler nur über echte Quiz-Linien (Info-/Erklärlinien zählen nicht) — wie serverseitig.
    const quiz = book.filter(p => !p.isInfoOnly);
    this.total = quiz.length;
    this.solved = quiz.filter(p => this.offlineSolvedIds.has(p.id)).length;
    const cur = after ?? exclude;
    let next: BookPuzzleDto | undefined;

    if (this.modeKind === 'random') {
      // Zufallstopf = QUIZ-Linien, wie serverseitig (CourseService: `quizScope`). Zog er aus dem
      // ungefilterten Buch, bekam der Spieler reine Info-/Erklärseiten als „Zufallsaufgabe": die
      // zählen nie als gelöst, der Fortschritt stand still und derselbe Block konnte beliebig oft
      // kommen — der Kurs erreichte 100 % nie. Betraf offline UND jeden anonymen Aufruf.
      const fresh = quiz.filter(p => p.id !== cur && !this.offlineSolvedIds.has(p.id));
      const pick = fresh.length ? fresh : quiz.filter(p => p.id !== cur);
      const pool = pick.length ? pick : quiz;
      next = pool.length ? pool[Math.floor(Math.random() * pool.length)] : undefined;
    } else {
      // sequenziell: ab Position nach `after` das erste in dieser Sitzung noch nicht gelöste.
      const start = after != null ? book.findIndex(p => p.id === after) : -1;
      for (let j = 1; j <= book.length; j++) {
        const cand = book[(Math.max(start, -1) + j + book.length) % book.length];
        if (!this.offlineSolvedIds.has(cand.id)) { next = cand; break; }
      }
    }

    if (!next) {
      // Alle (lokal) gelöst → fertig-Panel.
      this.completed = true;
      if (!hadPuzzle) { h.puzzle = null; h.state = 'COURSE_DONE'; }
      return true;
    }
    this.completed = false;
    h.gaveUp = false;
    h.loadError = false;
    h.puzzle = next;
    h.setupPuzzle(next);
    return true;
  }

  /**
   * Lädt beim Öffnen eines Kurses (online) im Hintergrund alle Puzzles des Buchs und legt sie
   * offline ab — damit der Kurs offline weiterläuft, ohne dass man vorher manuell auf ☁ tippen
   * muss. Fire-and-forget, nur online, idempotent (überspringt, wenn schon gecacht).
   */
  autoCache(): void {
    if (this.bookId == null || this.isAnon) return;   // anonym cacht loadAnonNext über den public-Endpoint
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    const bookId = this.bookId;
    const lang = this.h.requestLang;
    // Schon offline vorhanden — außer in einer anderen Sprache als der gewählten: dann neu holen.
    const meta = getBookOfflineLanguageByBookId(bookId);
    if (meta && !offlineLanguageStale(meta, lang ?? '', this.h.courseLang.languages(this.h.langRef))) return;
    this.h.courseService.getBookPuzzles(bookId, lang).subscribe({
      next: puzzles => {
        const fileName = puzzles?.[0]?.bookFileName;
        if (fileName && puzzles.length) saveBookOffline(fileName, puzzles, bookId, lang);
      },
      error: () => { /* offline/Fehler: ignorieren, ☁ bleibt als manueller Weg */ },
    });
  }

  /** „Nächstes Puzzle" / „Überspringen": eins weiter im jeweiligen Modus. */
  next(): void {
    const p = this.h.puzzle;
    const cur = p?.id;
    // Sequenziell durchgeklickte Info-/Erklärlinie merken → beim nächsten Wiedereinstieg wird sie
    // übersprungen (der Kurs setzt dahinter fort statt sie erneut zu zeigen).
    if (cur != null && p?.isInfoOnly && this.h.inCourse && this.bookId != null) {
      this.offlineSolvedIds.add(cur);   // (offline/anonym) dieselbe Info-Linie nicht erneut zeigen
      if (this.isAnon) {
        saveCourseLocalSolved(this.bookId, this.offlineSolvedIds);   // anonym: lokal persistieren
      } else if (typeof navigator === 'undefined' || navigator.onLine) {
        this.h.courseService.markInfoSeen(this.bookId, cur).subscribe({ next: () => {}, error: () => {} });
      }
    }
    if (this.modeKind === 'random') this.loadNext(undefined, cur);
    else this.loadNext(cur, undefined);
  }

  /** Rückweg aus dem Kurs: angemeldet zur Kursübersicht. Gäste spielen nur öffentliche Kurse (Kurz-URL /{slug}) — für
   *  sie hat /courses keine Seite, nur die Anmeldemaske (UX-024); sie gehen zur Startseite. */
  backToCourses(): void {
    this.h.router.navigate([this.h.isLoggedIn ? '/courses' : '/']);
  }

  /** Beschriftung des Rückpfeils und der „Zur Übersicht“-Knöpfe im Kurs — für Gäste „Zur Startseite“ (s. o.). */
  backKey(overview: 'book.course.backToCourses' | 'book.course.toOverview'): string {
    return this.h.isLoggedIn ? overview : 'book.course.toStart';
  }

  /** „Mit den restlichen weiter": gescheiterte, ungelöste Linien wieder in den Topf, OHNE die gelösten
   *  zurückzusetzen. Anonym gibt es keine serverseitige Sperre → einfach weiterladen. */
  retryFailed(): void {
    if (this.bookId == null) return;
    if (this.isAnon) { this.completed = false; this.loadNext(); return; }
    this.h.courseService.retryFailed(this.bookId).subscribe({
      next: () => { this.completed = false; this.loadNext(); },
      error: () => this.h.snackbar.warn(this.h.translate.instant('book.course.restartFailed'), { duration: 3000 }),
    });
  }

  /** Kurs zurücksetzen und von vorn beginnen — bringt im Random-Modus auch die falsch gelösten
   * Puzzles wieder in den Pool. Direkt vom „abgeschlossen"-Panel aus erreichbar. */
  restart(): void {
    if (this.bookId == null) return;
    // Anonym: nur den lokalen Fortschritt verwerfen, kein Server-Reset.
    if (this.isAnon) {
      this.offlineSolvedIds.clear();
      clearCourseLocalSolved(this.bookId);
      this.completed = false;
      this.h.snackbar.success(this.h.translate.instant('book.course.restarted'), { duration: 2500 });
      this.loadNext();
      return;
    }
    this.h.courseService.reset(this.bookId).subscribe({
      next: () => {
        this.completed = false;
        this.h.snackbar.success(this.h.translate.instant('book.course.restarted'), { duration: 2500 });
        this.loadNext();
      },
      error: () => this.h.snackbar.warn(this.h.translate.instant('book.course.restartFailed'), { duration: 3000 }),
    });
  }

  /** Meldet jeden Kurs-Versuch (gelöst/fehlgeschlagen) ans Backend — Grundlage für die akkumulierte
   *  Kurs-/Studienzeit im Trainingsziele-Tracker. Pro Puzzle-Durchgang nur einmal (Zeit-Inflation
   *  durch Online+Offline-Retry vermeiden); Fortschritt wird nur bei `solved` hochgezählt. */
  recordAttempt(solved: boolean): void {
    const h = this.h;
    const puzzle = h.puzzle;
    if (!h.inCourse || this.attemptRecorded || this.bookId == null || !puzzle) return;
    this.attemptRecorded = true;
    // Versuch ist entschieden → gemerkte Zwischenzeit verfällt (ein Retry startet wieder bei 0).
    clearSolveElapsed(`course:${puzzle.id}`);

    // Anonym: Fortschritt nur lokal (localStorage). Kein Server-Call/Queue — Kurs-Endpoints sind
    // auth-pflichtig, und anonyme Versuche zählen bewusst nicht in Bestenlisten/Trainingsziele.
    if (this.isAnon) {
      if (solved && !this.offlineSolvedIds.has(puzzle.id)) {
        this.offlineSolvedIds.add(puzzle.id);
        saveCourseLocalSolved(this.bookId, this.offlineSolvedIds);
        this.solved = Math.min(this.solved + 1, this.total || this.solved + 1);
      }
      return;
    }

    const url = `/api/courses/${this.bookId}/results`;
    // `mode` ist hier seit jeher die Durchlaufart (sequential/random); die Spielweise heißt
    // deshalb `solveMode`. Offline-Body identisch zum Online-Body (sonst geht sie beim
    // Nachsenden verloren).
    const solveMode = h.attemptSolveMode;
    const body = { bookPuzzleId: puzzle.id, solved, mode: this.modeKind, timeSeconds: h.solveSeconds, chapterIndex: this.chapterIndex ?? undefined, hintsUsed: h.hintsUsed, solveMode };
    if (!navigator.onLine) {
      // Offline → Server-Aufzeichnung vormerken; bei Solve zusätzlich lokalen Fortschritt hochzählen.
      h.offlineQueue.enqueue('POST', url, body);
      if (solved) {
        this.offlineSolvedIds.add(puzzle.id);   // beim nächsten Laden überspringen
        this.solved = Math.min(this.solved + 1, this.total || this.solved + 1);
      }
      return;
    }
    h.courseService.recordResult(this.bookId, puzzle.id, solved, this.modeKind, h.solveSeconds, this.chapterIndex ?? undefined, h.hintsUsed, solveMode).subscribe({
      next: p => { this.solved = p.solvedCount; this.total = p.total; this.applyStats(p.book, p.chapter, p.chapterName); },
      error: () => h.offlineQueue.enqueue('POST', url, body),
    });
  }

  /** Übernimmt die vom Backend gelieferte Buch-/Kapitel-Statistik (Fortschritt/Zeit/Trefferquote). */
  applyStats(book?: CourseScopeStats | null, chapter?: CourseScopeStats | null, chapterName?: string | null): void {
    this.bookStats = book ?? null;
    this.chapterStats = chapter ?? null;
    this.chapterName = chapterName ?? null;
  }
}

/** mm:ss bzw. h:mm:ss aus Sekunden (für die Kurs-Zeitanzeige). */
export function courseTime(seconds: number): string { return formatSecondsClock(seconds); }

/** Geschätzte Restzeit für das ganze Buch/Kapitel bei bisherigem Tempo (z. B. „~1 h 20 min").
 *  `null`, solange nichts abschätzbar ist (nichts versucht bzw. alles gelöst). */
export function courseEta(stats: CourseScopeStats): string | null {
  const secs = estimateRemainingSeconds(stats);
  return secs == null ? null : formatEtaShort(secs);
}
