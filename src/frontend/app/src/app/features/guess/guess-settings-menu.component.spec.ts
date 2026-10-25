import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessSettingsMenuComponent } from './guess-settings-menu.component';
import { GuessAccept } from './guess.service';

describe('GuessSettingsMenuComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuessSettingsMenuComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  it('aendert das uebergebene accept-Objekt und meldet es zum Speichern', async () => {
    const accept: GuessAccept = { better: true, similar: true };
    const fixture = TestBed.createComponent(GuessSettingsMenuComponent);
    fixture.componentRef.setInput('accept', accept);
    let saved = 0;
    fixture.componentInstance.changed.subscribe(() => saved++);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const boxes = document.querySelectorAll<HTMLInputElement>('.set-box input[type=checkbox]');
    expect(boxes.length).toBe(2);
    boxes[0].click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(accept.better).withContext('dasselbe Objekt wie in der Seite').toBeFalse();
    expect(accept.similar).toBeTrue();
    expect(saved).toBe(1);
  });
});
