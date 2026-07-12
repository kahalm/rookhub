import { AfterViewInit, Component, ElementRef, Input, OnChanges, ViewChild } from '@angular/core';
import * as QRCode from 'qrcode';

/**
 * Kleiner, framework-agnostischer QR-Renderer (ersetzt `angularx-qrcode`, das jedem Angular-Major
 * hinterherhinkt und den Upgrade blockierte). Nutzt die reine `qrcode`-Lib und zeichnet auf ein
 * `<canvas>` — bewusst NICHT als `<img src="data:…">`, damit die strikte CSP (`img-src`) unberührt
 * bleibt. Inputs bewusst kompatibel zum bisherigen Aufruf (Breite + Fehlerkorrektur „M").
 */
@Component({
  selector: 'app-qr-code',
  standalone: true,
  template: '<canvas #canvas [attr.aria-label]="\'QR\'"></canvas>',
})
export class QrCodeComponent implements AfterViewInit, OnChanges {
  @Input() data = '';
  @Input() width = 220;

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
    if (!el || !this.data) return;
    QRCode.toCanvas(el, this.data, { width: this.width, errorCorrectionLevel: 'M', margin: 1 })
      .catch(() => { /* ungültige/leere Daten: Canvas bleibt leer */ });
  }
}
