import { TestBed } from '@angular/core/testing';
import { QrCodeComponent } from './qr-code.component';

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
});
