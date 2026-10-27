import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MarkNotesRevision, MarkedPositionsService } from './marked-positions.service';
import { TranslatePipe } from '@ngx-translate/core';
import { KNOWN_MARK_TAGS, MAX_MARK_COMMENT_LENGTH, MAX_MARK_TAG_LENGTH, addMarkTag, knownMarkTag } from './mark-tags';

/**
 * Schlagworte + Kommentar einer „+"-Stellung bearbeiten (0.862.0, Wunsch 2026-10-27: „wenn ich auf das + klicke, sollte ich
 * Tags vergeben können, außerdem Kommentare dazu abgeben"). Feste Schlagworte als Umschalt-Chips, eigene Wörter über ein
 * Feld (Enter fügt hinzu), dazu ein Kommentar. Geteilt von RookHubs „+"-Knopf und LeagueHubs Stellungsfenster; speichern tut
 * der Aufrufer (`tags`/`comment` sind Zwei-Wege-Modelle).
 * Seit 0.862.1 (Wunsch 2026-10-27: „merk dir beim Bearbeiten auch die alten, damit ich ggf. Gelöschtes wiederherstellen
 * kann"): mit `fen` klappt „Frühere Fassungen" die aufgehobenen Fassungen auf; „Wiederherstellen" übernimmt eine in die
 * Felder (gespeichert wird erst mit „Speichern" — die jetzige Fassung wandert dabei selbst in den Verlauf).
 */
@Component({
  selector: 'app-mark-notes-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, DatePipe],
  template: `
    <div class="mn-label" id="mn-tags">{{ 'markPosition.tags' | translate }}</div>
    <div class="mn-chips" role="group" aria-labelledby="mn-tags">
      @for (t of known; track t) {
        <button type="button" class="mn-chip" [class.on]="has(t)" [attr.aria-pressed]="has(t)" (click)="toggle(t)">
          {{ 'markPosition.tag.' + t | translate }}</button>
      }
      @for (t of custom(); track t) {
        <button type="button" class="mn-chip on own" aria-pressed="true" (click)="toggle(t)"
                [attr.aria-label]="t + ' — ' + ('common.remove' | translate)">{{ t }} ×</button>
      }
      @for (t of suggested(); track t) {
        <button type="button" class="mn-chip other" aria-pressed="false" (click)="toggle(t)"
                [title]="'markPosition.fromOthers' | translate">{{ t }}</button>
      }
    </div>
    <div class="mn-own">
      <input type="text" class="mn-input" [maxLength]="maxTag" [value]="draft()" (input)="draft.set($any($event.target).value)"
             (keydown.enter)="$event.preventDefault(); addDraft()" [attr.aria-label]="'markPosition.ownTag' | translate"
             [placeholder]="'markPosition.ownTag' | translate" />
      <button type="button" class="mn-add" [disabled]="!draft().trim()" (click)="addDraft()">{{ 'common.add' | translate }}</button>
    </div>
    @if (others().length) {
      <div class="mn-label">{{ 'markPosition.othersComments' | translate }}</div>
      @for (c of others(); track $index) { <blockquote class="mn-other">{{ c }}</blockquote> }
    }
    <label class="mn-label" for="mn-comment">{{ 'markPosition.comment' | translate }}</label>
    <textarea id="mn-comment" class="mn-text" rows="3" [maxLength]="maxComment" [value]="comment()"
              (input)="comment.set($any($event.target).value)"></textarea>
    @if (fen()) {
      <button type="button" class="mn-hist-toggle" [attr.aria-expanded]="historyOpen()" (click)="toggleHistory()">
        {{ 'markPosition.history' | translate }} {{ historyOpen() ? '▴' : '▾' }}</button>
      @if (historyOpen()) {
        @if (history(); as list) {
          @if (!list.length) { <p class="mn-hist-empty">{{ 'markPosition.historyEmpty' | translate }}</p> }
          <ul class="mn-hist">
            @for (r of list; track r.id) {
              <li>
                <div class="mn-hist-head">
                  <span class="mn-hist-date">{{ r.createdAt | date:'short' }}</span>
                  <button type="button" class="mn-add" (click)="restore(r)">{{ 'markPosition.restore' | translate }}</button>
                </div>
                @if (r.tags.length) {
                  <div class="mn-hist-tags">@for (t of r.tags; track t) {<span class="mn-hist-tag">{{ label(t) | translate }}</span>}</div>
                }
                @if (r.comment) { <div class="mn-hist-comment">{{ r.comment }}</div> }
              </li>
            }
          </ul>
        } @else if (historyError()) {
          <p class="mn-hist-empty" role="alert">{{ 'common.loadFailed' | translate }}</p>
        }
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .mn-label { display: block; font-size: 0.8rem; font-weight: 600; opacity: 0.8; margin: 10px 0 4px; }
    .mn-chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .mn-chip { font: inherit; font-size: 0.82rem; padding: 3px 10px; border-radius: 999px; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 35%, transparent); background: transparent; color: inherit; }
    .mn-chip.on { background: var(--rh-success); border-color: var(--rh-success); color: var(--mat-sys-surface); }
    .mn-chip.other { border-style: dashed; }
    .mn-chip:focus-visible, .mn-add:focus-visible { outline: 2px solid currentColor; outline-offset: 1px; }
    .mn-own { display: flex; gap: 6px; margin-top: 8px; }
    .mn-input, .mn-text { font: inherit; color: inherit; background: transparent; box-sizing: border-box; width: 100%;
      border: 1px solid color-mix(in srgb, currentColor 35%, transparent); border-radius: 6px; padding: 6px 8px; }
    .mn-input { flex: 1; min-width: 0; }
    .mn-text { resize: vertical; min-height: 64px; }
    .mn-add { font: inherit; font-size: 0.85rem; padding: 0 12px; border-radius: 6px; cursor: pointer; color: inherit;
      border: 1px solid color-mix(in srgb, currentColor 35%, transparent); background: transparent; }
    .mn-add:disabled { opacity: 0.45; cursor: default; }
    .mn-hist-toggle { font: inherit; font-size: 0.85rem; margin-top: 10px; padding: 2px 0; border: 0; background: none;
      color: inherit; opacity: 0.8; cursor: pointer; }
    .mn-hist-toggle:focus-visible { outline: 2px solid currentColor; }
    .mn-hist { list-style: none; margin: 4px 0 0; padding: 0; display: grid; gap: 6px; max-height: 220px; overflow-y: auto; }
    .mn-hist li { border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 6px; padding: 6px 8px; }
    .mn-hist-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 0.8rem; }
    .mn-hist-date { opacity: 0.75; }
    .mn-hist-head .mn-add { padding: 2px 10px; }
    .mn-hist-tags { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px; }
    .mn-hist-tag { font-size: 0.75rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .mn-hist-comment { margin-top: 4px; font-size: 0.85rem; white-space: pre-wrap; }
    .mn-hist-empty { font-size: 0.85rem; opacity: 0.75; margin: 4px 0 0; }
    .mn-other { margin: 0 0 6px; padding: 4px 10px; border-left: 3px solid color-mix(in srgb, currentColor 30%, transparent);
      white-space: pre-wrap; font-size: 0.88rem; }
  `],
})
export class MarkNotesEditorComponent {
  /** Die eigenen Schlagworte (feste Codes oder eigene Wörter). */
  readonly tags = model<string[]>([]);
  /** Der eigene Kommentar. */
  readonly comment = model('');
  /** Schlagworte anderer (LeagueHub) — als gestrichelte Vorschläge, ein Klick übernimmt sie. */
  readonly otherTags = input<readonly string[]>([]);
  /** Bemerkungen anderer (LeagueHub, ohne Namen) — nur zum Lesen. */
  readonly others = input<readonly string[]>([]);
  /** Die Stellung — mit ihr gibt es „Frühere Fassungen" (0.862.1). */
  readonly fen = input<string | null>(null);

  private readonly service = inject(MarkedPositionsService);
  readonly historyOpen = signal(false);
  readonly history = signal<MarkNotesRevision[] | null>(null);
  readonly historyError = signal(false);

  readonly known = KNOWN_MARK_TAGS;
  readonly maxTag = MAX_MARK_TAG_LENGTH;
  readonly maxComment = MAX_MARK_COMMENT_LENGTH;
  readonly draft = signal('');

  /** Eigene Wörter (alles, was kein fester Code ist). */
  readonly custom = computed(() => this.tags().filter(t => !knownMarkTag(t)));
  /** Eigene Wörter anderer, die man selbst noch nicht hat (feste Codes stehen ohnehin als Chip da). */
  readonly suggested = computed(() => this.otherTags()
    .filter(t => !knownMarkTag(t) && !this.tags().some(o => o.toLowerCase() === t.toLowerCase())));

  constructor() {
    // Andere Stellung (LeagueHub ‹ ›) → Verlauf zu und neu laden
    effect(() => {
      this.fen();
      this.historyOpen.set(false);
      this.history.set(null);
    });
  }

  toggleHistory(): void {
    const fen = this.fen();
    if (!fen) return;
    this.historyOpen.set(!this.historyOpen());
    if (!this.historyOpen() || this.history()) return;
    this.historyError.set(false);
    this.service.history(fen).subscribe({
      next: list => { if (this.fen() === fen) this.history.set(list); },
      error: () => this.historyError.set(true),
    });
  }

  /** Eine frühere Fassung in die Felder übernehmen (noch nicht gespeichert). */
  restore(r: MarkNotesRevision): void {
    this.tags.set([...r.tags]);
    this.comment.set(r.comment ?? '');
  }

  /** Übersetzungsschlüssel eines festen Schlagworts, ein eigenes Wort bleibt (der Translate-Pipe gibt es unverändert zurück). */
  label(tag: string): string {
    const known = knownMarkTag(tag);
    return known ? 'markPosition.tag.' + known : tag;
  }

  has(tag: string): boolean {
    return this.tags().some(t => t.toLowerCase() === tag.toLowerCase());
  }

  toggle(tag: string): void {
    if (this.has(tag)) this.tags.set(this.tags().filter(t => t.toLowerCase() !== tag.toLowerCase()));
    else { const next = addMarkTag(this.tags(), tag); if (next) this.tags.set(next); }
  }

  addDraft(): void {
    const next = addMarkTag(this.tags(), this.draft());
    if (next) this.tags.set(next);
    this.draft.set('');
  }
}
