import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructHead, ReconstructHeadFormComponent } from './reconstruct-head-form.component';

describe('ReconstructHeadFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructHeadFormComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  it('schreibt in das Objekt der Seite und meldet Speichern/Abbrechen', async () => {
    const head: ReconstructHead = { title: 'Runde 3', white: '', black: '', event: '', playedOn: '', result: '', note: '' };
    const fixture = TestBed.createComponent(ReconstructHeadFormComponent);
    fixture.componentRef.setInput('head', head);
    const events: string[] = [];
    fixture.componentInstance.save.subscribe(() => events.push('save'));
    fixture.componentInstance.cancel.subscribe(() => events.push('cancel'));
    fixture.detectChanges();
    await fixture.whenStable();

    const el: HTMLElement = fixture.nativeElement;
    const title = el.querySelector<HTMLInputElement>('input[maxlength="200"]')!;
    expect(title.value).toBe('Runde 3');
    title.value = 'Runde 4';
    title.dispatchEvent(new Event('input'));
    expect(head.title).withContext('dasselbe Objekt wie in der Seite').toBe('Runde 4');

    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('.form-actions button'));
    buttons[1].click();
    buttons[0].click();
    expect(events).toEqual(['save', 'cancel']);
  });

  it('sperrt Speichern, solange die Seite beschaeftigt ist', () => {
    const fixture = TestBed.createComponent(ReconstructHeadFormComponent);
    fixture.componentRef.setInput('head', { title: '', white: '', black: '', event: '', playedOn: '', result: '', note: '' });
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.form-actions button');
    expect(buttons[1].disabled).toBeTrue();
  });
});
