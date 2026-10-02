import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnDestroy, Output, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { HelpHintComponent } from '../../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../../shared/icon-label/icon-label.directive';
import { MaiaEngineService } from './maia-engine.service';
import { MAIA_ELO_OPTIONS } from './maia-model';

/** Was die Karte zeigt, solange kein Sparring läuft. */
type CardPhase = 'rest' | 'confirm' | 'loading' | 'error';

/**
 * „Sparring gegen Maia" im Analysebrett: die Karte zwischen Engine-Karte und Zugliste.
 *
 * Eigene Komponente, damit `analysis.component.ts` nicht weiter wächst. Sie führt den LADE-Ablauf selbst
 * (`prepare()` → ggf. Rückfrage → `download()` → Fortschritt) und meldet `start` erst, wenn das Modell
 * bereit ist — nur so kann das Analysebrett beim Start sofort einen Maia-Zug anfordern. Das Spiel selbst
 * (wer zieht, Baum, Engine aus/an) gehört dem Analysebrett; die Karte schickt dafür nur Ereignisse.
 *
 * Zwei Regeln, die nicht kippen dürfen:
 * 1. **`start` nur aus DIESEM Ablauf**: jeder Start/Abbruch zählt `flow` hoch, eine später fertige Ladung
 *    eines abgebrochenen (oder nach dem Verlassen der Seite) Ablaufs startet nichts mehr.
 * 2. **Kein Versprechen, das der Browser nicht hält**: ohne nutzbare Cache API (Dev über HTTP, Privatmodus)
 *    sagt die Rückfrage, dass das Modell bei jedem Besuch neu kommt (`canStore`).
 *
 * „Partie analysieren" (`showAnalyze`) zeigt die Karte nur an — ob es eine Partie gibt, ob eine Engine bereitsteht
 * und was der Klick tut, entscheidet das Analysebrett. Ein gesperrter Knopf zeigt keinen Tooltip; der Grund steht
 * deshalb als Zeile darunter (auch am Handy lesbar). Für Vorleser hängt `matTooltip` ihn als Beschreibung an.
 */
@Component({
  selector: 'app-maia-sparring-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatProgressBarModule,
    MatProgressSpinnerModule, MatSelectModule, MatTooltipModule, NgTemplateOutlet, TranslatePipe, HelpHintComponent,
    IconLabelDirective,
  ],
  template: `
    <mat-card class="maia-card">
      <mat-card-content>
        <div class="maia-head">
          <mat-icon class="maia-icon">smart_toy</mat-icon>
          <span class="maia-title">{{ 'analysis.maia.title' | translate }}</span>
          <app-help-hint [text]="'analysis.maia.hint' | translate" />
          <span class="maia-spacer"></span>
          <mat-form-field appearance="outline" class="elo-field" subscriptSizing="dynamic">
            <mat-label>{{ 'analysis.maia.strength' | translate }}</mat-label>
            <mat-select [value]="elo" (selectionChange)="eloChange.emit($event.value)">
              @for (e of eloOptions; track e) { <mat-option [value]="e">{{ e }}</mat-option> }
            </mat-select>
          </mat-form-field>
        </div>

        @if (active) {
          <p class="maia-line">
            {{ 'analysis.maia.status' | translate:{ elo: elo, side: ((userColor === 'white' ? 'analysis.maia.youWhite' : 'analysis.maia.youBlack') | translate) } }}
          </p>
          @if (thinking) {
            <p class="maia-thinking"><mat-spinner diameter="16" /> {{ 'analysis.maia.thinking' | translate }}</p>
          }
          <div class="maia-actions">
            @if (maiaToMove && !thinking) {
              <button mat-icon-button class="maia-move" (click)="maiaMove.emit()" [appIconLabel]="'analysis.maia.maiaMove' | translate">
                <mat-icon>play_arrow</mat-icon>
              </button>
            }
            <button mat-icon-button class="maia-switch" (click)="switchSides.emit()" [appIconLabel]="'analysis.maia.switchSides' | translate">
              <mat-icon>swap_horiz</mat-icon>
            </button>
            <button mat-icon-button class="maia-restart" (click)="restart.emit()" [appIconLabel]="'analysis.maia.restart' | translate">
              <mat-icon>replay</mat-icon>
            </button>
            <button mat-icon-button class="maia-stop" (click)="stop.emit()" [appIconLabel]="'analysis.maia.stop' | translate">
              <mat-icon>stop</mat-icon>
            </button>
          </div>
          @if (showAnalyze) {
            <div class="maia-buttons"><ng-container [ngTemplateOutlet]="analyzeButton" /></div>
            <ng-container [ngTemplateOutlet]="analyzeHint" />
          }
        } @else {
          @switch (phase()) {
            @case ('confirm') {
              <p class="maia-line">{{ (maia.canStore() ? 'analysis.maia.downloadInfo' : 'analysis.maia.downloadInfoNoStore') | translate }}</p>
              <div class="maia-buttons">
                <button mat-flat-button color="primary" class="maia-download" (click)="onDownload()">{{ 'analysis.maia.download' | translate }}</button>
                <button mat-button class="maia-cancel" (click)="onCancel()">{{ 'common.cancel' | translate }}</button>
              </div>
            }
            @case ('loading') {
              @if (maia.status() === 'downloading') {
                <mat-progress-bar mode="determinate" [value]="maia.progress()" />
                <p class="maia-line">{{ 'analysis.maia.downloading' | translate:{ percent: maia.progress() } }}</p>
              } @else {
                <mat-progress-bar mode="indeterminate" />
                <p class="maia-line">{{ 'analysis.maia.loading' | translate }}</p>
              }
              <div class="maia-buttons">
                <button mat-button class="maia-cancel" (click)="onCancel()">{{ 'common.cancel' | translate }}</button>
              </div>
            }
            @case ('error') {
              <p class="maia-line maia-error">{{ errorKey() | translate }}</p>
              <div class="maia-buttons">
                <button mat-flat-button color="primary" class="maia-retry" (click)="onRetry()">{{ 'analysis.maia.retry' | translate }}</button>
              </div>
            }
            @default {
              <div class="maia-buttons">
                <button mat-flat-button color="primary" class="maia-start" [disabled]="disabled" (click)="onStart()">{{ 'analysis.maia.start' | translate }}</button>
                @if (showAnalyze) { <ng-container [ngTemplateOutlet]="analyzeButton" /> }
              </div>
              @if (showAnalyze) { <ng-container [ngTemplateOutlet]="analyzeHint" /> }
            }
          }
        }
      </mat-card-content>
    </mat-card>

    <ng-template #analyzeButton>
      <button mat-stroked-button class="maia-analyze" [disabled]="analyzing || analyzeBlocked" (click)="onAnalyze()"
              [matTooltip]="analyzeBlocked ? ('guess.upload.noEngine' | translate) : ''"
              [attr.title]="analyzeBlocked ? ('guess.upload.noEngine' | translate) : null">
        @if (analyzing) { <mat-spinner diameter="16" class="maia-analyze-spinner" /> } @else { <mat-icon>insights</mat-icon> }
        {{ 'games.analyze' | translate }}
      </button>
    </ng-template>
    <ng-template #analyzeHint>
      @if (analyzeBlocked) { <p class="maia-line maia-analyze-hint">{{ 'guess.upload.noEngine' | translate }}</p> }
    </ng-template>
  `,
  styles: [`
    :host { display: block; }
    .maia-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .maia-icon { color: var(--rh-accent); }
    .maia-title { font-weight: 600; }
    .maia-spacer { flex: 1 1 auto; }
    .elo-field { width: 104px; }
    .maia-line { margin: 8px 0 0; font-size: .9rem; overflow-wrap: anywhere; }
    .maia-error { color: var(--rh-error); }
    .maia-thinking { display: flex; align-items: center; gap: 8px; margin: 6px 0 0; font-size: .85rem;
      color: color-mix(in srgb, currentColor 70%, transparent); }
    .maia-actions { display: flex; align-items: center; gap: 2px; flex-wrap: wrap; margin-top: 4px; }
    .maia-buttons { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-top: 8px; }
    mat-progress-bar { margin-top: 10px; }
    .maia-analyze { max-width: 100%; }
    .maia-analyze-spinner { display: inline-block; margin-right: 8px; vertical-align: middle; }
    .maia-analyze-hint { font-size: .85rem; color: color-mix(in srgb, currentColor 70%, transparent); }
  `],
})
export class MaiaSparringCardComponent implements OnDestroy {
  /** Läuft das Sparring (dann die aktive Ansicht statt des Lade-Ablaufs)? */
  @Input() active = false;
  @Input() userColor: 'white' | 'black' = 'white';
  /** Maia überlegt gerade (kleiner Kreisel, „Maia zieht" versteckt). */
  @Input() thinking = false;
  /** Sparring läuft, Maia ist am Zug, die Stellung ist nicht zu Ende → „Maia zieht" anbieten. */
  @Input() maiaToMove = false;
  @Input() elo = 1600;
  /** „Starten" gesperrt (etwa während der Stellungs-Editor offen ist). */
  @Input() disabled = false;
  /** „Partie analysieren" zeigen (Ruhe: neben „Starten", aktiv: unter den Symbolen). */
  @Input() showAnalyze = false;
  /** Der Klick läuft gerade (speichern + einreihen) — Knopf gesperrt, Kreisel statt Symbol. */
  @Input() analyzing = false;
  /** Keine Engine bereit — Knopf gesperrt, Grund als Zeile darunter. */
  @Input() analyzeBlocked = false;

  /** Das Modell ist bereit — das Analysebrett beginnt das Sparring. */
  @Output() readonly start = new EventEmitter<void>();
  @Output() readonly stop = new EventEmitter<void>();
  @Output() readonly switchSides = new EventEmitter<void>();
  @Output() readonly restart = new EventEmitter<void>();
  @Output() readonly maiaMove = new EventEmitter<void>();
  @Output() readonly eloChange = new EventEmitter<number>();
  /** „Partie analysieren" geklickt. */
  @Output() readonly analyze = new EventEmitter<void>();

  readonly maia = inject(MaiaEngineService);
  readonly eloOptions = MAIA_ELO_OPTIONS;
  readonly phase = signal<CardPhase>('rest');

  readonly errorKey = computed(() => this.maia.error() === 'unavailable'
    ? 'analysis.maia.errorUnavailable' : 'analysis.maia.errorFailed');

  /** Zählt jeden Start/Abbruch: nur der jüngste Ablauf darf am Ende `start` melden (Regel 1). */
  private flow = 0;
  /** Endete der letzte Versuch beim Herunterladen? Dann lädt „Erneut versuchen" gleich wieder (die Zustimmung
   *  gab es schon), sonst beginnt es mit dem Blick in den Cache. */
  private lastWasDownload = false;

  onStart(): void {
    if (this.disabled) return;
    void this.run(false);
  }

  onDownload(): void { void this.run(true); }

  onRetry(): void { void this.run(this.lastWasDownload); }

  onAnalyze(): void {
    if (this.analyzing || this.analyzeBlocked) return;
    this.analyze.emit();
  }

  /** Rückfrage verwerfen bzw. das Warten aufgeben. Ein laufender Download lädt im Hintergrund zu Ende (er liegt
   *  danach im Cache, der nächste Start ist sofort da) — nur das Sparring beginnt nicht von selbst. */
  onCancel(): void {
    this.flow++;
    this.phase.set('rest');
  }

  ngOnDestroy(): void { this.flow++; }

  private async run(download: boolean): Promise<void> {
    const token = ++this.flow;
    this.lastWasDownload = download;
    this.phase.set('loading');
    const ok = await (download ? this.maia.download() : this.maia.prepare());
    if (token !== this.flow) return;   // abgebrochen, neu gestartet oder Seite verlassen
    if (ok) {
      this.phase.set('rest');
      this.start.emit();
      return;
    }
    const status = this.maia.status();
    this.phase.set(status === 'missing' ? 'confirm' : status === 'error' ? 'error' : 'rest');
  }
}
