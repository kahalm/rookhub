import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PromptData, PromptDialogComponent, PromptService } from './prompt-dialog.component';

/** Texteingabe statt window.prompt (Codereview W5 F5-016): alle Felder in EINEM Dialog. */
describe('PromptDialogComponent', () => {
  function render(data: PromptData) {
    const ref = { close: jasmine.createSpy('close') };
    TestBed.configureTestingModule({
      imports: [PromptDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: ref },
      ],
    });
    const fixture = TestBed.createComponent(PromptDialogComponent);
    fixture.detectChanges();
    return { fixture, ref, el: fixture.nativeElement as HTMLElement };
  }

  it('zeigt je Feld ein Eingabefeld, vorbelegt, und liefert beim Speichern alle Werte zusammen', async () => {
    const { fixture, ref, el } = render({
      title: 'Titel auf KidHub',
      hint: 'leer = Buchname',
      fields: [
        { key: 'de', label: 'Deutsch', value: 'Alt' },
        { key: 'en', label: 'English' },
        { key: 'hr', label: 'Hrvatski', value: null },
      ],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const inputs = Array.from(el.querySelectorAll('input'));
    expect(inputs.length).toBe(3);
    expect(inputs.map(i => i.value)).toEqual(['Alt', '', '']);
    expect(el.querySelector('h2')?.textContent).toContain('Titel auf KidHub');
    expect(el.textContent).toContain('leer = Buchname');

    inputs[1].value = 'Checkmate';
    inputs[1].dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();

    expect(ref.close).toHaveBeenCalledOnceWith({ de: 'Alt', en: 'Checkmate', hr: '' });
  });

  it('Abbrechen schliesst mit null und schickt das Formular nicht ab', () => {
    const { ref, el } = render({ fields: [{ key: 'name', label: 'Name', value: 'x' }] });
    el.querySelector<HTMLButtonElement>('button[type=button]')!.click();
    expect(ref.close).toHaveBeenCalledOnceWith(null);
  });
});

describe('PromptService', () => {
  function setup(result: unknown) {
    TestBed.configureTestingModule({
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const open = spyOn(TestBed.inject(MatDialog), 'open')
      .and.returnValue({ afterClosed: () => of(result) } as MatDialogRef<unknown>);
    return { service: TestBed.inject(PromptService), open };
  }

  it('reicht die Daten an den Dialog und liefert dessen Werte', () => {
    const { service, open } = setup({ name: 'Neu' });
    let answer: Record<string, string> | null | undefined;
    service.ask({ fields: [{ key: 'name', label: 'Name' }] }).subscribe(r => answer = r);

    expect(answer).toEqual({ name: 'Neu' });
    expect(open.calls.mostRecent().args[0]).toBe(PromptDialogComponent);
    expect((open.calls.mostRecent().args[1]!.data as PromptData).fields[0].key).toBe('name');
  });

  it('Schliessen ohne Ergebnis (Esc, Klick daneben) ergibt null', () => {
    const { service } = setup(undefined);
    let answer: Record<string, string> | null | undefined;
    service.ask({ fields: [{ key: 'name', label: 'Name' }] }).subscribe(r => answer = r);
    expect(answer).toBeNull();
  });
});
