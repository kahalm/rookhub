import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleYourTurnComponent } from './puzzle-your-turn.component';

describe('PuzzleYourTurnComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('showStatus=false blendet NUR die Statuszeile aus (Timer + Aktionen bleiben)', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    const el: HTMLElement = fixture.nativeElement;
    // setInput statt Feldzuweisung: die Komponente ist OnPush und würde sonst nicht neu rendern.
    fixture.componentRef.setInput('timerSeconds', 42);
    fixture.detectChanges();
    expect(el.querySelector('.ytp-status')).toBeTruthy();

    fixture.componentRef.setInput('showStatus', false);
    fixture.detectChanges();
    expect(el.querySelector('.ytp-status')).toBeNull();     // Doppelaussage zum Brett-Hinweis weg
    expect(el.querySelector('.ytp-timer')).toBeTruthy();    // Zeit bleibt
    expect(el.querySelectorAll('.ytp-actions button').length).toBeGreaterThan(0);
  });
});
