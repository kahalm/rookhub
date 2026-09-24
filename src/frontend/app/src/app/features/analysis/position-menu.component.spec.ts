import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ExternalEngineService } from './external-engine.service';
import { PositionMenuComponent } from './position-menu.component';

describe('PositionMenuComponent', () => {
  const FEN = 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3';
  let fixture: ComponentFixture<PositionMenuComponent>;
  let engines: jasmine.SpyObj<ExternalEngineService>;
  let snackbar: jasmine.SpyObj<SnackbarService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let loggedIn = true;

  function setup(inputs: Record<string, unknown> = {}): void {
    engines = jasmine.createSpyObj<ExternalEngineService>('ExternalEngineService', ['listEngines']);
    engines.listEngines.and.returnValue(of({ hasCredentials: true, tokenInvalid: false, engines: [{ id: 'e1', name: 'Cloud' }], backgroundEngineIds: ['e1'] } as any));
    snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['copy', 'warn', 'success']);
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(null) } as any);
    TestBed.configureTestingModule({
      imports: [PositionMenuComponent],
      providers: [
        provideNoopAnimations(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { get isLoggedIn() { return loggedIn; } } },
        { provide: ExternalEngineService, useValue: engines },
        { provide: SnackbarService, useValue: snackbar },
        { provide: MatDialog, useValue: dialog },
      ],
    });
    fixture = TestBed.createComponent(PositionMenuComponent);
    fixture.componentRef.setInput('fen', FEN);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  function open(): void {
    (fixture.nativeElement.querySelector('.position-menu-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
  }
  const item = (cls: string) => document.querySelector(`.cdk-overlay-container .${cls}`) as HTMLElement | null;

  beforeEach(() => { loggedIn = true; });
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach(c => c.innerHTML = ''));

  it('der Chessable-Eintrag öffnet Chessables FEN-Suche für die Stellung in einem neuen Tab', () => {
    setup();
    open();
    const a = item('pm-chessable') as HTMLAnchorElement;
    expect(a.getAttribute('href')).toBe(
      'https://www.chessable.com/courses/fen/r1bqkbnrUpppp1pppU2n5U4p3U4P3U5N2UPPPP1PPPURNBQKB1R%20w%20KQkq%20-%202%203/');
    expect(a.getAttribute('target')).toBe('_blank');
  });

  it('„Stellung teilen" legt am PC den Link zum Analysebrett in die Zwischenablage — mit Brett-Ausrichtung', async () => {
    setup({ orientation: 'black' });
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    const write = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as any);
    fixture.componentInstance.share();
    await Promise.resolve();
    const url = new URL(write.calls.mostRecent().args[0]);
    expect(url.pathname).toBe('/analysis');
    expect(url.searchParams.get('fen')).toBe(FEN);
    expect(url.searchParams.get('orientation')).toBe('black');
    expect(snackbar.copy).toHaveBeenCalled();
  });

  it('ohne hereingereichte Engines fragt das Menü erst beim Öffnen — und nur einmal', () => {
    setup();
    expect(engines.listEngines).not.toHaveBeenCalled();
    open();
    expect(engines.listEngines).toHaveBeenCalledTimes(1);
    expect(item('pm-background')).not.toBeNull();
    expect(item('pm-jobs')).not.toBeNull();
    fixture.componentInstance.ensureEngines();
    expect(engines.listEngines).toHaveBeenCalledTimes(1);
  });

  it('das Analysebrett reicht seine Engines herein: kein zweiter Abruf, ohne Engines kein Hintergrund-Eintrag', () => {
    setup({ engines: { hasEngines: false, hasBackground: false } });
    open();
    expect(engines.listEngines).not.toHaveBeenCalled();
    expect(item('pm-background')).toBeNull();
    expect(item('pm-share')).not.toBeNull();
  });

  it('ohne Anmeldung gibt es weder Abruf noch Hintergrund-Eintrag', () => {
    loggedIn = false;
    setup();
    open();
    expect(engines.listEngines).not.toHaveBeenCalled();
    expect(item('pm-background')).toBeNull();
  });

  it('„Im Hintergrund analysieren" öffnet den Auftrags-Dialog mit Stellung, Tiefe und Linien', () => {
    setup({ engines: { hasEngines: true, hasBackground: true }, depth: 30, lines: 2 });
    open();
    item('pm-background')!.click();
    const data = dialog.open.calls.mostRecent().args[1]!.data;
    expect(data).toEqual({ fen: FEN, depth: 30, lines: 2, hasBackgroundEngine: true });
  });
});
