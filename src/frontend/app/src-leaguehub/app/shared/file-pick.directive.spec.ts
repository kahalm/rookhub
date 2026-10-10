import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FilePickDirective, filePickText } from './file-pick.directive';

@Component({
  standalone: true,
  imports: [FilePickDirective],
  template: `
    <label class="field">PGN-Datei <input type="file" lhFilePick id="one" (change)="changed.set(changed() + 1)" /></label>
    <label class="field">Bilder <input type="file" lhFilePick multiple id="many" /></label>
    <input type="file" lhFilePick="Foto wählen" id="free" />`,
})
class HostComponent {
  readonly changed = signal(0);
}

/** UI-Sweep 2026-10-10 (l-filepicker): deutsche Dateiauswahl statt „Choose File / No file chosen". */
describe('FilePickDirective', () => {
  function create(): HTMLElement {
    const f = TestBed.createComponent(HostComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  it('Text: nichts, ein Name, mehrere', () => {
    expect(filePickText(null)).toBe('Keine Datei gewählt');
    expect(filePickText([])).toBe('Keine Datei gewählt');
    expect(filePickText([{ name: 'runde3.pgn' }])).toBe('runde3.pgn');
    expect(filePickText([{ name: 'a.jpg' }, { name: 'b.jpg' }, { name: 'c.jpg' }])).toBe('3 Dateien gewählt');
  });

  it('setzt Knopf und „Keine Datei gewählt" hinter das (versteckte) Feld und zeigt den gewählten Namen', () => {
    const el = create();
    const one = el.querySelector('#one') as HTMLInputElement;
    expect(one.classList).toContain('file-native');
    const pick = one.nextElementSibling as HTMLElement;
    expect(pick.classList).toContain('file-pick');
    expect(pick.querySelector('.file-btn')?.textContent).toBe('📎 Datei wählen');
    expect(pick.querySelector('.file-btn')?.getAttribute('aria-hidden')).toBe('true');
    expect(pick.querySelector('.file-name')?.textContent).toBe('Keine Datei gewählt');
    expect((el.querySelector('#many')!.nextElementSibling as HTMLElement).querySelector('.file-btn')?.textContent).toBe('📎 Dateien wählen');

    const dt = new DataTransfer();
    dt.items.add(new File(['1. e4 *'], 'runde3.pgn'));
    one.files = dt.files;
    one.dispatchEvent(new Event('change'));
    expect(pick.querySelector('.file-name')?.textContent).toBe('runde3.pgn');
  });

  it('ohne umgebendes Label öffnet der Knopf die Auswahl selbst', () => {
    const el = create();
    const free = el.querySelector('#free') as HTMLInputElement;
    const click = spyOn(free, 'click');
    const btn = (free.nextElementSibling as HTMLElement).querySelector('.file-btn') as HTMLElement;
    expect(btn.textContent).toBe('📎 Foto wählen');
    btn.click();
    expect(click).toHaveBeenCalled();
  });
});
