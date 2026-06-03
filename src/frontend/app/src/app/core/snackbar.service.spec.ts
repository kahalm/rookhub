import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from './snackbar.service';

describe('SnackbarService', () => {
  let svc: SnackbarService;
  let openSpy: jasmine.Spy;

  beforeEach(() => {
    const ref = {} as any;
    openSpy = jasmine.createSpy('open').and.returnValue(ref);
    TestBed.configureTestingModule({
      providers: [
        SnackbarService,
        { provide: MatSnackBar, useValue: { open: openSpy } },
        // Übersetzt einen Key zu "T:<key>", damit Aktions-Label klar erkennbar ist.
        { provide: TranslateService, useValue: { instant: (k: string) => `T:${k}` } },
      ],
    });
    svc = TestBed.inject(SnackbarService);
  });

  it('info: 3000 ms + übersetzte Standard-Aktion "common.close"', () => {
    svc.info('Hallo');
    expect(openSpy).toHaveBeenCalledWith('Hallo', 'T:common.close', { duration: 3000 });
  });

  it('success: 2000 ms', () => {
    svc.success('OK');
    expect(openSpy).toHaveBeenCalledWith('OK', 'T:common.close', { duration: 2000 });
  });

  it('quick: 1500 ms', () => {
    svc.quick('Toggle');
    expect(openSpy).toHaveBeenCalledWith('Toggle', 'T:common.close', { duration: 1500 });
  });

  it('warn: 5000 ms', () => {
    svc.warn('Achtung');
    expect(openSpy).toHaveBeenCalledWith('Achtung', 'T:common.close', { duration: 5000 });
  });

  it('copy: 2000 ms ohne Aktions-Schaltfläche', () => {
    svc.copy('Kopiert');
    expect(openSpy).toHaveBeenCalledWith('Kopiert', '', { duration: 2000 });
  });

  it('eigener Aktions-Key wird übersetzt, Dauer überschreibbar', () => {
    svc.info('Msg', { action: 'common.ok', duration: 2500 });
    expect(openSpy).toHaveBeenCalledWith('Msg', 'T:common.ok', { duration: 2500 });
  });

  it('action "" → keine Schaltfläche', () => {
    svc.show('Msg', { action: '' });
    expect(openSpy).toHaveBeenCalledWith('Msg', '', { duration: 3000 });
  });

  it('rawAction → Label wörtlich (nicht übersetzt)', () => {
    svc.show('Invalid FEN', { action: 'OK', rawAction: true, duration: 2500 });
    expect(openSpy).toHaveBeenCalledWith('Invalid FEN', 'OK', { duration: 2500 });
  });

  it('duration 0 → bleibt stehen (persistenter Hinweis)', () => {
    svc.show('Update', { action: 'app.reload', duration: 0 });
    expect(openSpy).toHaveBeenCalledWith('Update', 'T:app.reload', { duration: 0 });
  });

  it('gibt die MatSnackBarRef zurück (für onAction)', () => {
    const ref = svc.show('Msg');
    expect(ref).toBeDefined();
  });
});
