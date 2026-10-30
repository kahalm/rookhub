import { TestBed } from '@angular/core/testing';
import * as QRCode from 'qrcode';
import { QR_QUIET_ZONE, QrCodeComponent } from './qr-code.component';

describe('QrCodeComponent', () => {
  it('renders a canvas without throwing for given data', async () => {
    await TestBed.configureTestingModule({ imports: [QrCodeComponent] }).compileComponents();
    const f = TestBed.createComponent(QrCodeComponent);
    f.componentRef.setInput('data', 'https://rookhub.example/g/abc');
    f.componentRef.setInput('width', 160);
    f.detectChanges();
    expect(f.nativeElement.querySelector('canvas')).toBeTruthy();
  });

  it('handles empty data gracefully', async () => {
    await TestBed.configureTestingModule({ imports: [QrCodeComponent] }).compileComponents();
    const f = TestBed.createComponent(QrCodeComponent);
    f.componentRef.setInput('data', '');
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });

  it('d9: 4 Module weiße Ruhezone rund um den Code (QR-Standard), auch auf dunklem Grund', async () => {
    await TestBed.configureTestingModule({ imports: [QrCodeComponent] }).compileComponents();
    const f = TestBed.createComponent(QrCodeComponent);
    const url = 'https://rookhub.oberschmid.homes/puzzles/12345?share=abcdef';
    f.componentRef.setInput('data', url);
    f.componentRef.setInput('width', 220);
    f.detectChanges();
    await new Promise(r => setTimeout(r, 50));
    expect(QR_QUIET_ZONE).toBe(4);

    const canvas = f.nativeElement.querySelector('canvas') as HTMLCanvasElement;
    const size = QRCode.create(url, { errorCorrectionLevel: 'M' }).modules.size;
    const px = canvas.width / (size + 2 * QR_QUIET_ZONE);   // Pixel je Modul
    const band = Math.floor(QR_QUIET_ZONE * px) - 1;         // Rand, der weiß sein muss
    const img = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data;
    const dark = (x: number, y: number) => img[(y * canvas.width + x) * 4] < 128;
    let darkInBand = 0;
    for (let y = 0; y < canvas.height; y++) {
      for (let x = 0; x < canvas.width; x++) {
        const inBand = x < band || y < band || x >= canvas.width - band || y >= canvas.height - band;
        if (inBand && dark(x, y)) darkInBand++;
      }
    }
    expect(band).toBeGreaterThan(8);
    expect(darkInBand).toBe(0);
    // und der Code selbst ist gezeichnet (Suchmuster oben links direkt hinter der Ruhezone)
    expect(dark(Math.ceil(QR_QUIET_ZONE * px) + 1, Math.ceil(QR_QUIET_ZONE * px) + 1)).toBeTrue();
  });
});
