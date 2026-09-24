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
const MID = 50;
/** Zwischenpunkte je Zug beim Glätten — genug, dass man keine Ecken mehr sieht, wenig genug für ein SVG. */
const SMOOTH_STEPS = 8;

interface Pt { x: number; y: number; }

/**
 * Bewertungskurve einer Partie: x = Stellung (0 = Start … n = nach dem letzten Zug), y = Höhe 0..100
 * (50 = ausgeglichen; was die Höhe bedeutet, entscheidet der Aufrufer — der Partie-Rückblick gibt die
 * Bewertung linear bis ±10, `graphHeight`). Die Fläche zwischen Kurve und Mittellinie ist oberhalb hell
 * (Weiß steht besser), unterhalb dunkel, der Rest grau — wie bei Lichess.
 *
 * **Geglättet** (seit 0.521.1, `smooth`): zwischen den Stellungen läuft die Kurve als monotone kubische
 * Interpolation (Fritsch–Carlson) statt als gerade Linie — gewünscht 2026-09-24 („extremst kantig", chess.com
 * fühle sich runder an). Monoton heißt: die Kurve geht durch jeden gerechneten Punkt und schießt zwischen zwei
 * Punkten nie über sie hinaus — sie zeigt keine Bewertung, die keine Stellung hatte. Der Versuch davor
 * (0.520.1: weiße Fläche vom unteren Rand wie bei chess.com) gefiel nicht und ist zurückgenommen.
 *
 * Reines SVG ohne Bibliothek; zwei Entscheidungen, die man beim Umbauen leicht kippt:
 * - **Keine `clipPath`/`url(#…)`-Verweise.** Mit `<base href>` und Routing lösen Firefox und Safari
 *   `url(#id)` gegen die Basis auf und finden das Element nicht — die Flächen wären unsichtbar. Die
 *   hellen und dunklen Flächen werden deshalb als eigene Polygone gerechnet (Kurve an der Mittellinie
 *   gekappt, Schnittpunkte eingefügt).
 * - **Lücken bleiben Lücken.** Eine nicht gerechnete Stellung unterbricht Linie und Fläche; über sie
 *   hinweg zu verbinden hieße, eine Bewertung zu zeichnen, die niemand gerechnet hat.
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
        @for (s of segments(); track $index) {
          <polygon class="area-white" [attr.points]="s.white" />
          <polygon class="area-black" [attr.points]="s.black" />
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
      background: #4a4a4a; border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
    }
    svg { display: block; width: 100%; height: 100%; }
    .area-white { fill: #ececec; }
    .area-black { fill: #161616; }
    .mid { stroke: rgba(255, 255, 255, 0.35); stroke-width: 1; }
    .line { fill: none; stroke: #9e9e9e; stroke-width: 1.5; stroke-linejoin: round; }
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

  /** Gewinnchance Weiß je Stellung (0..100), `null` = nicht gerechnet; Länge = Züge + 1. */
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

  readonly segments = computed(() => this.runs().filter(r => r.length > 1).map(raw => {
    const run = EvalGraphComponent.smooth(raw);
    const withCrossings = EvalGraphComponent.withMidCrossings(run);
    const polygon = (clamp: (y: number) => number) => {
      const first = withCrossings[0];
      const last = withCrossings[withCrossings.length - 1];
      return [{ x: first.x, y: MID }, ...withCrossings.map(p => ({ x: p.x, y: clamp(p.y) })), { x: last.x, y: MID }]
        .map(EvalGraphComponent.fmt).join(' ');
    };
    return {
      line: run.map(EvalGraphComponent.fmt).join(' '),
      // Im SVG wächst y nach UNTEN: „Weiß vorn" = y < 50.
      white: polygon(y => Math.min(y, MID)),
      black: polygon(y => Math.max(y, MID)),
    };
  }));

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

  /**
   * Monotone kubische Interpolation (Fritsch–Carlson) durch die Punkte eines Laufs, abgetastet mit
   * `SMOOTH_STEPS` Zwischenpunkten je Zug. Die Stützpunkte selbst bleiben exakt erhalten (dort sitzen die
   * farbigen Punkte), und zwischen zwei Punkten bleibt die Kurve in deren Wertebereich: an einem Hoch oder
   * Tief ist die Steigung 0, sonst wird sie so begrenzt, dass nichts überschwingt.
   */
  static smooth(run: Pt[]): Pt[] {
    const n = run.length;
    if (n < 3) return run;
    const d: number[] = [];                       // Steigung je Abschnitt
    for (let i = 0; i < n - 1; i++) d.push((run[i + 1].y - run[i].y) / (run[i + 1].x - run[i].x));
    const m: number[] = new Array(n);             // Steigung je Punkt
    m[0] = d[0];
    m[n - 1] = d[n - 2];
    for (let i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
    for (let i = 0; i < n - 1; i++) {
      if (d[i] === 0) { m[i] = 0; m[i + 1] = 0; continue; }
      const a = m[i] / d[i];
      const b = m[i + 1] / d[i];
      const h = a * a + b * b;
      if (h > 9) {
        const t = 3 / Math.sqrt(h);
        m[i] = t * a * d[i];
        m[i + 1] = t * b * d[i];
      }
    }
    const out: Pt[] = [run[0]];
    for (let i = 0; i < n - 1; i++) {
      const p = run[i];
      const q = run[i + 1];
      const dx = q.x - p.x;
      for (let k = 1; k <= SMOOTH_STEPS; k++) {
        if (k === SMOOTH_STEPS) { out.push(q); break; }
        const t = k / SMOOTH_STEPS;
        const t2 = t * t;
        const t3 = t2 * t;
        const y = (2 * t3 - 3 * t2 + 1) * p.y + (t3 - 2 * t2 + t) * dx * m[i]
          + (-2 * t3 + 3 * t2) * q.y + (t3 - t2) * dx * m[i + 1];
        out.push({ x: p.x + t * dx, y });
      }
    }
    return out;
  }

  /** Schnittpunkte mit der Mittellinie einfügen, damit das Kappen die Fläche exakt dort teilt. */
  private static withMidCrossings(run: Pt[]): Pt[] {
    const out: Pt[] = [run[0]];
    for (let i = 1; i < run.length; i++) {
      const a = run[i - 1];
      const b = run[i];
      if ((a.y - MID) * (b.y - MID) < 0) {
        const t = (MID - a.y) / (b.y - a.y);
        out.push({ x: a.x + t * (b.x - a.x), y: MID });
      }
      out.push(b);
    }
    return out;
  }

  private static fmt(p: Pt): string {
    return `${Math.round(p.x * 100) / 100},${Math.round(p.y * 100) / 100}`;
  }
}
