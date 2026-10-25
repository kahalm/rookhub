import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SanPipe } from '../../shared/chess/san.pipe';
import { GuessHistoryMove } from './guess.service';
import { guessHistoryRows, skipReason } from './guess-history.util';

/** Der Kommentar, der über der Zugliste steht (angesehener Zug bzw. eben gespielter Schritt). */
export interface GuessNote { move: string; text: string; language?: string | null }

/**
 * Die Partie bis hierhin (Eröffnung + gelöste Züge) in der Punktepartie: blättern, direkt
 * anklicken, Sprache der Anmerkungen umschalten (Zerlegung Z24, aus `GuessBoardComponent`).
 * Darstellung ohne eigenen Zustand — wohin geblättert wird, entscheidet die Seite.
 */
@Component({
  selector: 'app-guess-history-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe, SanPipe],
  template: `
    <div class="opening">
      <div class="onav">
        <button mat-icon-button (click)="browse.emit(-1)" [disabled]="atStart()"
                [attr.title]="'guess.startPosition' | translate"><mat-icon>first_page</mat-icon></button>
        <button mat-icon-button (click)="step.emit(-1)" [disabled]="atStart()"
                [attr.title]="'guess.prevMove' | translate"><mat-icon>chevron_left</mat-icon></button>
        <button mat-icon-button (click)="step.emit(1)" [disabled]="atTask()"
                [attr.title]="'guess.nextMove' | translate"><mat-icon>chevron_right</mat-icon></button>
        <!-- UI-Sweep 2026-10-11: kein zweites „Zur Aufgabe" hier — der Hauptknopf steht beim Durchsehen unter dem Brett. -->
        <!-- Umschalten nur, wenn es etwas umzuschalten gibt. -->
        @if (commentLanguages().length > 1) {
          <span class="onav-spacer"></span>
          <mat-icon class="lang-icon"
                    [attr.title]="'guess.commentLanguage' | translate">translate</mat-icon>
          @for (l of commentLanguages(); track l) {
            <button type="button" class="lang" [class.on]="l === commentLanguage()"
                    (click)="chooseLanguage.emit(l)">{{ langLabel(l) }}</button>
          }
        }
      </div>
      @if (browsedSkip; as why) {
        <div class="note skip-note">{{ why }}</div>
      }
      @if (note(); as note) {
        <div class="note">
          <span class="note-move">{{ note.move | san:'text' }}</span>
          <span>{{ note.text }}</span>
          <!-- Steht der Kommentar nur in einer anderen Sprache da, wird das gesagt statt
               eine Luecke zu lassen. -->
          @if (note.language) {
            <span class="lang-tag">{{ langLabel(note.language) }}</span>
          }
        </div>
      }
      <div class="omoves">
        @for (row of rows(); track row.no) {
          <div class="orow">
            <span class="no">{{ row.no }}.</span>
            @if (row.wIdx >= 0) {
              <button type="button" class="mv" [class.on]="browseIndex() === row.wIdx"
                      [class.noted]="row.wNoted" [class.skipped]="row.wSkipped"
                      [attr.title]="hintFor(row.wNoted, row.wSkipped)"
                      (click)="browse.emit(row.wIdx)">{{ row.w | san }}</button>
            } @else { <span class="mv muted">…</span> }
            @if (row.b !== null) {
              <button type="button" class="mv" [class.on]="browseIndex() === row.bIdx"
                      [class.noted]="row.bNoted" [class.skipped]="row.bSkipped"
                      [attr.title]="hintFor(row.bNoted, row.bSkipped)"
                      (click)="browse.emit(row.bIdx)">{{ row.b | san }}</button>
            } @else { <span></span> }
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    /* Kein eigener Kasten: .opening bleibt das Flex-Kind der Seitenspalte wie vor der Zerlegung. */
    :host { display: contents; }
    /* Eröffnung: Navigation oben, darunter die Züge UNTEREINANDER (Nr. · Weiß · Schwarz). */
    .opening { margin-top: 8px; }
    .onav { display: flex; align-items: center; gap: 2px; flex-wrap: wrap; }
    .onav button[mat-stroked-button] { margin-left: 8px; }
    /* Die Zugliste steht NEBEN dem Brett und darf deshalb so hoch werden wie das Brett. Breit
       wird sie trotzdem nicht: feste, schmale Spalten halten Weiss und Schwarz beieinander —
       gedehnt ueber die halbe Seite liest sich eine Zugliste schlecht. */
    .omoves { margin-top: 4px; max-height: 52vh; overflow-y: auto; }
    .orow { display: grid; grid-template-columns: 34px 96px 96px; gap: 4px; align-items: baseline;
            padding: 1px 2px; }
    .orow .no { color: color-mix(in srgb, currentColor 55%, transparent); font-size: .8rem;
                text-align: right; font-variant-numeric: tabular-nums; }
    .orow .mv { text-align: left; background: none; border: 0; color: inherit; font: inherit;
                padding: 1px 6px; border-radius: 3px; cursor: pointer; }
    .orow .mv:hover { background: color-mix(in srgb, currentColor 12%, transparent); }
    .orow .mv.on { background: color-mix(in srgb, currentColor 22%, transparent); font-weight: 600; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    /* Kommentierte Zuege tragen einen Punkt — Farbe allein traegt die Auskunft nicht. */
    .mv.noted { font-weight: 600; }
    .mv.noted::after { content: '\\2022'; margin-left: 2px; color: var(--mat-sys-primary); }
    /* Uebersprungen: durchgestrichen und blass — die Zeile war nie eine Aufgabe. */
    .mv.skipped { text-decoration: line-through; opacity: .6; }
    .skip-note { font-size: .82rem; }
    .note { margin: 8px 0 4px; padding: 8px 10px; border-radius: 6px; line-height: 1.45;
            border: 1px solid color-mix(in srgb, currentColor 18%, transparent);
            background: color-mix(in srgb, currentColor 5%, transparent); }
    .note-move { font-weight: 600; margin-right: 6px; }
    .onav-spacer { flex: 1 1 auto; }
    .lang-icon { font-size: 18px; width: 18px; height: 18px; opacity: .6; }
    .lang { border: none; background: none; cursor: pointer; font: inherit; font-size: .8rem;
            padding: 2px 6px; border-radius: 4px; color: inherit; opacity: .6; }
    .lang.on { opacity: 1; font-weight: 600; background: color-mix(in srgb, currentColor 12%, transparent); }
    .lang-tag { margin-left: 6px; font-size: .7rem; opacity: .6; text-transform: uppercase; }
  `],
})
export class GuessHistoryPanelComponent {
  private translate = inject(TranslateService);

  readonly history = input.required<GuessHistoryMove[]>();
  /** `null` = die Aufgabe selbst, `-1` = Grundstellung, sonst Index in `history`. */
  readonly browseIndex = input<number | null>(null);
  readonly atStart = input(false);
  readonly atTask = input(true);
  readonly note = input<GuessNote | null>(null);
  readonly commentLanguages = input<string[]>([]);
  readonly commentLanguage = input<string | null | undefined>(null);

  /** Einen Zug anschauen (`null` = zurück zur Aufgabe, `-1` = Grundstellung). */
  readonly browse = output<number | null>();
  /** Einen Halbzug vor (+1) oder zurück (-1). */
  readonly step = output<number>();
  readonly chooseLanguage = output<string>();

  readonly rows = computed(() => guessHistoryRows(this.history()));

  /** „DE", „EN" — und fuer eine Partie, deren Sprache sich nicht bestimmen liess, das Wort
   *  „Original" statt des nichtssagenden Kuerzels „und". */
  langLabel(code: string): string {
    return code === 'und' ? this.translate.instant('guess.commentOriginal') : code.toUpperCase();
  }

  /** Titel der markierten Zuege — beim Zeiger darueber und fuer Vorlesegeraete. */
  hintFor(noted: boolean, skipped: string | null): string | null {
    const parts = [];
    if (skipped) parts.push(this.translate.instant('guess.skipped.' + skipped));
    if (noted) parts.push(this.translate.instant('guess.hasComment'));
    return parts.length ? parts.join(' · ') : null;
  }

  /** Warum der gerade angesehene Zug nicht abgefragt wurde — sonst bleibt es ein Raetsel. */
  get browsedSkip(): string | null {
    const why = skipReason(this.history(), this.browseIndex());
    return why ? this.translate.instant('guess.skipped.' + why) : null;
  }
}
