import { ChangeDetectionStrategy, Component, Input, OnChanges } from '@angular/core';
import { DrawShape } from 'chessground/draw';

/**
 * DRUCKFESTES Schachbrett als Inline-SVG: Felder als Rechtecke, Figuren als `<image>`-Verweise auf
 * die mitgelieferten Piece-SVGs (`/piece/<set>/wK.svg` …), Pfeile/Feld-Markierungen als
 * SVG-Formen. Bewusst KEIN chessground: dessen Figuren sind CSS-Hintergrundbilder, und Browser
 * drucken Hintergründe standardmäßig NICHT — die Karten wären leer. `<image>`-Inhalte druckt
 * jeder Browser.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-flashcard-board',
  standalone: true,
  template: `
    <svg [attr.viewBox]="'0 0 8 8'" xmlns="http://www.w3.org/2000/svg">
      @for (sq of squares; track sq.x + '-' + sq.y) {
        <rect [attr.x]="sq.x" [attr.y]="sq.y" width="1" height="1" [attr.fill]="sq.light ? '#f0d9b5' : '#b58863'" />
      }
      @for (p of pieces; track p.x + '-' + p.y) {
        <image [attr.href]="p.href" [attr.x]="p.x" [attr.y]="p.y" width="1" height="1" />
      }
      @for (c of circles; track c.x + '-' + c.y) {
        <circle [attr.cx]="c.x" [attr.cy]="c.y" r="0.46" fill="none"
                [attr.stroke]="c.color" stroke-width="0.08" opacity="0.85" />
      }
      @for (a of arrows; track a.x1 + '-' + a.y1 + '-' + a.x2 + '-' + a.y2) {
        <line [attr.x1]="a.x1" [attr.y1]="a.y1" [attr.x2]="a.x2" [attr.y2]="a.y2"
              [attr.stroke]="a.color" stroke-width="0.16" opacity="0.85" stroke-linecap="round" />
        <polygon [attr.points]="a.head" [attr.fill]="a.color" opacity="0.85" />
      }
    </svg>
  `,
  styles: [`
    :host { display: block; }
    svg { display: block; width: 100%; height: auto; }
  `],
})
export class FlashcardBoardComponent implements OnChanges {
  @Input() fen = '';
  @Input() orientation: 'white' | 'black' = 'white';
  @Input() shapes: DrawShape[] = [];
  @Input() pieceSet = 'cburnett';

  squares: { x: number; y: number; light: boolean }[] = [];
  pieces: { x: number; y: number; href: string }[] = [];
  arrows: { x1: number; y1: number; x2: number; y2: number; head: string; color: string }[] = [];
  circles: { x: number; y: number; color: string }[] = [];

  // chessground-Standard-Pinsel (dieselben Farben wie im Solver-Review).
  private static readonly BRUSHES: Record<string, string> = {
    green: '#15781b', red: '#882020', blue: '#003088', yellow: '#e68f00',
  };

  ngOnChanges(): void {
    this.squares = [];
    for (let y = 0; y < 8; y++)
      for (let x = 0; x < 8; x++)
        this.squares.push({ x, y, light: (x + y) % 2 === 0 });

    this.pieces = [];
    const rows = (this.fen.split(' ')[0] || '').split('/');
    for (let r = 0; r < Math.min(rows.length, 8); r++) {
      let file = 0;
      for (const c of rows[r]) {
        if (c >= '1' && c <= '8') { file += Number(c); continue; }
        if (file >= 8) break;
        const code = (c === c.toUpperCase() ? 'w' : 'b') + c.toUpperCase();
        const { x, y } = this.toXY(file, 7 - r);
        this.pieces.push({ x, y, href: `/piece/${this.pieceSet}/${code}.svg` });
        file++;
      }
    }

    this.arrows = [];
    this.circles = [];
    for (const s of this.shapes || []) {
      const color = FlashcardBoardComponent.BRUSHES[s.brush || 'green'] || FlashcardBoardComponent.BRUSHES['green'];
      const o = this.keyToXY(s.orig as string);
      if (!o) continue;
      if (!s.dest) {
        this.circles.push({ x: o.x + 0.5, y: o.y + 0.5, color });
        continue;
      }
      const d = this.keyToXY(s.dest as string);
      if (!d) continue;
      // Pfeil von Feldmitte zu Feldmitte, vor der Spitze verkürzt; Spitze als Dreieck.
      const cx1 = o.x + 0.5, cy1 = o.y + 0.5, cx2 = d.x + 0.5, cy2 = d.y + 0.5;
      const dx = cx2 - cx1, dy = cy2 - cy1;
      const len = Math.hypot(dx, dy) || 1;
      const ux = dx / len, uy = dy / len;
      const tipX = cx2 - ux * 0.18, tipY = cy2 - uy * 0.18;
      const baseX = tipX - ux * 0.34, baseY = tipY - uy * 0.34;
      const px = -uy, py = ux;
      const head = `${tipX},${tipY} ${baseX + px * 0.2},${baseY + py * 0.2} ${baseX - px * 0.2},${baseY - py * 0.2}`;
      this.arrows.push({ x1: cx1 + ux * 0.28, y1: cy1 + uy * 0.28, x2: baseX, y2: baseY, head, color });
    }
  }

  /** Brett-Koordinaten (file 0..7 = a..h, rank 0..7 = 1..8) → SVG-Zelle, Ausrichtung beachtet. */
  private toXY(file: number, rank: number): { x: number; y: number } {
    return this.orientation === 'white'
      ? { x: file, y: 7 - rank }
      : { x: 7 - file, y: rank };
  }

  private keyToXY(key: string): { x: number; y: number } | null {
    if (!key || key.length < 2) return null;
    const file = key.charCodeAt(0) - 97;
    const rank = Number(key[1]) - 1;
    if (file < 0 || file > 7 || !(rank >= 0 && rank <= 7)) return null;
    return this.toXY(file, rank);
  }
}
