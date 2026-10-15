import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { setEinkActive } from '../../core/eink';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { AnalysisBoardComponent } from './analysis-board.component';
import {
  BoardFullscreenButtonComponent,
} from '../../shared/fullscreen/board-fullscreen-button.component';

describe('AnalysisBoardComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('schickt die äußere Hülle ins Vollbild und hängt den Knopf ans Brett', async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    fixture.detectChanges();

    const target: HTMLElement =
      fixture.debugElement.query(By.directive(BoardFullscreenButtonComponent)).componentInstance.target;
    expect(target.classList).toContain('ab-fs-host');
    expect(fixture.nativeElement.querySelector('.ab-fs-host .board-fs-btn')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.ab-wrap .board-fs-btn')).toBeNull();
  });

  // a1-rk-svg-nan-pfeile: Engine-Pfeile auf ein noch ungemessenes/verborgenes Brett ergaben NaN-Koordinaten
  // („<line> attribute x1: Expected length, NaN“). Sie warten jetzt, bis das Brett eine Breite hat.
  it('setzt Pfeile erst, wenn das Brett eine Größe hat — nie mit NaN-Koordinaten', async () => {
    await TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'none';
    host.style.width = '400px';
    document.body.appendChild(host);
    try {
      fixture.detectChanges();
      fixture.componentRef.setInput('shapes', [{ orig: 'e2', dest: 'e4', brush: 'green' }]);
      fixture.detectChanges();
      const nanLines = () => Array.from(host.querySelectorAll('svg line'))
        .filter(l => ['x1', 'y1', 'x2', 'y2'].some(a => (l.getAttribute(a) ?? '').includes('NaN')));
      expect(host.querySelectorAll('svg line').length).withContext('verborgen: noch kein Pfeil').toBe(0);

      host.style.display = 'block';
      await new Promise(r => setTimeout(r, 100));   // ResizeObserver meldet die neue Größe
      expect(host.querySelectorAll('svg line').length).withContext('sichtbar: Pfeil nachgeholt').toBeGreaterThan(0);
      expect(nanLines().length).toBe(0);
    } finally {
      host.remove();
    }
  });

  it('E-Ink: neue Stellung → schwarz/weiß über das Brett (Nachbilder), ein Zug nicht', fakeAsync(() => {
    TestBed.configureTestingModule({
      imports: [AnalysisBoardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    const fixture = TestBed.createComponent(AnalysisBoardComponent);
    fixture.nativeElement.style.cssText = 'display:block;width:320px';
    document.body.appendChild(fixture.nativeElement);
    setEinkActive(true);
    try {
      fixture.componentRef.setInput('fen', 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1');
      fixture.detectChanges();
      fixture.componentRef.setInput('fen', 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
      fixture.detectChanges();
      expect(document.querySelectorAll('.eink-flash').length).toBe(0);
      fixture.componentRef.setInput('fen', '8/8/4k3/8/8/4K3/4P3/8 w - - 0 1');
      fixture.detectChanges();
      expect(document.querySelectorAll('.eink-flash').length).toBe(1);
      tick(1000);
      expect(document.querySelectorAll('.eink-flash').length).toBe(0);
    } finally {
      setEinkActive(false);
      fixture.destroy();
      fixture.nativeElement.remove();
    }
  }));
});
