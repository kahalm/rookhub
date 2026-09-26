import { Injectable, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { localStore, readJson, writeJson } from '../../core/local-json-store';
import {
  LanguageOverview, effectiveLanguage, languagesFromLines, languagesFromOverview, mergeLanguages, normLang,
} from './course-language.util';
import { CourseService } from './course.service';

/** Immer DIESELBE leere Liste: ein neues `[]` je Aufruf wäre in einer Vorlagen-Bindung bei jedem Durchlauf ein
 *  „geänderter" Wert (NG0100 im Dev-Modus, OnPush-Kinder zeichneten ständig neu). */
const NO_LANGUAGES: string[] = [];

/** localStorage: gemerkte Sprachwahl je Kurs + welcher Dateiname zu welcher Kurs-Id gehört. */
export const COURSE_LANG_KEY = 'rookhub_course_lang';

/**
 * Wie eine Seite „ihren" Kurs kennt: die Kurs-Seiten über die `bookId`, das einzelne Buch-Puzzle
 * (`/puzzles/book/:id`) nur über den Dateinamen der Linie — `BookPuzzleDto` trägt keine Kurs-Id.
 */
export interface CourseRef {
  bookId?: number | null;
  fileName?: string | null;
}

interface Stored {
  /** Wahl je Kurs-Schlüssel (`b<bookId>` bzw. `f<Dateiname>`) → Sprachkürzel. */
  c: Record<string, string>;
  /** Dateiname → bookId (damit das Einzel-Puzzle die Wahl des Kurses findet). */
  f: Record<string, number>;
}

/**
 * Sprachwahl der Kurs-Kommentare JE KURS, gemerkt je Gerät (Stufe C der Kurs-Übersetzung, 0.549.0).
 *
 * <p>Eine Wahl ist ein Sprachkürzel; „Original" ist das Kürzel der QUELLE (siehe
 * `course-language.util.ts`). Ohne Wahl gilt die Oberflächensprache — der Server liefert je Stelle
 * die Übersetzung, wo es eine gibt, sonst das Original, und die Anzeige nennt dann „Original".</p>
 *
 * <p>Welche Sprachen ein Kurs hat, sammelt der Dienst im Arbeitsspeicher (Signal, damit Auswahl und
 * Anzeige mitlaufen): aus der Übersetzungs-Übersicht und aus den `commentLanguages` der Linien, die
 * eine Seite ohnehin lädt. Die Übersicht wird je Kurs und Sitzung höchstens EINMAL angefragt.</p>
 *
 * <p>Die Übersicht holt {@link ensureLanguages} über den Kurs-Dienst (oder einen hereingereichten
 * Abruf). Fehler sind still: sie kosten höchstens die Auswahl, nie die Seite. Ohne Kurs-Dienst (Tests,
 * die den Dienst mit `new` bauen) passiert dort einfach nichts.</p>
 */
@Injectable({ providedIn: 'root' })
export class CourseLanguageService {
  private readonly choices = signal<Record<string, string>>({});
  private readonly known = signal<Record<string, string[]>>({});
  private files: Record<string, number> = {};
  private readonly overviewAsked = new Set<number>();

  constructor(private translate: TranslateService, private courses?: CourseService) {
    const stored = readJson<Partial<Stored>>(localStore(), COURSE_LANG_KEY);
    const c = stored?.c && typeof stored.c === 'object' ? stored.c : {};
    const f = stored?.f && typeof stored.f === 'object' ? stored.f : {};
    this.choices.set({ ...c });
    this.files = { ...f };
  }

  /** Sprache der Oberfläche (Signal in ngx-translate 18 — Templates laufen damit mit). */
  uiLanguage(): string {
    return normLang(this.translate.currentLang()) ?? normLang(this.translate.getFallbackLang()) ?? 'en';
  }

  /** Schlüssel eines Kurses: bevorzugt über die bookId (auch über den Dateinamen aufgelöst). */
  key(ref: CourseRef | null | undefined): string | null {
    if (!ref) return null;
    if (ref.bookId != null && Number.isFinite(ref.bookId)) return 'b' + ref.bookId;
    const file = ref.fileName?.trim();
    if (!file) return null;
    const id = this.files[file];
    return id != null ? 'b' + id : 'f' + file;
  }

  /** Die gemerkte Wahl (`null` = keine — dann gilt die Oberflächensprache). */
  choice(ref: CourseRef | null | undefined): string | null {
    const k = this.key(ref);
    if (!k) return null;
    const c = this.choices();
    if (c[k]) return c[k];
    // Wahl am Einzel-Puzzle getroffen, bevor die Kurs-Id bekannt war.
    const file = ref?.fileName?.trim();
    return file ? (c['f' + file] ?? null) : null;
  }

  /** Was als `?lang=` an den Server geht: die Wahl, sonst die Oberflächensprache. */
  requestLang(ref: CourseRef | null | undefined): string {
    return this.choice(ref) ?? this.uiLanguage();
  }

  /** Bekannte Sprachen des Kurses, die Quelle zuerst (leer = unbekannt). */
  languages(ref: CourseRef | null | undefined): string[] {
    const k = this.key(ref);
    return (k ? this.known()[k] : undefined) ?? NO_LANGUAGES;
  }

  /** Die Quellsprache („Original"), soweit bekannt. */
  source(ref: CourseRef | null | undefined): string | null {
    return this.languages(ref)[0] ?? null;
  }

  /** Wirksame Sprache: die gewünschte, wenn der Kurs sie hat, sonst das Original (`null` = unbekannt). */
  effective(ref: CourseRef | null | undefined): string | null {
    return effectiveLanguage(this.requestLang(ref), this.languages(ref));
  }

  /** Wahl merken (je Gerät). „Original" = das Kürzel der Quelle. */
  setChoice(ref: CourseRef | null | undefined, lang: string): void {
    const k = this.key(ref);
    const l = normLang(lang);
    if (!k || !l) return;
    this.choices.update(c => ({ ...c, [k]: l }));
    this.persist();
  }

  /** Sprachen einer Liste (Quelle zuerst) zum Bekannten dazunehmen. */
  noteLanguages(ref: CourseRef | null | undefined, langs: readonly (string | null | undefined)[] | null | undefined): void {
    const k = this.key(ref);
    if (!k || !langs?.length) return;
    this.known.update(m => {
      const merged = mergeLanguages(m[k] ?? [], langs);
      return sameList(m[k], merged) ? m : { ...m, [k]: merged };
    });
  }

  /** Sprachen aus gelieferten Linien (`commentLanguages` je Linie). */
  noteLines(ref: CourseRef | null | undefined,
            lines: readonly { commentLanguages?: readonly string[] | null }[] | null | undefined): void {
    this.noteLanguages(ref, languagesFromLines(lines));
  }

  /** Sprachen aus der Übersetzungs-Übersicht — sie ist die vollständige Auskunft und ERSETZT das Bekannte. */
  noteOverview(ref: CourseRef | null | undefined, overview: LanguageOverview | null | undefined): void {
    const k = this.key(ref);
    const langs = languagesFromOverview(overview);
    if (!k || langs.length === 0) return;
    this.known.update(m => sameList(m[k], langs) ? m : { ...m, [k]: langs });
  }

  /**
   * Sprachen des Kurses einmal je Sitzung aus der Übersicht holen. Bewusst still: ohne Übersicht
   * (Kurs nicht öffentlich und niemand angemeldet, Server älter) fehlt nur die Auswahl.
   */
  ensureLanguages(bookId: number | null | undefined, load?: () => Observable<LanguageOverview>): void {
    if (bookId == null || !Number.isFinite(bookId) || this.overviewAsked.has(bookId)) return;
    const courses = this.courses;
    const loader = load ?? (courses ? () => courses.getTranslations(bookId) : null);
    if (!loader) return;
    this.overviewAsked.add(bookId);
    try {
      loader().subscribe({
        next: o => this.noteOverview({ bookId }, o),
        error: () => { /* Beiwerk — die Seite bleibt bedienbar */ },
      });
    } catch {
      // Ein Abruf, der schon beim Aufbauen wirft, darf die Seite nicht mitreißen.
    }
  }

  /**
   * Kurs-Id und Dateiname verknüpfen. Eine Wahl, die am Einzel-Puzzle unter dem Dateinamen
   * getroffen wurde, wandert dabei zur Kurs-Id (sofern dort noch keine steht) — ebenso die dort
   * gesehenen Sprachen.
   */
  rememberFile(bookId: number | null | undefined, fileName: string | null | undefined): void {
    this.rememberFiles([{ bookId, fileName }]);
  }

  rememberFiles(pairs: readonly { bookId?: number | null; fileName?: string | null }[]): void {
    let changed = false;
    for (const { bookId, fileName } of pairs) {
      const file = fileName?.trim();
      if (bookId == null || !Number.isFinite(bookId) || !file) continue;
      const fileKey = 'f' + file;
      const bookKey = 'b' + bookId;
      if (this.files[file] !== bookId) { this.files[file] = bookId; changed = true; }
      const c = this.choices();
      if (c[fileKey]) {
        const next = { ...c };
        if (!next[bookKey]) next[bookKey] = next[fileKey];
        delete next[fileKey];
        this.choices.set(next);
        changed = true;
      }
      const seen = this.known()[fileKey];
      if (seen?.length) this.noteLanguages({ bookId }, seen);
    }
    if (changed) this.persist();
  }

  private persist(): void {
    // Quota/gesperrt: dann gilt die Wahl nur in dieser Sitzung — sie ist eine Bequemlichkeit.
    writeJson(localStore(), COURSE_LANG_KEY, { c: this.choices(), f: this.files } satisfies Stored);
  }
}

function sameList(a: readonly string[] | undefined, b: readonly string[]): boolean {
  return !!a && a.length === b.length && a.every((x, i) => x === b[i]);
}
