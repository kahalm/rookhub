import { ChangeDetectionStrategy, Component, ElementRef, computed, input, output, signal, viewChild } from '@angular/core';
import { DEFAULT_FACE, FACE_MAX_R, FACE_MIN_R, Face, clampFace, faceBox } from '../core/face';

interface Drag {
  mode: 'move' | 'size';
  startX: number;
  startY: number;
  face: Face;
}

/**
 * Das Gesicht im Bild wählen (Wunsch 2026-10-03): das Bild groß, darüber ein Kreis. Den Kreis ZIEHEN verschiebt ihn, der
 * Griff am Rand und der Regler darunter ändern die Größe, ein Tipp ins Bild setzt ihn dorthin; mit der Tastatur gehen
 * die Pfeiltasten und Plus/Minus. Außerhalb des Kreises ist das Bild abgedunkelt — so sieht man, was nachher in Kartei
 * und Abhak-Liste vor dem Namen steht.
 *
 * Die Komponente hält den Kreis nicht selbst: sie zeigt `face` (ohne Angabe den Anfangskreis) und meldet jede Änderung
 * über `faceChange` — schon ins Bild geholt (`clampFace`, dieselbe Regel wie am Server).
 */
@Component({
  selector: 'ch-face-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'face-picker' },
  template: `
    <div #stage class="face-stage" [class.disabled]="disabled()" (click)="jump($event)">
      <img [src]="src()" alt="Das Bild zum Blatt" draggable="false" (load)="loaded($any($event.target))" (error)="size.set(null)">
      @if (box(); as b) {
        <div class="face-ring" tabindex="0" role="group"
          aria-label="Kreis ums Gesicht: mit den Pfeiltasten verschieben, mit Plus und Minus die Größe ändern"
          [style.left.%]="b.left" [style.top.%]="b.top" [style.width.%]="b.width" [style.height.%]="b.height"
          (pointerdown)="down($event, 'move')" (pointermove)="move($event)" (pointerup)="up()" (pointercancel)="up()"
          (keydown)="key($event)">
          <span class="face-grip" aria-hidden="true" (pointerdown)="down($event, 'size')"></span>
        </div>
      }
    </div>
    @if (shown(); as f) {
      <label class="face-size"><span>Größe des Kreises</span>
        <input type="range" name="faceSize" [min]="minPercent" [max]="maxPercent" step="1" [value]="percent(f.r)" [disabled]="disabled()"
          (input)="resize(+$any($event.target).value / 100)"></label>
    }
  `,
})
export class FacePickerComponent {
  /** Adresse des Bilds (Vorschau der gewählten Datei oder das vorhandene Bild als Blob). */
  readonly src = input.required<string>();
  /** Der gewählte Kreis; ohne Angabe steht der Anfangskreis da. */
  readonly face = input<Face | null>(null);
  readonly disabled = input(false);
  readonly faceChange = output<Face>();

  /** Maße des Bilds, sobald es geladen ist — vorher gibt es keinen Kreis. */
  readonly size = signal<{ width: number; height: number } | null>(null);
  readonly shown = computed(() => {
    const s = this.size();
    return s ? clampFace(this.face() ?? DEFAULT_FACE, s.width, s.height) : null;
  });
  readonly box = computed(() => {
    const s = this.size();
    const f = this.shown();
    return s && f ? faceBox(f, s.width, s.height) : null;
  });
  readonly minPercent = FACE_MIN_R * 100;
  readonly maxPercent = FACE_MAX_R * 100;

  private readonly stage = viewChild.required<ElementRef<HTMLElement>>('stage');
  private drag: Drag | null = null;

  loaded(img: HTMLImageElement): void {
    this.size.set(img.naturalWidth > 0 && img.naturalHeight > 0 ? { width: img.naturalWidth, height: img.naturalHeight } : null);
  }

  percent(r: number): number {
    return Math.round(r * 100);
  }

  /** Den Kreis melden — ins Bild geholt; unverändert wird nichts gemeldet. */
  private emit(face: Face): void {
    const s = this.size();
    const next = s ? clampFace(face, s.width, s.height) : null;
    const now = this.shown();
    if (!next || (now && next.x === now.x && next.y === now.y && next.r === now.r && this.face())) return;
    this.faceChange.emit(next);
  }

  down(event: PointerEvent, mode: Drag['mode']): void {
    const face = this.shown();
    if (this.disabled() || !face) return;
    event.preventDefault();
    event.stopPropagation();                                             // der Griff liegt IM Kreis
    // Die Züge sollen am Kreis bleiben, auch wenn der Finger ihn verlässt. Ein nachgestelltes Ereignis (Test) hat keinen
    // aktiven Zeiger — dann eben ohne.
    try { (event.currentTarget as HTMLElement).closest('.face-ring')?.setPointerCapture(event.pointerId); } catch { /* ohne */ }
    this.drag = { mode, startX: event.clientX, startY: event.clientY, face };
  }

  move(event: PointerEvent): void {
    const d = this.drag;
    if (!d) return;
    const rect = this.stage().nativeElement.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    if (d.mode === 'move') {
      this.emit({ ...d.face, x: d.face.x + (event.clientX - d.startX) / rect.width, y: d.face.y + (event.clientY - d.startY) / rect.height });
    } else {
      // Der Griff bestimmt den Radius: Abstand des Zeigers vom Mittelpunkt, als Anteil der kürzeren Seite.
      const cx = rect.left + d.face.x * rect.width;
      const cy = rect.top + d.face.y * rect.height;
      this.emit({ ...d.face, r: Math.hypot(event.clientX - cx, event.clientY - cy) / Math.min(rect.width, rect.height) });
    }
  }

  up(): void {
    this.drag = null;
  }

  /** Ein Tipp ins Bild (neben den Kreis) setzt den Kreis dorthin. */
  jump(event: MouseEvent): void {
    const face = this.shown();
    if (this.disabled() || !face || (event.target as HTMLElement).closest('.face-ring')) return;
    const rect = this.stage().nativeElement.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    this.emit({ ...face, x: (event.clientX - rect.left) / rect.width, y: (event.clientY - rect.top) / rect.height });
  }

  resize(r: number): void {
    const face = this.shown();
    if (face && !this.disabled()) this.emit({ ...face, r });
  }

  key(event: KeyboardEvent): void {
    const face = this.shown();
    if (this.disabled() || !face) return;
    const step = 0.01;
    const next: Face | null =
      event.key === 'ArrowLeft' ? { ...face, x: face.x - step } :
      event.key === 'ArrowRight' ? { ...face, x: face.x + step } :
      event.key === 'ArrowUp' ? { ...face, y: face.y - step } :
      event.key === 'ArrowDown' ? { ...face, y: face.y + step } :
      event.key === '+' ? { ...face, r: face.r + step } :
      event.key === '-' ? { ...face, r: face.r - step } : null;
    if (!next) return;
    event.preventDefault();
    this.emit(next);
  }
}
