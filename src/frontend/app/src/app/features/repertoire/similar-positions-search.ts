import { Observable, of } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';
import type {
  SimilarMoveInput, SimilarPositionMatch, SimilarPositionsRequest, SimilarPositionsResult, SimilarityPreset,
} from '../../core/repertoire.service';
import type { SimilarRepertoireOption } from './similar-positions.component';
import { moveKey, parseMoveInput } from './similar-move.util';
import { localStore, readJson, writeJson } from '../../core/local-json-store';

/** Voreinstellung + Spiegel-/Seite-am-Zug-Schalter der Ähnlichkeitssuche, pro Gerät gemerkt (wie
 * die Sicht). Der erwogene ZUG gehört bewusst NICHT dazu: er gilt für genau eine Frage. */
export const SIMILAR_PREFS_KEY = 'rookhub_position_reps_similar';
export const SIMILAR_LIMIT = 40;

/** Der Teil von `RepertoireService`, den die Suche braucht. */
export interface SimilarSearchApi {
  list(): Observable<{ id: number; name: string }[] | null>;
  findSimilarPositions(req: SimilarPositionsRequest): Observable<SimilarPositionsResult>;
}

export interface SimilarSearchDeps {
  api: () => SimilarSearchApi;
  /** Die Ankerstellung (aktuelle Brett-Stellung). */
  fen: () => string;
  /** Eine Einstellung hat sich geändert → die Sicht neu laden (wenn sie offen ist). */
  reload: () => void;
}

/**
 * Zustand und Logik der Sicht „Ähnliche Stellungen" im Stellungs-Panel (Zerlegung Z32): Repertoire-Filter,
 * Voreinstellung/Spiegel/Seite am Zug (pro Gerät gemerkt), der erwogene Zug (auf der Ankerstellung aufgelöst,
 * geschickt als from/to) und die Aufbereitung der Treffer. Die Anfrage selbst (Abbestellen, veraltete Antworten,
 * Fehler, Zeitbudget) führt `PositionRepertoiresComponent` für alle drei Sichten gemeinsam. Ohne Angular.
 */
export class SimilarPositionsSearch {
  /** Auswählbare Repertoires (leer, solange/falls die Liste nicht kam). */
  options: SimilarRepertoireOption[] = [];
  selected = new Set<number>();
  preset: SimilarityPreset = 'ausgewogen';
  mirrored = true;
  /** Nur Stellungen mit derselben Seite am Zug (Default aus — wie serverseitig). */
  sameSide = false;
  /** Rohtext des Zug-Feldes, so wie getippt (SAN). */
  moveText = '';
  /** Auf der ANKER-Stellung aufgelöster Zug; `null` = kein gültiger Zug eingegeben. */
  move: SimilarMoveInput | null = null;
  /** Kanonischer SAN des erkannten Zuges ('' ohne Zug). */
  moveSan = '';
  /** Eingabe nicht leer, auf dieser Stellung aber kein legaler Zug. */
  moveInvalid = false;
  /** Treffer ohne passenden Zug ausblenden. Bewusst NICHT gemerkt: der Zug gilt nur für diese Frage. */
  onlyWithMove = false;
  matches: SimilarPositionMatch[] = [];

  private optionsLoaded = false;
  /** Sobald der Nutzer die Auswahl angefasst hat, wird sie nicht mehr auf „alle" zurückgesetzt. */
  private selectionTouched = false;

  constructor(private readonly deps: SimilarSearchDeps) {
    this.restorePrefs();
  }

  /** Erst die Repertoire-Liste (einmalig, für den Filter), dann die eigentliche Suche — als EINE Kette, damit das
   *  Abbestellen (neue Stellung) auch die Suche selbst abbricht. `null` = nichts ausgewählt, nichts gefragt. */
  search(): Observable<SimilarPositionsResult | null> {
    return this.repertoireOptions().pipe(
      switchMap(opts => {
        this.applyOptions(opts);
        // Bewusst KEINE Anfrage, wenn der Nutzer alles abgewählt hat — leere Liste hieße für den
        // Server „alle", und das wäre das Gegenteil dessen, was er gerade eingestellt hat.
        if (this.options.length > 0 && this.selected.size === 0) return of(null);
        const ids = this.options.filter(o => this.selected.has(o.id)).map(o => o.id);
        const req: SimilarPositionsRequest = {
          fen: this.deps.fen(),
          repertoireIds: ids,
          preset: this.preset,
          includeMirrored: this.mirrored,
          sameSideToMove: this.sameSide,
          limit: SIMILAR_LIMIT,
          // Kein minScore: die Schwelle setzt der Server (sonst driften die beiden Defaults).
        };
        if (this.move) {
          req.move = this.move;
          if (this.onlyWithMove) req.onlyWithMove = true;
        }
        return this.deps.api().findSimilarPositions(req);
      }),
    );
  }

  /** Antwort übernehmen. Der Server sortiert bereits nach Score; hier wird die Zusage der Sicht abgesichert —
   *  sortiert nach dem ENDWERT, der Reihenfolge, die die Liste zeigt. */
  applyResult(res: SimilarPositionsResult | null): void {
    if (!res) { this.matches = []; return; }
    this.matches = this.applyMoveFilter([...(res.matches ?? [])])
      .sort((a, b) => (b.score ?? 0) - (a.score ?? 0));
  }

  /** Repertoire-Liste für den Filter; scheitert sie, wird ohne Filter gesucht (Server: alle). */
  private repertoireOptions(): Observable<SimilarRepertoireOption[]> {
    if (this.optionsLoaded) return of(this.options);
    return this.deps.api().list().pipe(
      map(reps => (reps ?? []).map(r => ({ id: r.id, name: r.name }))),
      tap(opts => { this.options = opts; this.optionsLoaded = true; }),
      catchError(() => of([] as SimilarRepertoireOption[])),
    );
  }

  private applyOptions(opts: SimilarRepertoireOption[]): void {
    this.options = opts;
    if (this.selectionTouched) {
      // Zwischenzeitlich gelöschte Repertoires aus der Auswahl werfen (als neue Menge — die Sicht ist OnPush).
      const valid = new Set(opts.map(o => o.id));
      if ([...this.selected].some(id => !valid.has(id))) this.selected = new Set([...this.selected].filter(id => valid.has(id)));
      return;
    }
    this.selected = new Set(opts.map(o => o.id)); // Default: alle
  }

  setPreset(preset: SimilarityPreset): void {
    if (this.preset === preset) return;
    this.preset = preset;
    this.savePrefs();
    this.deps.reload();
  }

  setMirrored(on: boolean): void {
    if (this.mirrored === on) return;
    this.mirrored = on;
    this.savePrefs();
    this.deps.reload();
  }

  setSameSide(on: boolean): void {
    if (this.sameSide === on) return;
    this.sameSide = on;
    this.savePrefs();
    this.deps.reload();
  }

  /** Zug-Eingabe: Text merken, auf der Ankerstellung auflösen und nur dann neu suchen, wenn sich der ZUG geändert
   *  hat — `Ncd5` nach `Nd5` getippt ist derselbe Zug, und jeder Tastendruck auf dem Weg dahin (`N`, `Nd`) keiner. */
  setMoveText(text: string): void {
    const before = moveKey(this.move);
    this.applyMoveText(text);
    if (moveKey(this.move) !== before) this.deps.reload();
  }

  /** Zug-Zwang; ohne gültigen Zug wirkungslos (der Schalter ist dann auch nicht sichtbar). */
  setOnlyWithMove(on: boolean): void {
    if (this.onlyWithMove === on) return;
    this.onlyWithMove = on;
    if (this.move) this.deps.reload();
  }

  setSelection(ids: number[]): void {
    this.selected = new Set(ids);
    this.selectionTouched = true;
    this.deps.reload();
  }

  /** Die Ankerstellung hat sich geändert: „Nd5" ist dort ein anderer Zug — oder gar keiner mehr. Vor dem Laden. */
  reanchor(): void {
    if (this.moveText.trim()) this.applyMoveText(this.moveText);
  }

  /** Reine Auswertung ohne Nebenwirkung auf die Suche. */
  private applyMoveText(text: string): void {
    this.moveText = text;
    const parsed = parseMoveInput(this.deps.fen(), text);
    this.move = parsed.move;
    this.moveSan = parsed.san;
    this.moveInvalid = parsed.invalid;
  }

  /** Sicherheitsnetz für „nur Stellungen mit diesem Zug": ein Server, der `onlyWithMove` (noch) nicht kennt, liefert
   *  sonst Treffer ohne Zug-Treffer. Die Bedingung ist dieselbe wie serverseitig, das Filtern also folgenlos. */
  private applyMoveFilter(matches: SimilarPositionMatch[]): SimilarPositionMatch[] {
    if (!this.move || !this.onlyWithMove) return matches;
    return matches.filter(m => m.moveMatch !== null);
  }

  private restorePrefs(): void {
    // Kaputter/gesperrter Speicher: Voreinstellungen bleiben auf Default.
    const saved = readJson<{ preset?: string; mirrored?: boolean; sameSide?: boolean }>(localStore(), SIMILAR_PREFS_KEY);
    if (!saved || typeof saved !== 'object') return;
    if (saved.preset === 'struktur' || saved.preset === 'ausgewogen' || saved.preset === 'stellungsbild') {
      this.preset = saved.preset;
    }
    if (typeof saved.mirrored === 'boolean') this.mirrored = saved.mirrored;
    if (typeof saved.sameSide === 'boolean') this.sameSide = saved.sameSide;
  }

  private savePrefs(): void {
    // Privatmodus: Auswahl gilt nur für die Sitzung.
    writeJson(localStore(), SIMILAR_PREFS_KEY, { preset: this.preset, mirrored: this.mirrored, sameSide: this.sameSide });
  }
}
