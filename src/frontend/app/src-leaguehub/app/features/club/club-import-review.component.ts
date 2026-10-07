import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubImportResult, ImportGameDecision, RosterPerson } from '../../core/club.models';
import { reasonText } from '../../core/club-format';
import { ClubContextService } from '../../core/club-context.service';
import { de } from '../../core/league-format';
import { ImportReview, ReviewFilter, ReviewGame, ReviewSide, SideKey, included, needsLook, optionalGame, pairingText, quickPicks, reviewStatus } from './import-review';
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
        {{ review.counts().skip }} nicht@if (review.counts().unknown) {, bei {{ review.counts().unknown }} ist ein Spieler nicht (eindeutig) erkannt}.</p>
      @if (review.counts().optional) {
        <p class="small muted">{{ review.counts().optional }} ohne Gegner aus der Liga (nur in der Megabase) — nicht vorgewählt, anhaken nimmt sie trotzdem auf.</p>
      }
      @if (note()) { <p class="small ok-text" role="status">{{ note() }}</p> }
      @if (review.truncated) { <p class="err small">Es wurden nur die ersten 500 Partien gelesen — den Rest bitte in einem zweiten Upload.</p> }
      <div class="seg" role="group" aria-label="Anzeigen">
        @for (f of filters; track f.key) {
          <button type="button" [attr.aria-pressed]="review.filter() === f.key" (click)="review.filter.set(f.key)">
            {{ f.label }} ({{ review.filterCounts()[f.key] }})</button>
        }
      </div>
    </div>

    <div class="roster-scroll review-scroll">
      <table class="rtable review-table">
        <thead><tr><th><span class="sr">Importieren</span></th><th class="num">Nr.</th><th class="num">Jahr</th>
          <th>Turnier</th><th>Weiß</th><th>Schwarz</th><th class="num">Erg.</th><th>Status</th></tr></thead>
        <tbody>
          @for (r of review.visible(); track r.game.index) {
            <tr [class.off]="!isIn(r)">
              <td class="chk"><input type="checkbox" [checked]="isIn(r)" [disabled]="!status(r).importable"
                         [attr.aria-label]="'Partie ' + r.game.index + ' importieren'" (change)="review.toggleInclude(r.game.index)" /></td>
              <td class="num nr">{{ r.game.index }}</td>
              <td class="num yr">{{ r.game.year ?? '–' }}</td>
              <td class="small event" [attr.title]="r.game.event">{{ r.game.event ?? '' }}
                @if (!r.game.error && r.game.pairings?.length) {
                  <select class="pairing-pick" [attr.aria-label]="'Ligapaarung von Partie ' + r.game.index"
                          [class.set]="r.pairingId != null" (change)="pairing(r, $any($event.target).value)">
                    <option value="" [selected]="r.pairingId == null">keine Ligapartie</option>
                    @for (p of r.game.pairings; track p.id) {
                      <option [value]="p.id" [selected]="r.pairingId === p.id">{{ pairingText(p) }}</option>
                    }
                  </select>
                }</td>
              @for (k of sides; track k) {
                <td class="side" [class.white]="k === 'white'" [class.black]="k === 'black'" [attr.data-label]="k === 'white' ? 'Weiß' : 'Schwarz'">
                  <button type="button" class="side-btn" [disabled]="!!r.game.error" (click)="edit(r, k)"
                          [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ' korrigieren'">
                    <span [class.anon]="r[k].replace">{{ shown(r[k]) }}</span>
                    <span class="badge" [class.ok]="badge(r[k]).ok" [class.warn]="badge(r[k]).warn">{{ badge(r[k]).text }}</span>
                  </button>
                  @if (r[k].changed || (r[k].raw && r[k].name !== r[k].raw && !r[k].replace)) {
                    <span class="small muted raw">im PGN: {{ r[k].raw || '?' }}</span>
                  }
                  @if (!r.game.error && quickPicks(r[k]).length) {
                    <span class="quick-pick small"><span class="muted">Meintest du</span>
                      @for (c of quickPicks(r[k]); track c.name + (c.fide ?? '')) {
                        <button type="button" class="btn-chip" (click)="quick(r, k, c)"
                                [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ' in Partie ' + r.game.index + ': ' + c.name + ' übernehmen'"
                                [attr.title]="c.teams.join(', ')">{{ c.name }}</button>
                      }
                    </span>
                  }
                </td>
              }
              <td class="num res">{{ resultText(r.game.result) }}</td>
              <td class="small st">@if (isIn(r)) { <span class="ok-text">wird importiert</span> }
                @else if (status(r).importable) { <span class="muted">{{ optional(r) ? 'nicht in Liga — anhaken zum Hinzufügen' : 'abgewählt' }}</span> }
                @else { <span class="muted">{{ reason(status(r).reason!) }}</span> }</td>
            </tr>
            @if (editing(); as e) {
              @if (e.index === r.game.index) {
                <tr class="edit-row"><td colspan="8">
                  <div class="side-edit">
                    <p class="small"><b>{{ e.side === 'white' ? 'Weiß' : 'Schwarz' }} in Partie {{ r.game.index }}</b>
                      — im PGN: „{{ r[e.side].raw || '?' }}"@if (r.game.event) { · {{ r.game.event }} }@if (r.game.opening) { · {{ de(r.game.opening) }} }</p>
                    @if (r[e.side].candidates.length) {
                      <div class="cands-pick"><span class="small muted">Mehrere Ligaspieler heißen so:</span>
                        @for (c of r[e.side].candidates; track c.name + (c.fide ?? '')) {
                          <button type="button" class="btn-sec" (click)="pick(c)">{{ c.name }} <span class="small">{{ c.teams.join(', ') }}</span></button>
                        }
                      </div>
                    }
                    <div class="field-row">
                      <div class="field">Spieler
                        <lh-player-search [client]="client" [text]="e.text" [autoSearch]="true" [label]="(e.side === 'white' ? 'Weiß' : 'Schwarz') + ' in Partie ' + r.game.index"
                                          (textChange)="typed($event)" (picked)="pick($event)" (enter)="apply()" />
                      </div>
                      <label class="anon-inline">
                        <input type="checkbox" [checked]="r[e.side].replace" (change)="review.setReplace(r.game.index, e.side, $any($event.target).checked)" />
                        durch „{{ anon }}" ersetzen
                      </label>
                    </div>
                    @if (remembers) {
                      <p class="small muted">Gilt auch für alle anderen Partien mit „{{ r[e.side].raw || '?' }}" — und wird beim Import
                        gemerkt: das nächste Mal ordnet LeagueHub den Namen von selbst so zu.</p>
                    } @else {
                      <p class="small muted">Gilt auch für alle anderen Partien mit „{{ r[e.side].raw || '?' }}".</p>
                    }
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
        @if (importing()) { Importiere … @if (progress(); as pr) { {{ pr.done }} / {{ pr.total }} } }
        @else if (savedCount()) { Weiter importieren ({{ review.counts().take - savedCount() }} übrig) }
        @else { {{ review.counts().take }} Partien importieren }</button>
      <button type="button" class="btn-link" [disabled]="importing()" (click)="cancel.emit()">Schließen</button>
      <span class="update-msg" [class.err]="!!error()" role="status">{{ error() ?? '' }}</span>
    </div>
  `,
})
export class ClubImportReviewComponent {
  @Input({ required: true }) review!: ImportReview;
  @Input({ required: true }) client!: ClubClient;
  @Input({ required: true }) pgn!: string;
  /** Mit Konto werden Korrekturen als Namens-Zuordnung gemerkt (über einen Teilen-Link nicht). */
  @Input() remembers = true;
  /** Der Entwurf, zu dem die Liste gehört (0.595.0, angemeldet) — geht mit jeder Portion an den Server. */
  @Input() draftId: number | null = null;
  @Output() imported = new EventEmitter<ClubImportResult>();
  /** Nach jeder gespeicherten Portion: die Nummern ALLER in diesem Lauf gespeicherten Partien — die Seite legt sie im
   *  Entwurf ab, damit ein Abbruch nichts doppelt anfängt. */
  @Output() savedChange = new EventEmitter<number[]>();
  /** „Schließen": nur die Übersicht zu — ob und wie der Entwurf bleibt, entscheidet die Seite (er wird NICHT gelöscht). */
  @Output() cancel = new EventEmitter<void>();

  readonly sides: SideKey[] = ['white', 'black'];
  readonly filters: { key: ReviewFilter; label: string }[] = [
    { key: 'all', label: 'Alle' },
    { key: 'skipped', label: 'Nicht importiert' },
    { key: 'new', label: 'Noch nicht vorhanden' },
    { key: 'unknown', label: 'Nicht erkannt' },
  ];
  private readonly clubCtx = inject(ClubContextService);
  /** Der Name anonymisierter Spieler — der des Vereins (bzw. des Teilen-Links), 0.698.0. */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly de = de;
  readonly reason = reasonText;
  readonly status = reviewStatus;
  readonly isIn = included;
  readonly optional = optionalGame;

  readonly editing = signal<Editing | null>(null);
  readonly matching = signal(false);
  readonly editError = signal<string | null>(null);
  /** „3 weitere Partien mit „Kostic" ebenso zugeordnet." nach einer Korrektur. */
  readonly note = signal<string | null>(null);
  readonly importing = signal(false);
  readonly error = signal<string | null>(null);
  /** Fortschritt des portionsweisen Imports (0.590.0). */
  readonly progress = signal<{ done: number; total: number } | null>(null);
  /** Schon gespeicherte Partien dieser Übersicht — nach einem Abbruch geht „Weiter importieren" nur den Rest an. */
  readonly savedCount = signal(0);
  private readonly saved = new Set<number>();
  private acc: ClubImportResult | null = null;
  /** Partien je Anfrage: jede Portion wird sofort gespeichert, und 500 Partien bleiben so bei 50 Anfragen — unter den
   *  Drosseln (60 je Minute über einen Teilen-Link). */
  static readonly Portion = 10;
  /** Wartezeiten vor dem 2. und 3. Versuch einer Portion (ms); ein 429 wartet länger. */
  retryDelays = [2000, 5000];
  throttleDelay = 20_000;

  shown(s: ReviewSide): string {
    return s.replace ? this.anon : s.name || s.raw || '?';
  }

  badge(s: ReviewSide): { text: string; ok: boolean; warn: boolean } {
    if (s.replace) return { text: s.club ? `${this.anon}-Spieler` : s.owner ? 'du' : 'ersetzt', ok: false, warn: false };
    if (s.ambiguous) return { text: 'mehrdeutig', ok: false, warn: true };
    const kept = s.alias && !s.changed ? ' · gemerkt' : '';
    if (s.league && s.lastNameOnly) return { text: 'nur Nachname — prüfen', ok: false, warn: true };
    if (s.league) return { text: (s.club ? `${this.anon}, nicht ersetzt` : 'Ligaspieler') + kept, ok: !s.club, warn: s.club };
    if (s.mega) return { text: 'nicht in Liga' + kept, ok: false, warn: false };
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

  readonly quickPicks = quickPicks;
  readonly pairingText = pairingText;

  /** Ligapaarung gewählt (0.678.0) — setzt die noch nicht angefassten Spieler aus der Paarung. */
  pairing(r: ReviewGame, value: string): void {
    this.review.setPairing(r.game.index, value ? Number(value) : null);
  }

  /** „Meintest du …" in der Zeile: denselben Weg wie ein gewählter Ligaspieler, ohne das Feld zu öffnen (0.596.0). */
  quick(r: ReviewGame, side: SideKey, p: RosterPerson): void {
    const raw = r[side].raw ?? '';
    this.said(this.review.choosePerson(r.game.index, side, p), raw);
    if (this.editing()?.index === r.game.index && this.editing()?.side === side) this.editing.set(null);
  }

  pick(p: RosterPerson): void {
    const e = this.editing();
    if (!e) return;
    const raw = this.rawOf(e);
    this.said(this.review.choosePerson(e.index, e.side, p), raw);
    this.editing.set(null);
  }

  private rawOf(e: Editing): string {
    return this.review.games().find(g => g.game.index === e.index)?.[e.side].raw ?? '';
  }

  private said(n: number, raw: string): void {
    this.note.set(n ? `${n === 1 ? '1 weitere Seite' : n + ' weitere Seiten'} mit „${raw}“ ebenso zugeordnet.` : null);
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
      this.said(this.review.setTyped(e.index, e.side, text, m.white), this.rawOf(e));
      this.editing.set(null);
    } catch {
      this.editError.set('Abgleich hat nicht geklappt.');
    } finally {
      this.matching.set(false);
    }
  }

  /**
   * Importieren — Portion für Portion (0.590.0, Wunsch: „damit Progress nicht verloren geht"): je
   * {@link ClubImportReviewComponent.Portion} Partien eine Anfrage mit genau deren PGN-Text (aus der Übersicht), jede
   * sofort gespeichert. Reißt eine Portion ab, wird sie zweimal wiederholt — war sie schon gespeichert und nur die Antwort
   * verloren, erkennt der Server sie als doppelt. Scheitert es endgültig, bleibt das Gespeicherte gespeichert, und der
   * Knopf macht beim Rest weiter. Kennt der Server den Text je Partie nicht (älterer Stand), geht alles in einer Anfrage.
   */
  async run(): Promise<void> {
    const decisions = this.review.decisions().filter(d => !this.saved.has(d.index));
    const texts = new Map(this.review.games().map(r => [r.game.index, r.game.pgn ?? null]));
    this.importing.set(true);
    this.error.set(null);
    try {
      if (decisions.some(d => !texts.get(d.index))) {
        this.imported.emit(await this.send(this.pgn, decisions));
        return;
      }
      const acc = this.acc ??= { added: 0, duplicates: 0, anonymized: 0, remembered: 0, truncated: false, ids: [], failed: [] };
      const total = this.saved.size + decisions.length;
      this.progress.set({ done: this.saved.size, total });
      for (let i = 0; i < decisions.length; i += ClubImportReviewComponent.Portion) {
        const part = decisions.slice(i, i + ClubImportReviewComponent.Portion);
        const r = await this.withRetry(() => this.send(
          part.map(d => texts.get(d.index)!).join('\n'), part.map((d, k) => ({ ...d, index: k + 1 }))));
        acc.added += r.added;
        acc.duplicates += r.duplicates;
        acc.anonymized += r.anonymized;
        acc.remembered = (acc.remembered ?? 0) + (r.remembered ?? 0);
        acc.ids.push(...r.ids);
        acc.failed.push(...r.failed.map(f => ({ ...f, index: part[f.index - 1]?.index ?? f.index })));
        for (const d of part) this.saved.add(d.index);
        this.savedCount.set(this.saved.size);
        this.savedChange.emit([...this.saved]);
        this.progress.set({ done: this.saved.size, total });
      }
      this.acc = null;
      this.saved.clear();
      this.savedCount.set(0);
      this.imported.emit(acc);
    } catch {
      this.error.set(this.saved.size
        ? `${this.saved.size} Partien sind gespeichert, dann ist die Verbindung abgerissen — „Weiter importieren" macht beim Rest weiter.`
        : 'Importieren hat nicht geklappt.');
    } finally {
      this.importing.set(false);
      this.progress.set(null);
    }
  }

  /** Mit Entwurf geht seine Nummer mit (ein Verwalter, der fertigstellt, handelt für den Einreicher). */
  private send(pgn: string, games: ImportGameDecision[]): Promise<ClubImportResult> {
    return this.draftId != null ? this.client.importPgn(pgn, games, this.draftId) : this.client.importPgn(pgn, games);
  }

  /** Eine Portion, bei Netz-/Serverfehlern bis zu zweimal wiederholt (429: länger warten). Eine Absage (400) nicht. */
  private async withRetry(send: () => Promise<ClubImportResult>): Promise<ClubImportResult> {
    for (let attempt = 0; ; attempt++) {
      try {
        return await send();
      } catch (e) {
        const status = e instanceof HttpErrorResponse ? e.status : 0;
        const retryable = status === 0 || status === 429 || status >= 500;
        if (!retryable || attempt >= this.retryDelays.length) throw e;
        await new Promise(r => setTimeout(r, status === 429 ? this.throttleDelay : this.retryDelays[attempt]));
      }
    }
  }
}
