import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { SimilarityPreset, SimilarPositionMatch } from '../../core/repertoire.service';
import { longMoveLabel } from './similar-move.util';

/** Ein auswählbares Repertoire im Filter (nur, was die Auswahl braucht). */
export interface SimilarRepertoireOption { id: number; name: string; }

/** Die vier Komponenten der Metrik, in fester Reihenfolge — Anzeige-Namen kommen aus i18n. */
export const SIMILAR_PARTS = ['pawns', 'material', 'pieces', 'king'] as const;
export type SimilarPart = (typeof SIMILAR_PARTS)[number];

/** Die drei Voreinstellungen in Anzeige-Reihenfolge (Wire-Werte, siehe `SimilarityPreset`). */
export const SIMILAR_PRESETS: SimilarityPreset[] = ['struktur', 'ausgewogen', 'stellungsbild'];

/**
 * „Ähnliche Stellungen" — die dritte Sicht des Panels „Stellung in meinen Repertoires"
 * (`PositionRepertoiresComponent`), rein darstellend: Filter oben, Trefferliste darunter.
 *
 * Wie `PositionTreeComponent` hält diese Komponente KEINEN Zustand und ruft keinen Service —
 * Laden, Fehler und Navigation bleiben beim Panel. Sie zeigt aber bewusst auch bei null Treffern
 * die Filter an (sonst käme man aus einer zu strengen Voreinstellung nicht mehr heraus).
 *
 * Die Aufschlüsselung (Struktur/Material/Figuren/König) steht offen an jedem Treffer, nicht in
 * einem Tooltip: erst sie erklärt, WARUM zwei Stellungen als ähnlich gelten — ein Treffer mit
 * 90 % Struktur und 20 % Figuren ist ein anderes Argument als umgekehrt.
 *
 * Dasselbe gilt für den optionalen Zug: passt er an einem Treffer, steht dort der dort gespielte
 * Zug UND beide Zahlen (Stellungswert und Endwert nach Bonus) — sonst wäre nicht erkennbar, warum
 * ein Treffer mit magerer Struktur oben steht. Die SAN-Eingabe selbst wird nicht hier ausgewertet
 * (dafür bräuchte es die Ankerstellung): das Feld meldet nur Text nach oben und zeigt an, was das
 * Panel als Ergebnis zurückgibt.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-similar-positions',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="sp-filters">
      <div class="sp-row">
        <span class="sp-label">{{ 'positionInReps.similar.preset' | translate }}</span>
        @for (p of presets; track p) {
          <button type="button" class="sp-chip" [class.sp-chip-on]="preset === p" (click)="choosePreset(p)"
                  [matTooltip]="'positionInReps.similar.presetHint.' + p | translate">
            {{ 'positionInReps.similar.presets.' + p | translate }}
          </button>
        }
      </div>
      <div class="sp-hint">{{ 'positionInReps.similar.presetHint.' + preset | translate }}</div>

      <div class="sp-row">
        <span class="sp-label">{{ 'positionInReps.similar.move' | translate }}</span>
        <input class="sp-move-input" type="text" autocomplete="off" spellcheck="false"
               [class.sp-move-bad]="moveInvalid"
               [value]="moveText"
               (input)="onMoveInput($event)"
               [attr.aria-label]="'positionInReps.similar.move' | translate"
               [attr.aria-invalid]="moveInvalid ? 'true' : null"
               [placeholder]="'positionInReps.similar.movePlaceholder' | translate"
               [matTooltip]="'positionInReps.similar.moveHint' | translate" />
        @if (moveSan) { <span class="sp-move-ok">{{ moveSan }}</span> }
      </div>
      @if (moveInvalid) {
        <div class="sp-move-error">{{ 'positionInReps.similar.moveIllegal' | translate }}</div>
      } @else {
        <div class="sp-hint">{{ 'positionInReps.similar.moveHint' | translate }}</div>
      }

      <div class="sp-row">
        <button type="button" class="sp-chip" [class.sp-chip-on]="includeMirrored"
                (click)="mirroredChange.emit(!includeMirrored)"
                [matTooltip]="'positionInReps.similar.mirroredHint' | translate">
          <mat-icon>swap_horiz</mat-icon>{{ 'positionInReps.similar.mirrored' | translate }}
        </button>
        <button type="button" class="sp-chip" [class.sp-chip-on]="sameSideToMove"
                (click)="sameSideToMoveChange.emit(!sameSideToMove)"
                [matTooltip]="'positionInReps.similar.sameSideHint' | translate">
          <mat-icon>swap_vert</mat-icon>{{ 'positionInReps.similar.sameSide' | translate }}
        </button>
        <!-- Der Zwang ergibt nur Sinn, solange es einen gültigen Zug gibt. -->
        @if (moveSan) {
          <button type="button" class="sp-chip" [class.sp-chip-on]="onlyWithMove"
                  (click)="onlyWithMoveChange.emit(!onlyWithMove)"
                  [matTooltip]="'positionInReps.similar.onlyWithMoveHint' | translate">
            <mat-icon>filter_alt</mat-icon>{{ 'positionInReps.similar.onlyWithMove' | translate:{ move: moveSan } }}
          </button>
        }
      </div>

      @if (options.length) {
        <div class="sp-row">
          <span class="sp-label">{{ 'positionInReps.similar.repertoires' | translate }}</span>
          <button type="button" class="sp-link" (click)="selectAll()">{{ 'positionInReps.similar.selectAll' | translate }}</button>
          <button type="button" class="sp-link" (click)="selectNone()">{{ 'positionInReps.similar.selectNone' | translate }}</button>
        </div>
        <div class="sp-row sp-reps">
          @for (o of options; track o.id) {
            <button type="button" class="sp-chip" [class.sp-chip-on]="isSelected(o.id)" (click)="toggle(o.id)" [matTooltip]="o.name">
              <mat-icon>{{ isSelected(o.id) ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
              <span class="sp-rep-name">{{ o.name }}</span>
            </button>
          }
        </div>
      }
    </div>

    @if (options.length && selectedCount === 0) {
      <div class="sp-muted">{{ 'positionInReps.similar.noneSelected' | translate }}</div>
    } @else if (matches.length === 0) {
      @if (moveSan && onlyWithMove) {
        <div class="sp-muted">{{ 'positionInReps.similar.noneWithMove' | translate:{ move: moveSan } }}</div>
      } @else {
        <div class="sp-muted">{{ 'positionInReps.similar.none' | translate }}</div>
      }
    } @else {
      <div class="sp-count">{{ 'positionInReps.similar.foundCount' | translate:{ count: matches.length } }}</div>
      <!-- $index im Schlüssel: zwei Treffer derselben Linie/Stellung dürfen die Liste nicht sprengen. -->
      @for (m of matches; track m.repertoireId + ':' + m.gameIndex + ':' + m.ply + ':' + $index) {
        <button type="button" class="sp-match" (click)="open.emit(m)"
                [matTooltip]="'positionInReps.similar.openHint' | translate">
          <span class="sp-scores">
            <span class="sp-score" [class.sp-score-hi]="round(m.score) >= 80">{{ round(m.score) }}</span>
            <!-- Beide Zahlen: der Endwert oben, der reine Stellungswert darunter — aber nur, wenn
                 der Zug-Bonus sie überhaupt auseinandergezogen hat. -->
            @if (hasBonus(m)) {
              <span class="sp-score-base" [matTooltip]="'positionInReps.similar.positionScoreHint' | translate">
                {{ 'positionInReps.similar.positionScore' | translate:{ score: round(m.positionScore) } }}
              </span>
            }
          </span>
          <span class="sp-body">
            <span class="sp-where">
              <span class="sp-rep">{{ m.repertoireName }}</span>
              @if (m.mirrored) {
                <span class="sp-mirror" [matTooltip]="'positionInReps.similar.mirroredBadge' | translate">⇄</span>
              }
            </span>
            <span class="sp-where sp-sub">
              @if (m.chapter) { <span class="sp-chapter">{{ m.chapter }}</span><span class="sp-dot">·</span> }
              <span class="sp-line">{{ m.lineName || ('positionInReps.unnamedLine' | translate) }}</span>
              <span class="sp-dot">·</span>
              @if (m.ply > 0) {
                <span class="sp-move">{{ 'positionInReps.similar.atMove' | translate:{ move: moveLabel(m) } }}</span>
              } @else {
                <span class="sp-move">{{ 'positionInReps.similar.startPosition' | translate }}</span>
              }
            </span>
            @if (m.moveMatch) {
              <span class="sp-movehit" [class.sp-movehit-weak]="m.moveMatch === 'sameTarget'">
                {{ (m.moveMatch === 'exact'
                      ? 'positionInReps.similar.moveHitExact'
                      : 'positionInReps.similar.moveHitSameTarget') | translate:{ move: hitLabel(m) } }}
              </span>
            }
            <span class="sp-parts">
              @for (part of parts; track part) {
                <span class="sp-part">
                  <span class="sp-part-name">{{ 'positionInReps.similar.parts.' + part | translate }}</span>
                  <span class="sp-bar"><i [style.width.%]="pct(partValue(m, part))"></i></span>
                  <span class="sp-part-val">{{ round(partValue(m, part)) }}</span>
                </span>
              }
            </span>
          </span>
        </button>
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .sp-filters { border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); padding-bottom: 6px; margin-bottom: 6px; }
    .sp-row { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; margin-bottom: 4px; }
    .sp-reps { max-height: 96px; overflow: auto; }
    .sp-label { font-size: .74rem; color: color-mix(in srgb, currentColor 60%, transparent); margin-right: 2px; }
    .sp-hint { font-size: .74rem; color: color-mix(in srgb, currentColor 55%, transparent); margin: 0 0 6px; }
    .sp-chip { display: inline-flex; align-items: center; gap: 3px; max-width: 190px; font: inherit; font-size: .76rem; line-height: 1.4;
      border: 1px solid color-mix(in srgb, currentColor 22%, transparent); background: none; color: inherit;
      border-radius: 12px; padding: 1px 8px; cursor: pointer; }
    .sp-chip:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .sp-chip-on { color: #1976d2; border-color: #1976d2; background: color-mix(in srgb, #1976d2 12%, transparent); }
    .sp-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .sp-rep-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .sp-link { font: inherit; font-size: .74rem; background: none; border: none; padding: 0 4px; color: #1976d2; cursor: pointer; }
    .sp-muted { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; }
    .sp-count { font-size: .82rem; color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px; }
    .sp-match { display: flex; align-items: flex-start; gap: 8px; width: 100%; text-align: left; font: inherit;
      background: none; border: none; border-radius: 6px; padding: 5px 4px; cursor: pointer; }
    .sp-match:hover { background: color-mix(in srgb, currentColor 7%, transparent); }
    .sp-scores { flex: 0 0 auto; display: flex; flex-direction: column; align-items: center; min-width: 44px; }
    .sp-score { font-size: 1.35rem; font-weight: 700; line-height: 1.15; color: #1976d2; }
    .sp-score-hi { color: #2e7d32; }
    .sp-score-base { font-size: .64rem; line-height: 1.2; color: color-mix(in srgb, currentColor 55%, transparent); font-variant-numeric: tabular-nums; }
    .sp-move-input { font: inherit; font-size: .78rem; width: 82px; padding: 1px 6px; border-radius: 10px; background: none; color: inherit;
      border: 1px solid color-mix(in srgb, currentColor 22%, transparent); }
    .sp-move-input:focus { outline: none; border-color: #1976d2; }
    .sp-move-bad, .sp-move-bad:focus { border-color: #c62828; }
    .sp-move-ok { font-size: .76rem; font-weight: 600; color: #1976d2; }
    .sp-move-error { font-size: .74rem; color: #c62828; margin: 0 0 6px; }
    .sp-movehit { display: block; font-size: .78rem; font-weight: 600; color: #2e7d32; margin-top: 2px; }
    .sp-movehit-weak { font-weight: 500; color: color-mix(in srgb, #2e7d32 75%, currentColor); }
    .sp-body { flex: 1; min-width: 0; }
    .sp-where { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .sp-rep { font-weight: 600; font-size: .88rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .sp-mirror { flex: 0 0 auto; font-size: .78rem; border: 1px solid color-mix(in srgb, currentColor 25%, transparent); border-radius: 8px; padding: 0 5px; }
    .sp-sub { font-size: .78rem; color: color-mix(in srgb, currentColor 62%, transparent); }
    .sp-chapter, .sp-line { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .sp-move { flex: 0 0 auto; }
    .sp-dot { flex: 0 0 auto; }
    .sp-parts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1px 10px; margin-top: 3px; }
    .sp-part { display: flex; align-items: center; gap: 4px; font-size: .7rem; color: color-mix(in srgb, currentColor 62%, transparent); }
    .sp-part-name { flex: 0 0 auto; min-width: 52px; }
    .sp-bar { flex: 1; min-width: 24px; height: 4px; border-radius: 2px; background: color-mix(in srgb, currentColor 14%, transparent); overflow: hidden; }
    .sp-bar i { display: block; height: 100%; background: #1976d2; }
    .sp-part-val { flex: 0 0 auto; min-width: 20px; text-align: right; font-variant-numeric: tabular-nums; }
  `]
})
export class SimilarPositionsComponent {
  @Input() matches: SimilarPositionMatch[] = [];
  /** Auswählbare Repertoires; leer = Auswahl wird nicht angeboten (dann sucht der Server in allen). */
  @Input() options: SimilarRepertoireOption[] = [];
  /** Aktuell durchsuchte Repertoire-Ids. */
  @Input() selected: ReadonlySet<number> = new Set<number>();
  @Input() preset: SimilarityPreset = 'ausgewogen';
  @Input() includeMirrored = true;
  /** Nur Stellungen mit derselben Seite am Zug (Server-Default: aus). */
  @Input() sameSideToMove = false;
  /** Rohtext des Zug-Feldes (SAN, wie getippt) — die Auswertung macht das Panel. */
  @Input() moveText = '';
  /** Kanonischer SAN des erkannten Zuges; '' = kein gültiger Zug (Feld leer oder unlesbar). */
  @Input() moveSan = '';
  /** Eingabe war nicht leer, ist auf dieser Stellung aber kein legaler Zug. */
  @Input() moveInvalid = false;
  /** Treffer ohne passenden Zug ausblenden (nur mit gültigem Zug sichtbar). */
  @Input() onlyWithMove = false;

  @Output() presetChange = new EventEmitter<SimilarityPreset>();
  @Output() mirroredChange = new EventEmitter<boolean>();
  @Output() sameSideToMoveChange = new EventEmitter<boolean>();
  @Output() moveTextChange = new EventEmitter<string>();
  @Output() onlyWithMoveChange = new EventEmitter<boolean>();
  /** Vollständige neue Auswahl (nicht der einzelne Umschalter) — das Panel lädt danach neu. */
  @Output() selectionChange = new EventEmitter<number[]>();
  @Output() open = new EventEmitter<SimilarPositionMatch>();

  readonly presets = SIMILAR_PRESETS;
  readonly parts = SIMILAR_PARTS;

  get selectedCount(): number { return this.options.filter(o => this.selected.has(o.id)).length; }

  isSelected(id: number): boolean { return this.selected.has(id); }

  choosePreset(p: SimilarityPreset): void {
    if (p !== this.preset) this.presetChange.emit(p);
  }

  onMoveInput(ev: Event): void {
    this.moveTextChange.emit((ev.target as HTMLInputElement | null)?.value ?? '');
  }

  /** Wurde an diesem Treffer überhaupt ein Bonus verrechnet? Nur dann sagen zwei Zahlen mehr als eine. */
  hasBonus(m: SimilarPositionMatch): boolean {
    return !!m.moveMatch && this.round(m.score) !== this.round(m.positionScore);
  }

  /** Exakter Treffer: der SAN, wie er dort steht („Nd5"). Schwächere Stufe: lange Notation
   * („Nf3-d5") — dort ist gerade das ABWEICHENDE Ausgangsfeld die Information. */
  hitLabel(m: SimilarPositionMatch): string {
    if (m.moveMatch === 'sameTarget') return longMoveLabel(m.moveSan, m.moveFrom, m.moveTo);
    return m.moveSan || longMoveLabel(m.moveSan, m.moveFrom, m.moveTo);
  }

  toggle(id: number): void {
    const next = this.options.map(o => o.id).filter(x => (x === id ? !this.selected.has(x) : this.selected.has(x)));
    this.selectionChange.emit(next);
  }
  selectAll(): void { this.selectionChange.emit(this.options.map(o => o.id)); }
  selectNone(): void { this.selectionChange.emit([]); }

  partValue(m: SimilarPositionMatch, part: SimilarPart): number {
    switch (part) {
      case 'pawns': return m.pawnScore;
      case 'material': return m.materialScore;
      case 'pieces': return m.pieceScore;
      default: return m.kingScore;
    }
  }

  /** Anzeige-Runden; defensiv gegen fehlende/kaputte Serverwerte (NaN → 0). */
  round(v: number): number { return Number.isFinite(v) ? Math.round(v) : 0; }
  /** Balkenbreite: auf 0..100 geklemmt, damit ein Ausreißer das Layout nicht sprengt. */
  pct(v: number): number { return Math.max(0, Math.min(100, this.round(v))); }

  /** Halbzüge → Zugnummer: ply 1 → „1.", ply 2 → „1…", ply 3 → „2.". */
  moveLabel(m: SimilarPositionMatch): string {
    const ply = Math.max(1, Math.trunc(m.ply));
    return Math.ceil(ply / 2) + (ply % 2 === 1 ? '.' : '…');
  }
}
