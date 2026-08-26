import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AnalysisJobDialogComponent } from './analysis-job-dialog.component';

const FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

describe('AnalysisJobDialogComponent', () => {
  async function make(hasBackgroundEngine = true, depth = 22, lines = 3) {
    const ref = { close: jasmine.createSpy('close') };
    await TestBed.configureTestingModule({
      imports: [AnalysisJobDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: ref },
        { provide: MAT_DIALOG_DATA, useValue: { fen: FEN, depth, lines, hasBackgroundEngine } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisJobDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, ref, http: TestBed.inject(HttpTestingController) };
  }

  it('lifts the live depth to the next offered step and keeps a known line count', async () => {
    const { c } = await make(true, 22, 3);
    expect(c.depth).toBe(24);
    expect(c.lines).toBe(3);
  });

  it('posts the job and closes with the created job', async () => {
    const { c, ref, http } = await make();
    c.title = ' Kritische Stellung ';
    c.submit();
    const req = http.expectOne('/api/analysis-jobs');
    expect(req.request.body).toEqual({ fen: FEN, targetDepth: 24, multiPv: 3, title: 'Kritische Stellung' });
    const job = { id: 1, fen: FEN, status: 'queued' };
    req.flush(job);
    expect(ref.close).toHaveBeenCalledWith(jasmine.objectContaining({ id: 1 }));
  });

  it('batch mode posts all positions at once and closes with the batch result', async () => {
    const ref = { close: jasmine.createSpy('close') };
    await TestBed.configureTestingModule({
      imports: [AnalysisJobDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }), { provide: MatDialogRef, useValue: ref },
        { provide: MAT_DIALOG_DATA, useValue: { fens: [FEN, 'x'], depth: 30, lines: 3, hasBackgroundEngine: true } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisJobDialogComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.batch).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('analysisJobs.dialog.batchTitle');
    expect(fixture.nativeElement.querySelector('input[matInput]')).toBeNull();   // kein Titel im Mehrfach-Modus

    c.submit();
    const req = TestBed.inject(HttpTestingController).expectOne('/api/analysis-jobs/batch');
    expect(req.request.body).toEqual({ fens: [FEN, 'x'], targetDepth: 30, multiPv: 3 });
    req.flush({ created: [{ id: 1 }], skipped: [{ fen: 'x', reason: 'invalid' }] });
    expect(ref.close).toHaveBeenCalledWith(jasmine.objectContaining({ created: [{ id: 1 }] }));
  });

  it('without a background engine it only explains and never posts', async () => {
    const { c, fixture, http } = await make(false);
    expect(fixture.nativeElement.textContent).toContain('analysisJobs.dialog.noEngine');
    c.submit();
    http.expectNone('/api/analysis-jobs');
  });
});
