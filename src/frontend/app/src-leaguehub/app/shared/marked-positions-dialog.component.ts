import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { MarkNotesEditorComponent } from '@rh/shared/mark-position/mark-notes-editor.component';
import { ReplayMark } from '@rh/shared/player-card/game-replay.component';
import { LineupsApiService, ReplayStart } from '../core/lineups';

/** Eine Partie, deren Stellungen das Fenster zeigt. */
export interface MarkedPositionsGame {
  clubGameId: number;
  /** „Weiß – Schwarz" bzw. „Runde 3 · Brett 2: …", als kleine Zeile unter dem Titel. */
  subtitle?: string;
  /** Brett aus Sicht von Schwarz (eigener Spieler hatte Schwarz). */
  flipped?: boolean;
}

/** Eine Partie (Abzeichen an der Partie) oder viele (0.862.0, „+n" je Runde/Liga der Übersicht) — in dieser Reihenfolge. */
export interface MarkedPositionsDialogData {
  games: MarkedPositionsGame[];
}

/** „In der Partie zeigen" → Partie und Stellung, zu der das Nachspielen springen soll; sonst `undefined`. */
export type MarkedPositionsDialogResult = { clubGameId: number; mark: ReplayMark } | undefined;

interface Item { game: MarkedPositionsGame; mark: ReplayMark; }

/**
 * Die interessanten Stellungen einer Vereinspartie als Fenster (0.862.0, Wunsch 2026-10-27: „wenn man auf das + klickt, nur das
 * Diagramm sehen — wer am Zug + Klassifizierung + Bemerkung, hier änderbar/erweiterbar; oben mit ‹ › durch die Stellungen;
 * Überschrift immer ‚Interessante Stellung aus Partie/aus Variante'"). Speichern markiert die Stellung dabei auch für den
 * Betrachter selbst (`PUT …/marks/notes`). „In der Partie zeigen" schließt mit der Stellung — die Seite klappt dann das
 * Nachspielen auf wie bisher. Seit dem Nachtrag (Wunsch 2026-10-27: „in der Übersicht je Runde und je Liga eine Anzeige +30,
 * Klick bringt diese 30 Stellungen, jeweils mit ‚in der Partie zeigen'") auch über mehrere Partien hinweg.
 */
@Component({
  selector: 'lh-marked-positions-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, ChessBoardComponent, MarkNotesEditorComponent],
  template: `
    <div class="mp-head">
      <button type="button" class="mp-nav" (click)="go(-1)" [disabled]="index() === 0" aria-label="Vorige Stellung">‹</button>
      <h2 mat-dialog-title class="mp-title">{{ mark()?.ply == null && mark() ? 'Interessante Stellung aus Variante' : 'Interessante Stellung aus Partie' }}</h2>
      <button type="button" class="mp-nav" (click)="go(1)" [disabled]="index() >= items().length - 1" aria-label="Nächste Stellung">›</button>
    </div>
    <mat-dialog-content>
      @if (current()?.game?.subtitle; as sub) { <div class="mp-sub">{{ sub }}</div> }
      @if (loading()) {
        <p class="mp-msg">Lädt …</p>
      } @else if (loadError()) {
        <p class="mp-msg err" role="alert">Die Stellungen konnten nicht geladen werden.</p>
      } @else if (mark(); as m) {
        <div class="mp-meta">
          <span class="mp-turn"><span class="mp-dot" [class.black]="!whiteToMove()" aria-hidden="true"></span>{{ whiteToMove() ? 'Weiß am Zug' : 'Schwarz am Zug' }}</span>
          <span class="mp-where">{{ where() }}</span>
          @if (items().length > 1) { <span class="mp-count">{{ index() + 1 }} / {{ items().length }}</span> }
        </div>
        <div class="mp-board"><app-chess-board [fen]="m.fen" [flipped]="!!current()!.game.flipped" /></div>
        @for (m2 of [m]; track current()!.game.clubGameId + ':' + m2.positionKey) {
          <app-mark-notes-editor [(tags)]="tags" [(comment)]="comment" [otherTags]="m2.tags ?? []" [others]="m2.comments ?? []" [fen]="m2.fen"
                                 [(editing)]="editing" />
        }
        @if (saveError()) { <p class="mp-msg err" role="alert">{{ saveError() }}</p> }
        @if (saved()) { <p class="mp-msg ok" role="status">Gespeichert.</p> }
      } @else {
        <p class="mp-msg">Keine interessanten Stellungen.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      @if (current(); as it) { <button type="button" class="btn-link" (click)="show(it)">In der Partie zeigen</button> }
      <span class="spacer"></span>
      <button type="button" class="btn-link" mat-dialog-close>Schließen</button>
      @if (current() && editing()) { <button type="button" class="btn-sec mp-save" [disabled]="busy() || !dirty()" (click)="save()">Speichern</button> }
    </mat-dialog-actions>
  `,
  styles: [`
    .mp-head { display: flex; align-items: center; gap: 4px; padding: 0 12px; }
    .mp-title { flex: 1; text-align: center; margin: 0; padding: 16px 0 8px; font-size: 1.1rem; }
    .mp-nav { width: 36px; height: 36px; border-radius: 50%; border: 0; background: transparent; color: inherit; cursor: pointer;
      font-size: 1.6rem; line-height: 1; }
    .mp-nav:disabled { opacity: 0.3; cursor: default; }
    .mp-nav:not(:disabled):hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .mp-nav:focus-visible { outline: 2px solid currentColor; }
    .mp-sub { font-size: 0.85rem; opacity: 0.75; text-align: center; margin-bottom: 6px; }
    .mp-meta { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; font-size: 0.88rem; margin-bottom: 6px; }
    .mp-turn { display: inline-flex; align-items: center; gap: 6px; font-weight: 600; }
    .mp-dot { width: 12px; height: 12px; border-radius: 50%; background: var(--paper); border: 1.5px solid var(--ink); box-sizing: border-box; }
    .mp-dot.black { background: var(--ink); }
    .mp-where { opacity: 0.75; }
    .mp-count { margin-left: auto; opacity: 0.75; }
    .mp-board { width: min(360px, 100%); aspect-ratio: 1; margin: 0 auto; }
    .mp-msg { margin: 8px 0; }
    .mp-msg.err { color: var(--red); }
    .spacer { flex: 1; }
  `],
})
export class MarkedPositionsDialogComponent {
  readonly data = inject<MarkedPositionsDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<MarkedPositionsDialogComponent, MarkedPositionsDialogResult>>(MatDialogRef);
  private readonly api = inject(LineupsApiService);

  readonly items = signal<Item[]>([]);
  readonly index = signal(0);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saved = signal(false);
  readonly tags = signal<string[]>([]);
  readonly comment = signal('');
  /** Lesen oder Bearbeiten (0.865.0): je Stellung erst lesen, der Bleistift im Editor schaltet um; nach dem Speichern wieder lesen. */
  readonly editing = signal(false);

  readonly current = computed<Item | null>(() => this.items()[this.index()] ?? null);
  readonly mark = computed(() => this.current()?.mark ?? null);
  readonly whiteToMove = computed(() => (this.mark()?.fen.split(' ')[1] ?? 'w') !== 'b');
  /** „nach 12. …" (Hauptlinie) bzw. „Variante ab Zug 9" — Zugnummern aus den Halbzügen. */
  readonly where = computed(() => {
    const m = this.mark();
    if (!m) return '';
    if (m.ply != null) return m.ply === 0 ? 'Ausgangsstellung' : `nach Zug ${Math.ceil(m.ply / 2)}${m.ply % 2 ? '' : ' …'}`;
    return m.variationFromPly != null ? `Variante ab Zug ${Math.floor(m.variationFromPly / 2) + 1}` : 'Variante';
  });
  readonly dirty = computed(() => {
    const m = this.mark();
    if (!m) return false;
    return JSON.stringify(this.tags()) !== JSON.stringify(m.myTags ?? []) || this.comment().trim() !== (m.myComment ?? '');
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const lists = await Promise.all(this.data.games.map(g => this.api.clubGameMarks(g.clubGameId)));
      this.items.set(this.data.games.flatMap((game, i) => (lists[i] ?? []).map(mark => ({ game, mark }))));
      this.pick(0);
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  private pick(i: number): void {
    this.index.set(i);
    const m = this.items()[i]?.mark;
    this.tags.set([...(m?.myTags ?? [])]);
    this.comment.set(m?.myComment ?? '');
    this.editing.set(false);
    this.saveError.set(null);
    this.saved.set(false);
  }

  go(step: number): void {
    const i = this.index() + step;
    if (i >= 0 && i < this.items().length) this.pick(i);
  }

  async save(): Promise<void> {
    const it = this.current();
    if (!it) return;
    this.busy.set(true);
    this.saveError.set(null);
    try {
      const id = it.game.clubGameId;
      const list = await this.api.setClubGameMarkNotes(id, it.mark.fen, this.tags(), this.comment().trim() || null);
      // Nur die Stellungen dieser Partie auffrischen — Reihenfolge und Platz bleiben.
      this.items.update(items => items.map(x => x.game.clubGameId !== id ? x
        : { ...x, mark: list.find(n => n.positionKey === x.mark.positionKey) ?? x.mark }));
      this.pick(this.index());
      this.saved.set(true);
    } catch {
      this.saveError.set('Speichern ging gerade nicht — bitte noch einmal.');
    } finally {
      this.busy.set(false);
    }
  }

  show(it: Item): void {
    this.ref.close({ clubGameId: it.game.clubGameId, mark: it.mark });
  }
}

/**
 * Das Fenster öffnen; schließt es mit „In der Partie zeigen", liefert das Promise den Einstieg fürs Nachspielen (Halbzug der
 * Hauptlinie, sonst „erste Marke"), sonst `null`.
 */
export async function openMarkedPositions(dialog: MatDialog, games: MarkedPositionsGame[]):
    Promise<{ clubGameId: number; start: ReplayStart } | null> {
  const ref = dialog.open<MarkedPositionsDialogComponent, MarkedPositionsDialogData, MarkedPositionsDialogResult>(
    MarkedPositionsDialogComponent, { data: { games }, width: '440px', maxWidth: '94vw', autoFocus: 'dialog' });
  const r = await firstValueFrom(ref.afterClosed());
  if (!r) return null;
  return { clubGameId: r.clubGameId, start: r.mark.ply != null ? { kind: 'ply', ply: r.mark.ply } : { kind: 'mark' } };
}
