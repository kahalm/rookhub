import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RememberedLinesComponent } from './remembered-lines.component';
import { AuthService } from '../../core/auth.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('RememberedLinesComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [RememberedLinesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RememberedLinesComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('RememberedLinesComponent Analyse-Info', () => {
  it('zeigt Status, Tiefe und Bewertung des Auftrags an der Karte und bietet sonst „Im Hintergrund analysieren"', async () => {
    await TestBed.configureTestingModule({
      imports: [RememberedLinesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }), { provide: AuthService, useValue: { isLoggedIn: true } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(RememberedLinesComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url.startsWith('/api/extension/remembered-lines')).flush([
      { id: 1, fen: START, courseId: null, courseName: 'Kritisch', sourceUrl: '/analysis/jobs', createdAt: '2026-08-26T10:00:00Z',
        analysis: { jobId: 5, status: 'paused', reachedDepth: 27, targetDepth: 40, multiPv: 3, evalText: '-0.45', updatedAt: '2026-08-26T10:05:00Z' } },
      { id: 2, fen: START, courseId: '99', courseName: 'Kurs', sourceUrl: 'https://www.chessable.com/x', createdAt: '2026-08-26T09:00:00Z', analysis: null },
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('analysisJobs.status.paused');
    expect(text).toContain('-0.45');
    expect(fixture.nativeElement.querySelectorAll('a.analysis').length).toBe(1);
    // Nur die Karte OHNE Auftrag bekommt den Uhr-Knopf
    const clocks = Array.from(fixture.nativeElement.querySelectorAll('mat-icon')).filter((m: any) => m.textContent.trim() === 'schedule');
    expect(clocks.length).toBe(1);
    expect(fixture.componentInstance.labelOf(fixture.componentInstance.items[0])).toBe('Kritisch');
    expect(fixture.componentInstance.labelOf({ ...fixture.componentInstance.items[0], courseName: null })).toBe('remembered.analysisOrigin');
  });
});
