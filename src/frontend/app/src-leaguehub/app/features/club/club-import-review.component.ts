import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { ClubClient } from '../../core/club-api.service';
import { ClubImportResult, RosterPerson } from '../../core/club.models';
import { ANON_NAME, reasonText } from '../../core/club-format';
import { de } from '../../core/league-format';
import { ImportReview, ReviewGame, ReviewSide, SideKey, included, needsLook, reviewStatus } from './import-review';
import { PlayerSearchComponent } from './player-search.component';

interface Editing { index: number; side: SideKey; text: string }

/**
 * Übersicht vor dem PGN-Import: je Partie wer gegen wen, ob sie übernommen wird (und warum nicht), unerkannte oder
 * falsch erkannte Spieler korrigieren, „durch Schwaz ersetzen" je Seite umschalten, einzelne Partien abwählen. Erst
 * „Importieren" speichert. Der Zustand liegt in {@link ImportReview} (rein, getestet).
 */
@Component({
  selector: 'lh-club-import-review',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PlayerSearchComponent],
  template: `
    <div class="review-head">
      <p><b>{{ review.counts().total }} Partien gelesen</b> — {{ review.counts().take }} werden importiert,
        {{ review.counts().skip }} nicht@if (review.counts().unknown) {, bei {{ review.counts().unknown }} ist ein Spieler nicht (eindeutig) erkannt }.</p>
      @if (review.truncated) { <p class="err small">Es wurden nur die ersten 500 Partien gelesen — den Rest bitte in einem zweiten Upload.</p> }
      <div class="seg" role="group" aria-label="Anzeigen">
        <button type="button" [attr.aria-pressed]="review.filter() === 'all'" (click)="review.filter.set('all')">Alle</button>
        <button type="button" [attr.aria-pressed]="review.filter() === 'skipped'" (click)="review.filter.set('skipped')">Nicht importiert</button>
        <button type="button" [attr.aria-pressed]="review.filter() === 'unknown'" (click)="review.filter.set('unknown')">Nicht erkannt</button>
      </div>
    </div>

    <div class="roster-scroll">
      <table class="rtable review-table">
        <thead><tr><th><span class="sr">Importieren</span></th><th class="num">Nr.</th><th class="num">Jahr</th>
          <th>Weiß</th><th>Schwarz</th><th class="num">Erg.</th><th>Status</th></tr></thead>
        <tbody>
          @for (r of review.visible(); track r.game.index) {
            <tr [class.off]="!isIn(r)">
              <td><input type="checkbox" [checked]="isIn(r)" [disabled]="!status(r).importable"
                         [attr.aria-label]="'Partie ' + r.game.index + ' importieren'" (change)="review.toggleInclude(r.game.index)" /></td>
              <td class="num">{{ r.game.index }}</td>
              <td class="num">{{ r.game.year ?? '–' }}</td>
              @for (k of sides; track k) {
                <td>
                  <button type="button" class="side-btn" [disabled]="!!r.game.error" (click)="edit(r, k)"
                          [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ' korrigieren'">
                    <span [class.anon]="r[k].replace">{{ shown(r[k]) }}</span>
                    <span class="badge" [class.ok]="badge(r[k]).ok" [class.warn]="badge(r[k]).warn">{{ badge(r[k]).text }}</span>
                  </button>
                  @if (r[k].changed || (r[k].raw && r[k].name !== r[k].raw && !r[k].replace)) {
                    <span class="small muted raw">im PGN: {{ r[k].raw || '?' }}</span>
                  }
                </td>
              }
              <td class="num">{{ resultText(r.game.result) }}</td>
              <td class="small">@if (isIn(r)) { <span class="ok-text">wird importiert</span> }
                @else if (status(r).importable) { <span class="muted">abgewählt</span> }
                @else { <span class="muted">{{ reason(status(r).reason!) }}</span> }</td>
            </tr>
            @if (editing(); as e) {
              @if (e.index === r.game.index) {
                <tr class="edit-row"><td colspan="7">
                  <div class="side-edit">
                    <p class="small"><b>{{ e.side === 'white' ? 'Weiß' : 'Schwarz' }} in Partie {{ r.game.index }}</b>
                      — im PGN: „{{ r[e.side].raw || '?' }}"@if (r.game.opening) { · {{ de(r.game.opening) }} }</p>
                    @if (r[e.side].candidates.length) {
                      <div class="cands-pick"><span class="small muted">Mehrere Ligaspieler heißen so:</span>
                        @for (c of r[e.side].candidates; track c.name + (c.fide ?? '')) {
                          <button type="button" class="btn-sec" (click)="pick(c)">{{ c.name }} <span class="small">{{ c.teams.join(', ') }}</span></button>
                        }
                      </div>
                    }
                    <div class="field-row">
                      <div class="field">Spieler
                        <lh-player-search [client]="client" [text]="e.text" [label]="(e.side === 'white' ? 'Weiß' : 'Schwarz') + ' in Partie ' + r.game.index"
                                          (textChange)="typed($event)" (picked)="pick($event)" (enter)="apply()" />
                      </div>
                      <label class="anon-inline">
                        <input type="checkbox" [checked]="r[e.side].replace" (change)="review.setReplace(r.game.index, e.side, $any($event.target).checked)" />
                        durch „{{ anon }}" ersetzen
                      </label>
                    </div>
                    <div class="actions">
                      <button type="button" class="btn-sec" [disabled]="matching()" (click)="apply()">Getippten Namen übernehmen</button>
                      <button type="button" class="btn-link" (click)="editing.set(null)">Schließen</button>
                      @if (editError()) { <span class="err small">{{ editError() }}</span> }
                    </div>
                  </div>
                </td></tr>
              }
            }
          }
        </tbody>
      </table>
    </div>

    <div class="actions">
      <button type="button" class="btn-pri" [disabled]="importing() || !review.counts().take" (click)="run()">
        {{ importing() ? 'Importiere …' : review.counts().take + ' Partien importieren' }}</button>
      <button type="button" class="btn-link" [disabled]="importing()" (click)="cancel.emit()">Verwerfen</button>
      <span class="update-msg" [class.err]="!!error()" role="status">{{ error() ?? '' }}</span>
    </div>
  `,
})
export class ClubImportReviewComponent {
  @Input({ required: true }) review!: ImportReview;
  @Input({ required: true }) client!: ClubClient;
  @Input({ required: true }) pgn!: string;
  @Output() imported = new EventEmitter<ClubImportResult>();
  @Output() cancel = new EventEmitter<void>();

  readonly sides: SideKey[] = ['white', 'black'];
  readonly anon = ANON_NAME;
  readonly de = de;
  readonly reason = reasonText;
  readonly status = reviewStatus;
  readonly isIn = included;

  readonly editing = signal<Editing | null>(null);
  readonly matching = signal(false);
  readonly editError = signal<string | null>(null);
  readonly importing = signal(false);
  readonly error = signal<string | null>(null);

  shown(s: ReviewSide): string {
    return s.replace ? ANON_NAME : s.name || s.raw || '?';
  }

  badge(s: ReviewSide): { text: string; ok: boolean; warn: boolean } {
    if (s.replace) return { text: s.club ? 'Schwaz-Spieler' : s.owner ? 'du' : 'ersetzt', ok: false, warn: false };
    if (s.ambiguous) return { text: 'mehrdeutig', ok: false, warn: true };
    if (s.league) return { text: s.club ? 'Schwaz, nicht ersetzt' : 'Ligaspieler', ok: !s.club, warn: s.club };
    return { text: needsLook(s) ? 'nicht erkannt' : '', ok: false, warn: true };
  }

  resultText(r: string): string {
    return r === '1/2-1/2' ? '½–½' : r === '*' ? '–' : r.replace('-', '–');
  }

  edit(r: ReviewGame, side: SideKey): void {
    const cur = this.editing();
    if (cur && cur.index === r.game.index && cur.side === side) { this.editing.set(null); return; }
    this.editing.set({ index: r.game.index, side, text: r[side].name ?? r[side].raw ?? '' });
    this.editError.set(null);
  }

  typed(text: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, text });
  }

  pick(p: RosterPerson): void {
    const e = this.editing();
    if (!e) return;
    this.review.choosePerson(e.index, e.side, p);
    this.editing.set(null);
  }

  /** Den getippten Namen übernehmen und neu abgleichen lassen (ein Treffer der Suche geht direkt über `pick`). */
  async apply(): Promise<void> {
    const e = this.editing();
    if (!e) return;
    const text = e.text.trim();
    if (!text) { this.editError.set('Bitte einen Namen eingeben.'); return; }
    this.matching.set(true);
    try {
      const m = await this.client.match(text, '');
      this.review.setTyped(e.index, e.side, text, m.white);
      this.editing.set(null);
    } catch {
      this.editError.set('Abgleich hat nicht geklappt.');
    } finally {
      this.matching.set(false);
    }
  }

  async run(): Promise<void> {
    this.importing.set(true);
    this.error.set(null);
    try {
      this.imported.emit(await this.client.importPgn(this.pgn, this.review.decisions()));
    } catch {
      this.error.set('Importieren hat nicht geklappt.');
    } finally {
      this.importing.set(false);
    }
  }
}
