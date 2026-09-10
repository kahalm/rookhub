import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CreateCourseDialogComponent } from './create-course-dialog.component';

describe('CreateCourseDialogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CreateCourseDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CreateCourseDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  /** Der Partien-Import ist derselbe Dialog mit `mode: 'games'` — dort ist die Datei PFLICHT,
   *  denn ein „Import" ohne PGN waere nur ein leerer Kurs unter falschem Namen. */
  async function makeGames() {
    await TestBed.configureTestingModule({
      imports: [CreateCourseDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
        { provide: MAT_DIALOG_DATA, useValue: { mode: 'games' } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CreateCourseDialogComponent);
    fixture.detectChanges();
    return { c: fixture.componentInstance, ref: TestBed.inject(MatDialogRef) as unknown as { close: jasmine.Spy } };
  }

  function pgn(name: string): File {
    return new File(['[Event "x"]\n\n1. e4 e5 *\n'], name, { type: 'application/x-chess-pgn' });
  }

  function choose(c: CreateCourseDialogComponent, file: File | null): void {
    c.onFileSelected({ target: { files: file ? [file] : [] } } as unknown as Event);
  }

  it('Partien-Import verlangt eine Datei', async () => {
    const { c, ref } = await makeGames();
    expect(c.games).toBeTrue();
    c.name = 'Meine Partien';
    expect(c.canSubmit()).toBeFalse();       // Name allein genuegt hier nicht
    c.submit();
    expect(ref.close).not.toHaveBeenCalled();

    choose(c, pgn('irgendwas.pgn'));
    expect(c.canSubmit()).toBeTrue();
    c.submit();
    expect(ref.close).toHaveBeenCalled();
  });

  it('leerer Kurs braucht dagegen KEINE Datei', async () => {
    const { c, ref } = await make();
    expect(c.games).toBeFalse();
    c.name = 'Turmendspiele';
    expect(c.canSubmit()).toBeTrue();
    c.submit();
    expect(ref.close).toHaveBeenCalledWith({ name: 'Turmendspiele', file: null });
  });

  it('schlaegt den Namen aus dem Dateinamen vor', async () => {
    const { c } = await makeGames();
    choose(c, pgn('meine-partien_2026.pgn'));
    expect(c.name).toBe('meine partien 2026');
  });

  it('ueberschreibt einen getippten Namen NICHT', async () => {
    const { c } = await makeGames();
    c.name = 'Von Hand';
    choose(c, pgn('egal.pgn'));
    expect(c.name).toBe('Von Hand');
  });

  async function make() {
    await TestBed.configureTestingModule({
      imports: [CreateCourseDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CreateCourseDialogComponent);
    fixture.detectChanges();
    return { c: fixture.componentInstance, ref: TestBed.inject(MatDialogRef) as unknown as { close: jasmine.Spy } };
  }

  it('legt ohne PGN an — der Name allein genügt', async () => {
    const { c, ref } = await make();
    c.name = '  Mein Kurs  ';
    c.submit();
    expect(ref.close).toHaveBeenCalledWith({ name: 'Mein Kurs', file: null });
  });

  it('reicht ein angehängtes PGN durch und lässt es wieder entfernen', async () => {
    const { c, ref } = await make();
    const file = new File(['[Event "x"]'], 'kurs.pgn');
    c.name = 'Mit PGN';
    c.file = file;
    c.submit();
    expect(ref.close).toHaveBeenCalledWith({ name: 'Mit PGN', file });

    const input = { value: 'kurs.pgn' } as HTMLInputElement;
    c.clearFile(input);
    expect(c.file).toBeNull();
    expect(input.value).toBe('');   // sonst löst dieselbe Datei kein change-Ereignis mehr aus
  });

  it('legt ohne Namen nichts an', async () => {
    const { c, ref } = await make();
    c.name = '   ';
    c.submit();
    expect(ref.close).not.toHaveBeenCalled();
  });
});
