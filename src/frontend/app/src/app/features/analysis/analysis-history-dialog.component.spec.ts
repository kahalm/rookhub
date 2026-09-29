import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef } from '@angular/material/dialog';
import { AnalysisHistoryDialogComponent } from './analysis-history-dialog.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const entry = (id: number, extra: object = {}) => ({
  id, startFen: START, moves: ['e2e4', 'e7e5'], ply: 2, title: null, starred: [], preview: '1.e4 e5', moveCount: 2,
  createdAt: '2026-09-29T18:00:00Z', updatedAt: '2026-09-29T18:05:00Z', ...extra,
});

describe('AnalysisHistoryDialogComponent', () => {
  function make() {
    const ref = jasmine.createSpyObj<MatDialogRef<AnalysisHistoryDialogComponent>>('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [AnalysisHistoryDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }), { provide: MatDialogRef, useValue: ref }],
    });
    const fixture = TestBed.createComponent(AnalysisHistoryDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController), ref };
  }

  it('lists the entries with title or origin, preview and star count; a click chooses one', () => {
    const { fixture, c, http, ref } = make();
    http.expectOne('/api/analysis-history').flush([
      entry(1, { title: 'Carlsen – Nakamura', starred: [1, 2] }),
      entry(2, { startFen: '8/8/8/8/8/8/k7/K7 w - - 0 1', moves: [], preview: '', moveCount: 0 }),
    ]);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Carlsen – Nakamura');
    expect(text).toContain('★ 2');
    expect(text).toContain('1.e4 e5');
    expect(text).toContain('analysis.history.fromPosition');
    (fixture.nativeElement.querySelector('.pick') as HTMLButtonElement).click();
    expect(ref.close).toHaveBeenCalledWith(jasmine.objectContaining({ id: 1 }));
    expect(c.fromStart(entry(1) as any)).toBeTrue();
  });

  it('deletes an entry', () => {
    const { fixture, c, http } = make();
    http.expectOne('/api/analysis-history').flush([entry(1), entry(2)]);
    c.remove(c.entries()[0]);
    const del = http.expectOne('/api/analysis-history/1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    fixture.detectChanges();
    expect(c.entries().map(e => e.id)).toEqual([2]);
  });
});
