import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ActivityTimerStopDialogComponent, timerNoteMaxLength } from './activity-timer-stop-dialog.component';

describe('ActivityTimerStopDialogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ActivityTimerStopDialogComponent],
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
    const fixture = TestBed.createComponent(ActivityTimerStopDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Codereview N9-006: der Server speichert „{Label} — {Notiz}" in 200 Zeichen; das Feld erlaubte fest
  // 180 Zeichen Notiz, mit langem Label scheiterte das Stoppen (heute: still gekürzt).
  it('limits the note to what fits next to the label', () => {
    expect(timerNoteMaxLength('x'.repeat(100))).toBe(97);
    expect(timerNoteMaxLength('Coaching mit Trainer Huber – Endspieltechnik')).toBe(153);
    expect(timerNoteMaxLength('x'.repeat(300))).toBe(0);
  });

  it('binds the limit to the note input', async () => {
    await TestBed.configureTestingModule({
      imports: [ActivityTimerStopDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: { label: 'L'.repeat(100), startedAtIso: new Date().toISOString(), theme: null } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ActivityTimerStopDialogComponent);
    fixture.detectChanges();
    const inputs = Array.from(fixture.nativeElement.querySelectorAll('input[maxlength]')) as HTMLInputElement[];
    expect(inputs.length).toBe(1);
    expect(inputs[0].getAttribute('maxlength')).toBe('97');

    // Die Grenze steht sichtbar unter dem Feld, statt dass die Eingabe still abbricht.
    const hint = fixture.nativeElement.querySelector('mat-hint.note-count') as HTMLElement;
    expect(hint).toBeTruthy();
    expect(hint.textContent!.trim()).toBe('0 / 97');
    fixture.componentInstance.note = 'abcde';
    fixture.detectChanges();
    expect(hint.textContent!.trim()).toBe('5 / 97');
  });
});
