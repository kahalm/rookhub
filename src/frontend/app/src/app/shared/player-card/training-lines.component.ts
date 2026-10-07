import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateService } from '@ngx-translate/core';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { Router } from '@angular/router';
import { TreeFilter } from '@lh/core/league.models';
import { HandoffService } from '@rh/core/handoff.service';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { readChapterColorOverrides } from '@rh/features/repertoire/repertoire-color.util';
import { PLAYER_CARD_API } from './player-card-api';
import { ChapterColorOverrides, TRAINING_LINES_KEY, TRAINING_REPERTOIRE_MAX, TrainingLine, TrainingLines, lineText, matchedUntil, percent, trainingFilterParams,
  trainingRepertoireName } from './training-lines';

interface Remembered { repertoire: number | null; color: 'w' | 'b' | null }

/**
 * Abschnitt „Trainingslinien" der Spielerkarte (Wunsch 2026-10-07): die Linien eines eigenen Repertoires (nur „Für Extension
 * und Vorbereitung verwenden"), gereiht danach, wie wahrscheinlich DIESER Gegner sie aufs Brett bringt — gemessen an seinen
 * Partien (Filter der Karte). Je Linie „Trainieren" (Trainer der Haupt-App mit `?line=`), dazu „Alle in dieser Reihenfolge
 * trainieren" (`?opponent=`: der Trainer holt dieselben Linien und sortiert danach). Von LeagueHub aus per Einmal-Code
 * hinüber (`HandoffService.jumpToRookHub`). Nur angemeldet und nie über einen Teilen-Link — das entscheidet die Karte.
 * Deutsch wie die übrige Karte (LeagueHub stellt keine Sprache ein).
 */
@Component({
  selector: 'lh-training-lines',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="btn-sec tl-toggle" [attr.aria-expanded]="open()" (click)="toggle()">
      {{ open() ? 'Trainingslinien schließen' : 'Trainingslinien gegen ' + who() }}</button>
    @if (open()) {
      <section class="tl" aria-label="Trainingslinien">
        <h3>Trainingslinien <span class="muted small">— deine Linien, wahrscheinlichste zuerst</span></h3>
        @if (data(); as d) {
          @if (!d.repertoires.length) {
            <p class="muted small">Kein Repertoire ist für die Vorbereitung freigegeben. In RookHub unter „Repertoires“ → Bearbeiten
              „Für Extension und Vorbereitung verwenden“ anhaken.</p>
          } @else {
            <div class="tl-pick">
              <!-- Vorgabe: alle markierten Repertoires der Farbe in EINER Reihung (Wunsch 2026-10-07); die Farbe ist die Wahl -->
              @if (d.colors.length > 1) {
                <div class="seg tl-color" role="group" [attr.aria-label]="t('yourColor')">
                  @for (c of d.colors; track c) {
                    <button type="button" [attr.aria-pressed]="d.color === c" [disabled]="loading()" (click)="pickColor(c)">
                      {{ t(c === 'w' ? 'iHaveWhite' : 'iHaveBlack') }}</button>
                  }
                </div>
              }
              <label>Repertoire
                <select class="tl-rep" (change)="pickRepertoire($any($event.target).value)" [disabled]="loading()">
                  <option value="" [selected]="d.repertoire === null">{{ t('allMarked') }}</option>
                  @for (r of d.repertoires; track r.id) { <option [value]="r.id" [selected]="r.id === d.repertoire">{{ r.name }}</option> }
                </select>
              </label>
            </div>
            <p class="muted small tl-basis" role="status">
              @if (d.color) { Du mit {{ d.color === 'w' ? 'Weiß' : 'Schwarz' }} — gezählt: {{ d.games }}
                {{ d.games === 1 ? 'Partie' : 'Partien' }} von {{ who() }} mit {{ d.color === 'w' ? 'Schwarz' : 'Weiß' }}. }
              @if (loading()) { Lade … }
            </p>
            @if (d.lines.length) {
              <div class="tl-actions">
                @if (canCreate) {
                  <button type="button" class="btn-pri tl-create" [disabled]="busy()" [title]="t('hint')" (click)="createRepertoire(d)">
                    {{ creating() ? t('busy') : t('button') }}</button>
                }
                @if (d.repertoire !== null) {
                  <button type="button" class="btn-sec tl-all" [disabled]="busy()" (click)="trainAll(d)">Alle in dieser Reihenfolge trainieren</button>
                }
              </div>
              @if (d.repertoire === null) { <p class="muted small tl-all-hint">{{ t('trainAllHint') }}</p> }
              @if (createNote(); as n) { <p class="small" [class.err]="n.err" role="status">{{ n.text }}</p> }
              <ol class="tl-list">
                @for (l of d.lines; track l.key) {
                  <li [class.never]="l.neverReached">
                    <div class="tl-line">
                      <span class="tl-moves">{{ text(l) }}</span>
                      <span class="muted small tl-chapter">{{ origin(d, l) }}</span>
                    </div>
                    <div class="tl-stats">
                      @if (l.neverReached) {
                        <span class="tl-p muted">nie erreicht</span>
                        @if (l.reached) { <span class="muted small">nur über Zugumstellung: {{ l.reached }} {{ l.reached === 1 ? 'Partie' : 'Partien' }}</span> }
                      } @else if (l.missing > 0) {
                        <span class="tl-p tl-partial" [title]="'Er spielt die Linie bis ' + until(d, l) + ', danach ist er anders weitergegangen'">
                          bis {{ until(d, l) }} dabei</span>
                        <span class="muted small">Anfang {{ pct(l.prefixProbability) }}, {{ l.prefixReached }}
                          {{ l.prefixReached === 1 ? 'Partie' : 'Partien' }}</span>
                      } @else {
                        <span class="tl-p" [title]="'Wahrscheinlichkeit, dass ' + who() + ' diese Linie spielt'">{{ pct(l.probability) }}</span>
                        <span class="muted small">{{ l.reached }} {{ l.reached === 1 ? 'Partie' : 'Partien' }}@if (l.lastYear) {, zuletzt {{ l.lastYear }}}</span>
                      }
                      <button type="button" class="btn-sec tl-train" [disabled]="busy()" (click)="train(d, l)"
                              [attr.aria-label]="'Linie ' + text(l) + ' trainieren'">Trainieren</button>
                    </div>
                  </li>
                }
              </ol>
              @if (d.more) { <p class="muted small">… und {{ d.more }} weitere {{ d.more === 1 ? 'Linie' : 'Linien' }}.</p> }
            } @else if (!loading()) {
              <p class="muted small">Für diese Farbe gibt es keine Linien.</p>
            }
          }
        } @else if (loading()) { <p class="muted small">Lade Trainingslinien …</p> }
        @if (error(); as e) { <p class="err small" role="alert">{{ e }}</p> }
      </section>
    }
  `,
  styles: [`
    .tl-toggle { margin-top: .5rem; }
    .tl { margin-top: .75rem; }
    .tl-pick { display: flex; flex-wrap: wrap; gap: .5rem 1rem; align-items: center; }
    .tl-pick label { display: flex; gap: .5rem; align-items: center; min-width: 0; max-width: 100%; }
    .tl-pick select { min-width: 0; max-width: min(28rem, 100%); font: inherit; padding: .3rem .4rem; }
    .tl-basis { margin: .4rem 0; }
    .tl-actions { margin: .4rem 0 .6rem; }
    .tl-list { list-style: none; margin: 0; padding: 0; }
    .tl-list li { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: .25rem 1rem;
      padding: .4rem 0; border-top: 1px solid var(--line, #d9dfe7); }
    .tl-list li.never .tl-moves { opacity: .7; }
    .tl-line { flex: 1 1 16rem; min-width: 0; overflow-wrap: anywhere; }
    .tl-chapter { display: block; }
    .tl-stats { display: flex; flex-wrap: wrap; align-items: center; gap: .25rem .75rem; }
    .tl-p { font-weight: 600; min-width: 3.5rem; text-align: right; font-variant-numeric: tabular-nums; }
    .tl-partial { font-weight: 500; }
    .tl-actions { display: flex; flex-wrap: wrap; gap: .5rem; }
  `],
})
export class TrainingLinesComponent {
  private readonly api = inject(PLAYER_CARD_API);
  private readonly router = inject(Router);
  private readonly handoff = inject(HandoffService);
  private readonly confirm = inject(ConfirmService);
  private readonly translate = inject(TranslateService, { optional: true });

  /** Womit die API den Gegner findet (Karte: `key` bzw. FIDE-ID). */
  readonly key = input.required<string>();
  /** Name des Gegners („Huber, Franz"). */
  readonly name = input<string>('');
  /** Der Filter der Karte — dieselben Partien wie Profil und Baum. */
  readonly filter = input<TreeFilter | null>(null);

  readonly open = signal(false);
  readonly data = signal<TrainingLines | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly creating = signal(false);
  readonly createNote = signal<{ text: string; err: boolean } | null>(null);
  /** „Show me lines to train" anbieten: die API kann es (Spielervorbereitung, LeagueHub). */
  readonly canCreate = !!this.api.trainingRepertoire;
  readonly who = computed(() => (this.name() || '').split(',')[0].trim() || 'diesen Gegner');
  private seq = 0;
  private loadedFor = '';

  readonly text = (l: TrainingLine) => lineText(l.moves, l.start);
  readonly until = (d: TrainingLines, l: TrainingLine) => matchedUntil(l, d.color ?? 'w') ?? '?';
  /** Woher die Linie stammt: bei „Alle markierten" Repertoire · Kapitel, sonst das Kapitel. */
  readonly origin = (d: TrainingLines, l: TrainingLine) =>
    [d.repertoire === null ? l.repertoireName : '', l.chapter].filter(x => !!x).join(' · ');

  /** Text des Knopfs „Show me lines to train" und seiner Meldungen: in RookHub in der Sprache der Oberfläche (en/de/hr/hu),
   *  in LeagueHub (stellt keine Sprache ein) deutsch wie die übrige Karte. */
  t(key: string, params: Record<string, unknown> = {}): string {
    const name = trainingRepertoireName(this.name());
    const all = { name, max: TRAINING_REPERTOIRE_MAX, ...params };
    if (this.translate?.getCurrentLang()) {
      const v = this.translate.instant(`prep.trainingRepertoire.${key}`, all);
      if (v && v !== `prep.trainingRepertoire.${key}`) return v;
    }
    return GERMAN[key]?.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(all[k as keyof typeof all] ?? '')) ?? key;
  }

  /** „Show me lines to train": Repertoire „Prep: … Jahr" anlegen (gleichnamiges wird nach Rückfrage ersetzt) und öffnen. */
  async createRepertoire(d: TrainingLines): Promise<void> {
    const create = this.api.trainingRepertoire;
    if (!create || !d.repertoires.length) return;
    if (!(await firstValueFrom(this.confirm.ask(this.t('confirm'))))) return;
    this.busy.set(true);
    this.creating.set(true);
    this.createNote.set(null);
    try {
      const r = await create.call(this.api, this.key(), {
        repertoire: d.repertoire, color: d.color, filter: this.filter(),
        chapterColors: this.overrides(d.repertoire, d.repertoires.map(r => r.id)),
      });
      this.createNote.set({ text: this.t('done', { name: r.name, lines: r.lines }), err: false });
      // Die Kapitel des neuen Repertoires trainieren mit der Farbe von hier (die Auto-Erkennung könnte an einer Auswahl kippen).
      const params: Record<string, string> = d.color ? { trainColor: d.color } : {};
      if (this.handoff.rookHubUrl) await this.handoff.jumpToRookHub(`repertoires/${r.id}?${new URLSearchParams(params).toString()}`);
      else await this.router.navigate(['/repertoires', r.id], { queryParams: params });
    } catch (e) {
      // die Quelle heißt selbst wie das Ziel: der Server schützt sie (400 sameRepertoire)
      const same = (e as { error?: { reason?: string } })?.error?.reason === 'sameRepertoire';
      this.createNote.set({ text: this.t(same ? 'sameRepertoire' : 'failed'), err: true });
    } finally {
      this.creating.set(false);
      this.busy.set(false);
    }
  }
  readonly pct = percent;

  toggle(): void {
    this.open.update(o => !o);
    if (this.open()) {
      const want = `${this.key()}|${JSON.stringify(this.filter())}`;
      if (want !== this.loadedFor) void this.load(this.remembered());
    }
  }

  /** Filter: '' = alle markierten (Vorgabe), sonst ein Repertoire. Die Farbe bleibt, wenn es sie dort gibt. */
  pickRepertoire(value: string): void {
    const id = Number(value) || null;
    void this.load({ repertoire: id, color: this.data()?.color ?? null });
  }

  pickColor(c: 'w' | 'b'): void {
    void this.load({ repertoire: this.data()?.repertoire ?? null, color: c });
  }

  private remembered(): Remembered {
    const r = readJson<Partial<Remembered>>(localStore(), TRAINING_LINES_KEY);
    return {
      repertoire: typeof r?.repertoire === 'number' ? r.repertoire : null,
      color: r?.color === 'w' || r?.color === 'b' ? r.color : null,
    };
  }

  private async load(want: Remembered, retried = false): Promise<void> {
    const fetch = this.api.trainingLines;
    if (!fetch) return;
    const my = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    const key = this.key(), filter = this.filter();
    try {
      const d = await fetch.call(this.api, key, {
        repertoire: want.repertoire, color: want.color, filter,
        chapterColors: this.overrides(want.repertoire, this.data()?.repertoires.map(r => r.id) ?? []),
      });
      if (my !== this.seq) return;
      // Erste Abfrage über alle markierten: die Ids kennt die Seite erst jetzt — gibt es eigene Kapitelfarben, einmal mit ihnen.
      if (want.repertoire === null && !this.data() && !retried
          && Object.keys(this.overrides(null, d.repertoires.map(r => r.id)) ?? {}).length) {
        this.data.set(d);
        void this.load({ repertoire: null, color: d.color }, true);
        return;
      }
      this.data.set(d);
      this.loadedFor = `${key}|${JSON.stringify(filter)}`;
      // nur eine Bequemlichkeit — scheitert still (localStorage voll/gesperrt)
      writeJson(localStore(), TRAINING_LINES_KEY, { repertoire: d.repertoire, color: d.color });
    } catch (e) {
      if (my !== this.seq) return;
      // das gemerkte Repertoire gibt es nicht mehr (gelöscht, nicht mehr freigegeben): einmal ohne Vorgabe
      if (!retried && want.repertoire !== null && (e as { status?: number })?.status === 404) {
        void this.load({ repertoire: null, color: null }, true);
        return;
      }
      this.error.set('Die Trainingslinien konnten nicht geladen werden.');
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }

  /** Eigene Kapitelfarben (Trainer, je Gerät): flach für ein gewähltes Repertoire, sonst je Repertoire; `null` = keine. */
  private overrides(repertoire: number | null, all: number[]): ChapterColorOverrides | null {
    if (repertoire !== null) {
      const flat = readChapterColorOverrides(repertoire);
      return Object.keys(flat).length ? flat : null;
    }
    const per: ChapterColorOverrides = {};
    for (const id of all) {
      const m = readChapterColorOverrides(id);
      if (Object.keys(m).length) per[String(id)] = m;
    }
    return Object.keys(per).length ? per : null;
  }

  /** Eine Linie im Trainer ihres Repertoires. */
  train(d: TrainingLines, l: TrainingLine): Promise<void> {
    return this.go(d, { line: l.key, chapter: l.chapter || null }, l.repertoireId);
  }

  /** Alle Linien dieses Repertoires und dieser Farbe, in der Reihenfolge nach diesem Gegner. */
  trainAll(d: TrainingLines): Promise<void> {
    return this.go(d, {});
  }

  private async go(d: TrainingLines, extra: Record<string, string | null>, repertoire = d.repertoire): Promise<void> {
    if (repertoire === null) return;
    const params: Record<string, string> = {
      ...this.api.trainerParams?.(this.key()) ?? { opponent: `league:${this.key()}` },
      ...(d.color ? { color: d.color } : {}),
      ...trainingFilterParams(this.filter()),
    };
    for (const [k, v] of Object.entries(extra)) if (v) params[k] = v;
    const path = `repertoires/${repertoire}/train`;
    this.busy.set(true);
    try {
      if (this.handoff.rookHubUrl) await this.handoff.jumpToRookHub(`${path}?${new URLSearchParams(params).toString()}`);
      else await this.router.navigate(['/' + path], { queryParams: params });
    } finally {
      this.busy.set(false);
    }
  }
}

/** Die Texte des Knopfs in LeagueHub (ohne Sprache) — dieselben wie `prep.trainingRepertoire.*` in de.json. */
const GERMAN: Record<string, string> = {
  button: 'Trainings-Repertoire anlegen',
  hint: 'Legt dir ein eigenes Repertoire „{{name}}“ mit den (bis zu {{max}}) Linien an, die du gegen diesen Gegner am wahrscheinlichsten triffst.',
  confirm: 'Repertoire „{{name}}“ mit den bis zu {{max}} wichtigsten Linien gegen diesen Gegner anlegen? Ein vorhandenes gleichnamiges wird ersetzt.',
  busy: 'Lege an …',
  failed: 'Das Trainings-Repertoire ließ sich nicht anlegen.',
  done: '„{{name}}“ ist fertig ({{lines}} Linien) — wird geöffnet …',
  sameRepertoire: 'Das gewählte Repertoire heißt selbst „{{name}}“ — es würde sich selbst überschreiben. Wähle ein anderes oder benenne es um.',
  allMarked: 'Alle markierten',
  yourColor: 'Deine Farbe',
  iHaveWhite: 'Ich habe Weiß',
  iHaveBlack: 'Ich habe Schwarz',
  trainAllHint: '„Alle in dieser Reihenfolge trainieren“ gibt es für ein einzelnes Repertoire. Für alle markierten zusammen: „Trainings-Repertoire anlegen“ — das sammelt die wichtigsten Linien in einem Repertoire, das du dann trainierst.',
};
