import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output } from '@angular/core';

/**
 * Ein markierter Zug: der Punkt sitzt auf der Stellung NACH dem Zug `ply` (0-basiert). Die Farbe bringt
 * der Aufrufer mit — die Kurve kennt keine Zug-Klassen, und so bleibt die Farbtabelle an EINER Stelle
 * (`MOVE_CLASS_COLORS`), aus der auch die Zählertabelle malt.
 */
export interface EvalGraphMark {
  ply: number;
  /** Wird CSS-Klasse des Punkts (`dot <kind>`) — Haken für Tests und Styling, keine Farbe. */
  kind: string;
  color: string;
}

/** Breite des Koordinatensystems — die SVG wird per `preserveAspectRatio="none"` auf die Spalte
 *  gezogen, die Zahl bestimmt nur die Rechengenauigkeit. Höhe 100 = Prozent. */
const W = 1000;
const BOTTOM = 100;

interface Pt { x: number; y: number; }

/**
 * Bewertungskurve einer Partie wie bei chess.com: x = Stellung (0 = Start … n = nach dem letzten Zug),
 * y = Höhe 0..100 (50 = ausgeglichen; was die Höhe bedeutet, entscheidet der Aufrufer — der Partie-Rückblick
 * gibt die Bewertung linear bis ±10, `graphHeight`). Wie bei chess.com ist die Fläche UNTER der Kurve weiß
 * und alles darüber dunkel: je mehr Weiß, desto besser steht Weiß. Bis 0.520.0 war nur die Fläche zwischen
 * Kurve und Mittellinie gefüllt (oben hell, unten schwarz) auf grauem Grund — neben chess.com wirkte das wie
 * ein weißer Streifen auf Grau (Vergleich 2026-09-24).
 *
 * Reines SVG ohne Bibliothek; zwei Entscheidungen, die man beim Umbauen leicht kippt:
 * - **Keine `clipPath`/`url(#…)`-Verweise.** Mit `<base href>` und Routing lösen Firefox und Safari
 *   `url(#id)` gegen die Basis auf und finden das Element nicht — die Flächen wären unsichtbar. Die weiße
 *   Fläche ist deshalb ein eigenes Polygon je zusammenhängendem Lauf.
 * - **Lücken bleiben Lücken.** Eine nicht gerechnete Stellung unterbricht Linie und Fläche; über sie
 *   hinweg zu verbinden hieße, eine Bewertung zu zeichnen, die niemand gerechnet hat. Weil „keine weiße
 *   Fläche" hier „Schwarz gewinnt" heißt, bekommt eine Lücke ein eigenes neutrales Band (`gap`) — sonst
 *   sähe eine noch laufende Analyse am rechten Rand wie ein schwarzer Sieg aus.
 *
 * Die Flächen sind FESTE Farben: Weiß und Schwarz sind hier Figurenfarben, keine Themenfarben — mit
 * `currentColor` kehrte sich ihre Bedeutung im Dunkelmodus um. Das Bauteil trägt deshalb seinen
 * eigenen Hintergrund, wie das Brett.
 */
@Component({
  selector: 'app-eval-graph',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="graph" (click)="onClick($event)">
      <svg [attr.viewBox]="'0 0 ' + width + ' 100'" preserveAspectRatio="none" aria-hidden="true">
        @for (g of gaps(); track $index) {
          <rect class="gap" [attr.x]="g.x" y="0" [attr.width]="g.width" height="100" />
        }
        @for (s of segments(); track $index) {
          <polygon class="area-white" [attr.points]="s.white" />
        }
        <line class="mid" x1="0" y1="50" [attr.x2]="width" y2="50" vector-effect="non-scaling-stroke" />
        @for (s of segments(); track $index) {
          <polyline class="line" [attr.points]="s.line" vector-effect="non-scaling-stroke" />
        }
        @if (cursorX() !== null) {
          <line class="cursor" [attr.x1]="cursorX()" y1="0" [attr.x2]="cursorX()" y2="100" vector-effect="non-scaling-stroke" />
        }
      </svg>
      <!-- Punkte als HTML: in einer verzerrten SVG (preserveAspectRatio="none") würden Kreise zu Ellipsen. -->
      @for (d of dots(); track d.ply) {
        <span [class]="'dot ' + d.kind" [style.background]="d.color" [style.left.%]="d.left" [style.top.%]="d.top"></span>
      }
      @for (p of lonePoints(); track $index) {
        <span class="dot lone" [style.left.%]="p.left" [style.top.%]="p.top"></span>
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .graph {
      position: relative; height: 120px; cursor: pointer; overflow: hidden;
      background: #403e3b; border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
    }
    svg { display: block; width: 100%; height: 100%; }
    .area-white { fill: #ffffff; }
    .gap { fill: #6e6c69; }
    .mid { stroke: rgba(128, 128, 128, 0.55); stroke-width: 1; }
    .line { fill: none; stroke: #ffffff; stroke-width: 1; stroke-linejoin: round; }
    .cursor { stroke: #42a5f5; stroke-width: 2; }
    .dot {
      position: absolute; width: 8px; height: 8px; border-radius: 50%;
      transform: translate(-50%, -50%); pointer-events: none;
      box-shadow: 0 0 0 1.5px rgba(0, 0, 0, 0.55);
    }
    .dot.lone { width: 4px; height: 4px; background: #9e9e9e; box-shadow: none; }
  `],
})
export class EvalGraphComponent {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Höhe je Stellung (0..100, 50 = ausgeglichen, 100 = Weiß oben), `null` = nicht gerechnet; Länge = Züge + 1. */
  series = input<(number | null)[]>([]);
  /** Auffällige Züge (Brilliant, Great, Miss, Fehler, grobe Fehler) — als Punkt auf der Stellung nach dem Zug. */
  marks = input<EvalGraphMark[]>([]);
  /** Aktueller Zug wie `PgnViewerService.currentMoveIndex` (−1 = Startstellung). */
  currentIndex = input<number>(-1);

  /** Halbzug-Index wie `currentMoveIndex`: −1 = Startstellung. */
  moveClicked = output<number>();

  readonly width = W;

  /** Anzahl der Züge = Stellungen − 1. */
  private readonly plies = computed(() => Math.max(0, this.series().length - 1));

  private x(j: number): number {
    const n = this.plies();
    return n === 0 ? 0 : (j / n) * W;
  }

  /** Zusammenhängende Läufe gerechneter Stellungen (ab zwei Punkten — einer ist keine Linie). */
  private readonly runs = computed(() => {
    const series = this.series();
    const runs: Pt[][] = [];
    let current: Pt[] = [];
    series.forEach((v, j) => {
      if (v == null) {
        if (current.length) runs.push(current);
        current = [];
      } else {
        current.push({ x: this.x(j), y: 100 - v });
      }
    });
    if (current.length) runs.push(current);
    return runs;
  });

  readonly segments = computed(() => this.runs().filter(r => r.length > 1).map(run => {
    const first = run[0];
    const last = run[run.length - 1];
    return {
      line: run.map(EvalGraphComponent.fmt).join(' '),
      // Im SVG wächst y nach UNTEN: die weiße Fläche reicht vom unteren Rand bis zur Kurve.
      white: [{ x: first.x, y: BOTTOM }, ...run, { x: last.x, y: BOTTOM }].map(EvalGraphComponent.fmt).join(' '),
    };
  }));

  /** Nicht gerechnete Strecken: von der letzten gerechneten Stellung davor bis zur ersten danach (bzw. zum Rand). */
  readonly gaps = computed(() => {
    const series = this.series();
    const n = this.plies();
    if (n === 0) return [];
    const out: { x: number; width: number }[] = [];
    let start = -1;
    series.forEach((v, j) => {
      if (v == null && start < 0) start = j;
      if (v != null && start >= 0) {
        out.push(this.band(start, j));
        start = -1;
      }
    });
    if (start >= 0) out.push(this.band(start, series.length));
    return out;
  });

  private band(firstNull: number, nextDefined: number): { x: number; width: number } {
    const from = this.x(Math.max(0, firstNull - 1));
    const to = this.x(Math.min(this.plies(), nextDefined));
    return { x: Math.round(from * 100) / 100, width: Math.round((to - from) * 100) / 100 };
  }

  readonly lonePoints = computed(() => this.runs().filter(r => r.length === 1)
    .map(([p]) => ({ left: (p.x / W) * 100, top: p.y })));

  readonly dots = computed(() => {
    const series = this.series();
    return this.marks()
      .filter(m => series[m.ply + 1] != null)
      .map(m => ({
        ply: m.ply, kind: m.kind, color: m.color,
        left: (this.x(m.ply + 1) / W) * 100, top: 100 - (series[m.ply + 1] as number),
      }));
  });

  readonly cursorX = computed(() => {
    const n = this.plies();
    if (n === 0) return null;
    return this.x(Math.min(n, Math.max(0, this.currentIndex() + 1)));
  });

  /** Nächstgelegene Stellung zum Klick; links der ersten Marke ist das die Startstellung (−1). */
  onClick(event: MouseEvent): void {
    const n = this.plies();
    if (n === 0) return;
    const rect = this.host.nativeElement.getBoundingClientRect();
    if (rect.width <= 0) return;
    const fraction = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
    this.moveClicked.emit(Math.round(fraction * n) - 1);
  }

  private static fmt(p: Pt): string {
    return `${Math.round(p.x * 100) / 100},${Math.round(p.y * 100) / 100}`;
  }
}
