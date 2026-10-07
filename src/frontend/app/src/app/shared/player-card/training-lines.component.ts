import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TreeFilter } from '@lh/core/league.models';
import { HandoffService } from '@rh/core/handoff.service';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { readChapterColorOverrides } from '@rh/features/repertoire/repertoire-color.util';
import { PLAYER_CARD_API } from './player-card-api';
import { TRAINING_LINES_KEY, TrainingLine, TrainingLines, lineText, percent, trainingFilterParams } from './training-lines';

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
              <label>Repertoire
                <select [value]="d.repertoire ?? ''" (change)="pickRepertoire($any($event.target).value)" [disabled]="loading()">
                  @for (r of d.repertoires; track r.id) { <option [value]="r.id" [selected]="r.id === d.repertoire">{{ r.name }}</option> }
                </select>
              </label>
              @if (d.colors.length > 1) {
                <div class="seg" role="group" aria-label="Deine Farbe">
                  @for (c of d.colors; track c) {
                    <button type="button" [attr.aria-pressed]="d.color === c" [disabled]="loading()" (click)="pickColor(c)">
                      {{ c === 'w' ? 'Ich mit Weiß' : 'Ich mit Schwarz' }}</button>
                  }
                </div>
              }
            </div>
            <p class="muted small tl-basis" role="status">
              @if (d.color) { Du mit {{ d.color === 'w' ? 'Weiß' : 'Schwarz' }} — gezählt: {{ d.games }}
                {{ d.games === 1 ? 'Partie' : 'Partien' }} von {{ who() }} mit {{ d.color === 'w' ? 'Schwarz' : 'Weiß' }}. }
              @if (loading()) { Lade … }
            </p>
            @if (d.lines.length) {
              <div class="tl-actions">
                <button type="button" class="btn-pri" [disabled]="busy()" (click)="trainAll(d)">Alle in dieser Reihenfolge trainieren</button>
              </div>
              <ol class="tl-list">
                @for (l of d.lines; track l.key) {
                  <li [class.never]="l.neverReached">
                    <div class="tl-line">
                      <span class="tl-moves">{{ text(l) }}</span>
                      @if (l.chapter) { <span class="muted small tl-chapter">{{ l.chapter }}</span> }
                    </div>
                    <div class="tl-stats">
                      @if (l.neverReached) { <span class="tl-p muted">nie erreicht</span> }
                      @else {
                        <span class="tl-p" [title]="'Wahrscheinlichkeit, dass ' + who() + ' diese Linie spielt'">{{ pct(l.probability) }}</span>
                        <span class="muted small">{{ l.reached }} {{ l.reached === 1 ? 'Partie' : 'Partien' }}@if (l.lastYear) {, zuletzt {{ l.lastYear }}}</span>
                      }
                      <button type="button" class="btn-sec tl-train" [disabled]="busy()" (click)="train(d, l)"
                              [attr.aria-label]="'Linie ' + text(l) + ' trainieren'">Trainieren</button>
                    </div>
                  </li>
                }
              </ol>
              @if (d.more) { <p class="muted small">… und {{ d.more }} weitere {{ d.more === 1 ? 'Linie' : 'Linien' }} (im Trainer mit „Alle“ dabei).</p> }
            } @else if (!loading()) {
              <p class="muted small">Dieses Repertoire hat für diese Farbe keine Linien.</p>
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
  `],
})
export class TrainingLinesComponent {
  private readonly api = inject(PLAYER_CARD_API);
  private readonly router = inject(Router);
  private readonly handoff = inject(HandoffService);

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
  readonly who = computed(() => (this.name() || '').split(',')[0].trim() || 'diesen Gegner');
  private seq = 0;
  private loadedFor = '';

  readonly text = (l: TrainingLine) => lineText(l.moves, l.start);
  readonly pct = percent;

  toggle(): void {
    this.open.update(o => !o);
    if (this.open()) {
      const want = `${this.key()}|${JSON.stringify(this.filter())}`;
      if (want !== this.loadedFor) void this.load(this.remembered());
    }
  }

  pickRepertoire(value: string): void {
    const id = Number(value);
    if (!id) return;
    void this.load({ repertoire: id, color: null });
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
        chapterColors: want.repertoire ? readChapterColorOverrides(want.repertoire) : null,
      });
      if (my !== this.seq) return;
      this.data.set(d);
      this.loadedFor = `${key}|${JSON.stringify(filter)}`;
      // nur eine Bequemlichkeit — scheitert still (localStorage voll/gesperrt)
      writeJson(localStore(), TRAINING_LINES_KEY, { repertoire: d.repertoire, color: want.color ? d.color : null });
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

  /** Eine Linie im Trainer. */
  train(d: TrainingLines, l: TrainingLine): Promise<void> {
    return this.go(d, { line: l.key, chapter: l.chapter || null });
  }

  /** Alle Linien dieses Repertoires und dieser Farbe, in der Reihenfolge nach diesem Gegner. */
  trainAll(d: TrainingLines): Promise<void> {
    return this.go(d, {});
  }

  private async go(d: TrainingLines, extra: Record<string, string | null>): Promise<void> {
    if (d.repertoire === null) return;
    const params: Record<string, string> = {
      ...this.api.trainerParams?.(this.key()) ?? { opponent: `league:${this.key()}` },
      ...(d.color ? { color: d.color } : {}),
      ...trainingFilterParams(this.filter()),
    };
    for (const [k, v] of Object.entries(extra)) if (v) params[k] = v;
    const path = `repertoires/${d.repertoire}/train`;
    this.busy.set(true);
    try {
      if (this.handoff.rookHubUrl) await this.handoff.jumpToRookHub(`${path}?${new URLSearchParams(params).toString()}`);
      else await this.router.navigate(['/' + path], { queryParams: params });
    } finally {
      this.busy.set(false);
    }
  }
}
