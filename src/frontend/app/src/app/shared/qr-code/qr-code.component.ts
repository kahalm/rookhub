import { AfterViewInit, Component, ElementRef, OnChanges, ViewChild, ChangeDetectionStrategy, input } from '@angular/core';
import * as QRCode from 'qrcode';

/**
 * Ruhezone in Modulen (d9-rk-qr-ruhezone-zu-klein, DESIGN 5.6): der QR-Standard verlangt 4 Module Weiß um den Code.
 * Mit 1 Modul lag die dunkle Dialogfläche direkt am Code, und manche Scanner fanden ihn nicht.
 */
export const QR_QUIET_ZONE = 4;

/**
 * Kleiner, framework-agnostischer QR-Renderer (ersetzt `angularx-qrcode`, das jedem Angular-Major
 * hinterherhinkt und den Upgrade blockierte). Nutzt die reine `qrcode`-Lib und zeichnet auf ein
 * `<canvas>` — bewusst NICHT als `<img src="data:…">`, damit die strikte CSP (`img-src`) unberührt
 * bleibt. Inputs bewusst kompatibel zum bisherigen Aufruf (Breite + Fehlerkorrektur „M").
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-qr-code',
  standalone: true,
  template: '<canvas #canvas [attr.aria-label]="\'QR\'"></canvas>',
})
export class QrCodeComponent implements AfterViewInit, OnChanges {
  readonly data = input('');
  readonly width = input(220);

  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLCanvasElement>;
  private viewReady = false;

  ngAfterViewInit(): void {
    this.viewReady = true;
    this.render();
  }

  ngOnChanges(): void {
    if (this.viewReady) this.render();
  }

  private render(): void {
    const el = this.canvas?.nativeElement;
    if (!el || !this.data()) return;
    QRCode.toCanvas(el, this.data(), { width: this.width(), errorCorrectionLevel: 'M', margin: QR_QUIET_ZONE })
      .catch(() => { /* ungültige/leere Daten: Canvas bleibt leer */ });
  }
}
