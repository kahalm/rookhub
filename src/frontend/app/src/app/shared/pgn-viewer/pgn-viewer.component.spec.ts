import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { PgnViewerComponent } from './pgn-viewer.component';

describe('PgnViewerComponent', () => {
  async function setup(data: object = {}) {
    await TestBed.configureTestingModule({
      imports: [PgnViewerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();
    return TestBed.createComponent(PgnViewerComponent);
  }

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const fixture = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Gemeldet 2026-09-23: im Dialog blieb das Brett am PC bei 400 px in einer 900-px-Kiste. Jetzt wächst es mit
  // dem Fenster, die Zugliste hat eine feste Breite daneben. Welche Regeln gelten, entscheidet der Viewport des
  // Karma-Browsers (Media-Query bei 768 px; der Launcher stellt 1400 × 900 ein) — der Spec prüft beide.
  it('uses the window: board follows the viewport formula, move list keeps a fixed width beside it', async () => {
    const fixture = await setup({ pgn: '[White "a"]\n[Black "b"]\n\n1. e4 e5 2. Nf3 Nc6 *' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const board = el.querySelector('.board-wrap') as HTMLElement;
    const moves = el.querySelector('.moves-section') as HTMLElement;
    if (window.innerWidth > 768) {
      const expected = Math.min(720, Math.max(360, Math.min(window.innerHeight - 280, window.innerWidth - 480)));
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(expected));
      expect(Math.round(moves.getBoundingClientRect().width)).toBe(320);
      expect(Math.abs(moves.getBoundingClientRect().top - board.getBoundingClientRect().top)).toBeLessThan(2);
    } else {
      const section = el.querySelector('.board-section') as HTMLElement;
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(section.getBoundingClientRect().width));
      expect(moves.getBoundingClientRect().top).toBeGreaterThanOrEqual(board.getBoundingClientRect().bottom);
    }
  });
});
