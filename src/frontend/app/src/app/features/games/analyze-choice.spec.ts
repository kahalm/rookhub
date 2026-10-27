import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AnalyzeGameService } from './analyze-game.service';

describe('AnalyzeGameService — mit/ohne Engine (0.855.0)', () => {
  let svc: AnalyzeGameService;
  let http: HttpTestingController;
  let dialogResult: unknown;
  let opened = 0;

  beforeEach(() => {
    opened = 0;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialog, useValue: { open: () => { opened++; return { afterClosed: () => of(dialogResult) }; } } }],
    });
    svc = TestBed.inject(AnalyzeGameService);
    http = TestBed.inject(HttpTestingController);
  });

  it('ohne Vorgabe fragt das Fenster; „merken" speichert die Wahl je Nutzer', () => {
    dialogResult = { mode: 'blind', remember: true };
    let got: string | null = null;
    svc.choose().subscribe(c => got = c);
    http.expectOne('/api/view-state/analyze.choice').flush(null, { status: 204, statusText: 'No Content' });
    expect(opened).toBe(1);
    expect(got as string | null).toBe('blind');
    const put = http.expectOne(r => r.method === 'PUT' && r.url === '/api/view-state/analyze.choice');
    expect(put.request.body).toEqual({ mode: 'blind' });
    put.flush({});
    svc.choose().subscribe(c => got = c);   // Vorgabe gilt: kein Fenster, keine Anfrage
    expect(opened).toBe(1);
    http.verify();
  });

  it('gespeicherte Vorgabe „mit Engine": kein Fenster', () => {
    let got: string | null = null;
    svc.choose().subscribe(c => got = c);
    http.expectOne('/api/view-state/analyze.choice').flush({ mode: 'engine' });
    expect(opened).toBe(0);
    expect(got as string | null).toBe('engine');
  });

  it('ohne Engine: rechnet still im Hintergrund und öffnet das Analysebrett mit ?blind=1', () => {
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    svc.mode.set('blind');
    (svc as unknown as { modeLoaded: boolean }).modeLoaded = true;
    let started: boolean | null = null;
    svc.analyze('/api/games/4/analyze', null, '1. e4 *').subscribe(s => started = s);
    expect(started as boolean | null).toBeFalse();
    expect(http.expectOne(r => r.url.startsWith('/api/games/4/analyze')).request.method).toBe('POST');
    expect(nav).toHaveBeenCalledWith(['/analysis'], { state: { pgn: '1. e4 *', blind: true }, queryParams: { blind: 1 } });
  });
});
