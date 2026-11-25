import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { shortClock } from './clock.util';

/** Breite des Koordinatensystems (die SVG wird gezogen, wie bei der Bewertungskurve); Höhe 100 = Prozent. */
const W = 1000;

/** Ab so vielen Uhrenständen lohnt die Kurve — darunter reicht die Zeit an den Zügen. */
export const MIN_CLOCK_POINTS = 4;

interface ClockPoint { ply: number; seconds: number; left: number; top: number; label: string; }

/**
 * Zeitverbrauch einer Partie (1.015.0): Restzeit je Seite über die Züge, aus den `[%clk]` der Partie (bei eingelesenen
 * Formularen die notierten Restzeiten). x = Halbzug, y = Restzeit (oben = die größte vorkommende), Weiß als volle Linie in
 * der Akzentfarbe, Schwarz gestrichelt in der Schriftfarbe — die Seite ist so auch ohne Farbe (E-Ink, Druck) erkennbar,
 * die Legende zeigt dasselbe Muster. Punkte als HTML (in der gezogenen SVG würden Kreise zu Ellipsen), je Punkt ein
 * Tooltip; Klick springt zum Zug. Erscheint erst ab {@link MIN_CLOCK_POINTS} Uhrenständen. Kein `url(#…)` (siehe
 * Bewertungskurve).
 */
@Component({
  selector: 'app-clock-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    @if (visible()) {
      <figure class="clock-chart">
        <figcaption class="head">
          <span class="title">{{ 'pgnViewer.clockChart.title' | translate }}</span>
          <span class="legend" aria-hidden="true">
            <span class="key white"></span>{{ 'pgnViewer.clockChart.white' | translate }}
            <span class="key black"></span>{{ 'pgnViewer.clockChart.black' | translate }}
          </span>
        </figcaption>
        <div class="plot" (click)="onClick($event)" role="img" [attr.aria-label]="'pgnViewer.clockChart.aria' | translate: { count: points().length }">
          <span class="axis top">{{ maxLabel() }}</span>
          <svg [attr.viewBox]="'0 0 ' + width + ' 100'" preserveAspectRatio="none" aria-hidden="true">
            <line class="grid" x1="0" y1="50" [attr.x2]="width" y2="50" vector-effect="non-scaling-stroke" />
            <polyline class="line white" [attr.points]="whiteLine()" vector-effect="non-scaling-stroke" />
            <polyline class="line black" [attr.points]="blackLine()" vector-effect="non-scaling-stroke" />
            @if (cursorX() !== null) {
              <line class="cursor" [attr.x1]="cursorX()" y1="0" [attr.x2]="cursorX()" y2="100" vector-effect="non-scaling-stroke" />
            }
          </svg>
          @for (p of points(); track p.ply) {
            <span class="dot" [class.white]="p.ply % 2 === whiteParity()" [class.black]="p.ply % 2 !== whiteParity()"
                  [style.left.%]="p.left" [style.top.%]="p.top" [attr.title]="p.label"></span>
          }
        </div>
      </figure>
    }
  `,
  styles: [`
    :host { display: block; }
    .clock-chart { margin: 8px 0 0; }
    .head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; font-size: 12px; margin-bottom: 4px; }
    .title { font-weight: 600; }
    .legend { display: inline-flex; align-items: center; gap: 4px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .key { display: inline-block; width: 18px; height: 0; margin-left: 6px; vertical-align: middle; }
    .key.white { border-top: 2px solid var(--rh-accent); }
    .key.black { border-top: 2px dashed currentColor; }
    .plot {
      position: relative; height: 90px; cursor: pointer; overflow: hidden; border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
      background: color-mix(in srgb, currentColor 3%, transparent);
    }
    svg { display: block; width: 100%; height: 100%; }
    .grid { stroke: color-mix(in srgb, currentColor 12%, transparent); stroke-width: 1; }
    .line { fill: none; stroke-width: 2; stroke-linejoin: round; }
    .line.white { stroke: var(--rh-accent); }
    .line.black { stroke: currentColor; stroke-dasharray: 5 4; }
    .cursor { stroke: color-mix(in srgb, currentColor 45%, transparent); stroke-width: 1; }
    .axis { position: absolute; left: 4px; font-size: 10px; color: color-mix(in srgb, currentColor 60%, transparent); pointer-events: none; }
    .axis.top { top: 2px; }
    /* Trefferfläche größer als der Punkt (Tooltip per title) */
    .dot { position: absolute; width: 14px; height: 14px; transform: translate(-50%, -50%); }
    .dot::after {
      content: ''; position: absolute; inset: 3px; border-radius: 50%;
      box-shadow: 0 0 0 2px var(--mat-sys-surface, transparent);
    }
    .dot.white::after { background: var(--rh-accent); }
    .dot.black::after { background: currentColor; }
  `],
})
export class ClockChartComponent {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Restzeit in Sekunden je Halbzug-Index (wie `ParsedGame.clocks`). */
  readonly clocks = input<Readonly<Record<number, number>> | null | undefined>(null);
  /** Zahl der Halbzüge der Partie (x-Achse). */
  readonly plies = input(0);
  /** Wer den ersten Halbzug spielt (Stellungspartie mit Schwarz am Zug: `'b'`). */
  readonly firstColor = input<'w' | 'b'>('w');
  /** Aktueller Zug wie `PgnViewerService.currentMoveIndex` (−1 = Startstellung). */
  readonly currentIndex = input(-1);
  /** Halbzug-Index des angeklickten Punkts. */
  readonly moveClicked = output<number>();

  readonly width = W;

  /** Halbzüge mit gerader Nummer gehören Weiß — außer die Partie beginnt mit Schwarz. */
  readonly whiteParity = computed(() => this.firstColor() === 'w' ? 0 : 1);

  private readonly entries = computed(() => Object.entries(this.clocks() ?? {})
    .map(([k, v]) => ({ ply: Number(k), seconds: v }))
    .filter(e => Number.isInteger(e.ply) && e.ply >= 0 && e.ply < this.plies() && Number.isFinite(e.seconds))
    .sort((a, b) => a.ply - b.ply));

  readonly visible = computed(() => this.entries().length >= MIN_CLOCK_POINTS);
  private readonly max = computed(() => Math.max(60, ...this.entries().map(e => e.seconds)));
  readonly maxLabel = computed(() => shortClock(this.max()));

  private x(ply: number): number {
    const n = this.plies();
    return n <= 1 ? 0 : (ply / (n - 1)) * W;
  }
  /** 6 % Rand oben und unten, damit Punkte an den Enden nicht angeschnitten werden. */
  private y(seconds: number): number { return 6 + (1 - seconds / this.max()) * 88; }

  readonly points = computed<ClockPoint[]>(() => this.entries().map(e => {
    const white = e.ply % 2 === this.whiteParity();
    const moveNo = Math.floor((e.ply + (this.firstColor() === 'b' ? 1 : 0)) / 2) + 1;
    return {
      ply: e.ply, seconds: e.seconds, left: (this.x(e.ply) / W) * 100, top: this.y(e.seconds),
      label: `${moveNo}${white ? '.' : '…'} ${shortClock(e.seconds)}`,
    };
  }));

  private line(white: boolean): string {
    return this.points().filter(p => (p.ply % 2 === this.whiteParity()) === white)
      .map(p => `${Math.round(this.x(p.ply))},${Math.round(p.top * 10) / 10}`).join(' ');
  }
  readonly whiteLine = computed(() => this.line(true));
  readonly blackLine = computed(() => this.line(false));

  readonly cursorX = computed(() => {
    const i = this.currentIndex();
    return i >= 0 && i < this.plies() ? this.x(i) : null;
  });

  /** Klick: der Punkt mit dem nächsten Halbzug. */
  onClick(e: MouseEvent): void {
    const plot = (e.currentTarget as HTMLElement | null) ?? this.host.nativeElement;
    const rect = plot.getBoundingClientRect();
    const pts = this.points();
    if (!pts.length || rect.width <= 0) return;
    const ply = ((e.clientX - rect.left) / rect.width) * Math.max(1, this.plies() - 1);
    const best = pts.reduce((a, b) => Math.abs(b.ply - ply) < Math.abs(a.ply - ply) ? b : a);
    this.moveClicked.emit(best.ply);
  }
}
