import { TRAINING_NOTE_CATEGORIES, TrainingNotebookNote } from './training-videos.service';

/**
 * Reine Regeln des Trainer-Notizbuchs (Spec daneben): Filter nach Art/Partie/Suche, Reiter je Kategorie mit Anzahl,
 * Gruppen je Stunde, Ausschnitt um die Notizzeit und das Linkziel der Partie. Doku: `docs/frontend/trainerstunden.md`.
 */

/** `all` | `own` | `lesson` | `exercise` — Art der Partie, auf die eine Notiz zeigt. */
export type NotebookKindFilter = 'all' | 'own' | 'lesson' | 'exercise';

export interface NotebookFilter {
  kind: NotebookKindFilter;
  /** Schlüssel aus `notebookGameKey`, null = alle Partien. */
  game: string | null;
  /** Treffer der Volltextsuche (Notiz-Ids), null = keine Suche aktiv. */
  ids: ReadonlySet<number> | null;
}

/** Ausschnitt vor der Aussage (der Satz beginnt oft kurz vor der markierten Sekunde) und seine Länge. */
export const CLIP_LEAD_SEC = 5;
export const CLIP_LENGTH_SEC = 60;

/** Eine Partie stundenübergreifend: dieselbe RookHub-Partie bzw. dieselben Namen sind dieselbe Partie. */
export function notebookGameKey(n: Pick<TrainingNotebookNote, 'rookhubGameId' | 'gameWhite' | 'gameBlack' | 'gameKey'>): string | null {
  if (n.rookhubGameId != null) return 'g' + n.rookhubGameId;
  if (!n.gameKey) return null;
  return 'n' + (n.gameWhite ?? '?').trim() + '|' + (n.gameBlack ?? '?').trim();
}

export function filterNotes(notes: readonly TrainingNotebookNote[], f: NotebookFilter): TrainingNotebookNote[] {
  return notes.filter(n =>
    (f.kind === 'all' || n.gameKind === f.kind)
    && (f.game == null || notebookGameKey(n) === f.game)
    && (f.ids == null || f.ids.has(n.id)));
}

export interface CategoryTab { category: string; count: number; }

/** Reiter in fester Reihenfolge, nur Kategorien mit Notizen; Unbekanntes landet bei „allgemein“. */
export function categoryTabs(notes: readonly TrainingNotebookNote[]): CategoryTab[] {
  const counts = new Map<string, number>();
  for (const n of notes) {
    const c = normalizeCategory(n.category);
    counts.set(c, (counts.get(c) ?? 0) + 1);
  }
  return TRAINING_NOTE_CATEGORIES.filter(c => counts.has(c)).map(c => ({ category: c, count: counts.get(c)! }));
}

export function normalizeCategory(c: string | null | undefined): string {
  return (TRAINING_NOTE_CATEGORIES as readonly string[]).includes(c ?? '') ? c! : 'allgemein';
}

export interface LessonGroup { lessonId: number; date: string; notes: TrainingNotebookNote[]; }

/** Notizen einer Kategorie je Stunde, neueste Stunde zuerst, in der Stunde nach Zeit (der Server liefert so). */
export function groupByLesson(notes: readonly TrainingNotebookNote[], category: string): LessonGroup[] {
  const groups: LessonGroup[] = [];
  const byLesson = new Map<number, LessonGroup>();
  for (const n of notes) {
    if (normalizeCategory(n.category) !== category) continue;
    let g = byLesson.get(n.lessonId);
    if (!g) { g = { lessonId: n.lessonId, date: n.date, notes: [] }; byLesson.set(n.lessonId, g); groups.push(g); }
    g.notes.push(n);
  }
  return groups.sort((a, b) => b.date.localeCompare(a.date) || a.lessonId - b.lessonId);
}

/** Ausschnitt um die Notizzeit: 5 s davor bis 60 s danach, nie über das Ende der Stunde. */
export function clipRange(n: Pick<TrainingNotebookNote, 'startSec' | 'lessonDurationSec'>): { start: number; end: number } | null {
  if (n.startSec == null) return null;
  const start = Math.max(0, n.startSec - CLIP_LEAD_SEC);
  const limit = n.lessonDurationSec && n.lessonDurationSec > start ? n.lessonDurationSec : Number.POSITIVE_INFINITY;
  return { start, end: Math.min(n.startSec + CLIP_LENGTH_SEC, limit) };
}

/** Linkziel der Partie: die eigene RookHub-Partie, sonst der Eintrag, in dem die Partie liegt (die Notiz steht im
 *  privaten Teil, eine Lehrpartie im allgemeinen), an der Notizzeit. */
export function noteGameLink(n: Pick<TrainingNotebookNote, 'rookhubGameId' | 'gameKind' | 'lessonId' | 'startSec' | 'gameLessonId'>):
  { link: (string | number)[]; query: Record<string, number> | null } {
  if (n.rookhubGameId != null && n.gameKind === 'own') return { link: ['/games', n.rookhubGameId], query: null };
  return { link: ['/training-videos', n.gameLessonId ?? n.lessonId], query: n.startSec != null ? { t: n.startSec } : null };
}
