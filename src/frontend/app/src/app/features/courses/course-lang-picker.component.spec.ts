import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CourseLangPickerComponent, MachineNoteComponent } from './course-lang-picker.component';

describe('CourseLangPickerComponent', () => {
  function render(languages: string[], value: string | null) {
    TestBed.configureTestingModule({
      imports: [CourseLangPickerComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(CourseLangPickerComponent);
    fixture.componentRef.setInput('languages', languages);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    return fixture;
  }

  it('ist unsichtbar, solange der Kurs nur EINE Sprache hat', () => {
    const f = render(['en'], 'en');
    expect(f.nativeElement.querySelector('button')).toBeNull();
  });

  it('nennt die Quelle „Original" und andere Sprachen beim Namen der Sprachauswahl', () => {
    const f = render(['en', 'de'], 'en');
    const c = f.componentInstance;
    expect(f.nativeElement.querySelector('button')).not.toBeNull();
    expect(c.isSource('en')).toBeTrue();
    expect(c.name('de')).toBe('Deutsch');
    expect(c.sourceName()).toBe('English');
    expect(f.nativeElement.textContent).toContain('courses.lang.original');
  });

  it('eine unbekannte gewünschte Sprache zeigt das Original als aktiv', () => {
    const f = render(['en', 'de'], 'fr');
    expect(f.componentInstance.active()).toBe('en');
  });

  it('meldet nur einen WECHSEL', () => {
    const f = render(['en', 'de'], 'de');
    const picked: string[] = [];
    f.componentInstance.picked.subscribe(v => picked.push(v));
    f.componentInstance.pick('de');
    f.componentInstance.pick('en');
    expect(picked).toEqual(['en']);
  });
});

describe('MachineNoteComponent', () => {
  it('schaltet per Klick aufs Original, ohne Quelle nur Text', () => {
    TestBed.configureTestingModule({
      imports: [MachineNoteComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MachineNoteComponent);
    const clicks: number[] = [];
    f.componentInstance.original.subscribe(() => clicks.push(1));
    f.detectChanges();
    (f.nativeElement.querySelector('button') as HTMLButtonElement).click();
    expect(clicks.length).toBe(1);

    f.componentRef.setInput('clickable', false);
    f.detectChanges();
    expect(f.nativeElement.querySelector('button')).toBeNull();
    expect(f.nativeElement.textContent).toContain('courses.lang.machine');
  });
});
