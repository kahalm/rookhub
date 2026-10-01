import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { SolveModeDialogComponent, SolveModeDialogData } from './solve-mode-dialog.component';

// UX-004: „für alle Puzzle-Bereiche übernehmen“ — sonst kam dieselbe Frage in jedem Bereich einzeln.
describe('SolveModeDialogComponent', () => {
  function render(data: SolveModeDialogData): { el: HTMLElement; close: jasmine.Spy } {
    const close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [SolveModeDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    const fixture = TestBed.createComponent(SolveModeDialogComponent);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, close };
  }
  const choices = (el: HTMLElement) => el.querySelectorAll<HTMLButtonElement>('.sm-choice');

  it('bietet „für alle“ an (voreingestellt an) und meldet es mit der Wahl', () => {
    const { el, close } = render({ offerApplyAll: true });
    const box = el.querySelector<HTMLInputElement>('.sm-all input[type=checkbox]')!;
    expect(box.checked).toBeTrue();
    choices(el)[1].click();
    expect(close).toHaveBeenCalledWith({ mode: 'easy', applyAll: true });
  });

  it('Häkchen weg → gilt nur für diesen Bereich', () => {
    const { el, close } = render({ offerApplyAll: true });
    el.querySelector<HTMLInputElement>('.sm-all input[type=checkbox]')!.click();
    choices(el)[0].click();
    expect(close).toHaveBeenCalledWith({ mode: 'training', applyAll: false });
  });

  it('ohne Angebot (Kurs) kein Häkchen und kein „für alle“', () => {
    const { el, close } = render({});
    expect(el.querySelector('.sm-all')).toBeNull();
    choices(el)[0].click();
    expect(close).toHaveBeenCalledWith({ mode: 'training', applyAll: false });
  });
});
