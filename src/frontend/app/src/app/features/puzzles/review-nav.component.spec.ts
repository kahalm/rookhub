import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReviewNavComponent } from './review-nav.component';

describe('ReviewNavComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ReviewNavComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ReviewNavComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Delta-Review 2026-11-09, F1-015: ◀/▶ hatten keinen zugänglichen Namen (mat-icon ist aria-hidden).
  it('◀/▶ tragen einen zugänglichen Namen', async () => {
    await TestBed.configureTestingModule({
      imports: [ReviewNavComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ReviewNavComponent);
    fixture.detectChanges();
    const labels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['pgnViewer.nav.previous', 'pgnViewer.nav.next']);
  });
});
