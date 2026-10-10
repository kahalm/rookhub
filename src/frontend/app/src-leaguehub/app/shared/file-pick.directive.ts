import { AfterViewInit, Directive, ElementRef, OnDestroy, Renderer2, inject, input } from '@angular/core';

/** Text neben dem Knopf: nichts gewählt, ein Dateiname oder „3 Dateien gewählt". */
export function filePickText(files: FileList | readonly { name: string }[] | null | undefined): string {
  const n = files?.length ?? 0;
  if (!n) return 'Keine Datei gewählt';
  return n === 1 ? files![0].name : `${n} Dateien gewählt`;
}

/**
 * Deutsche Dateiauswahl statt „Choose File / No file chosen" (UI-Sweep 2026-10-10, l-filepicker): das native Feld bleibt
 * (Bindungen, `(change)`, Tests, Tastatur), wird aber unsichtbar; daneben setzt die Direktive einen Umriss-Knopf
 * „📎 Datei wählen" und den gewählten Dateinamen. Steht das Feld in einem `<label>`, öffnet ein Klick auf den Knopf die
 * Auswahl über das Label, sonst über `click()`. Den Namen frischt `change` auf — setzt die Seite den Wert selbst zurück,
 * sorgt `input` bzw. der nächste Wechsel dafür.
 */
@Directive({
  selector: 'input[type=file][lhFilePick]',
  standalone: true,
  host: { class: 'file-native', '(change)': 'refresh()', '(input)': 'refresh()' },
})
export class FilePickDirective implements AfterViewInit, OnDestroy {
  /** Beschriftung des Knopfs. */
  readonly lhFilePick = input<string>('');
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  private readonly r = inject(Renderer2);
  private wrap: HTMLElement | null = null;
  private name: HTMLElement | null = null;
  private unlisten: (() => void) | null = null;

  ngAfterViewInit(): void {
    const input = this.el.nativeElement;
    const parent = input.parentNode;
    if (!parent) return;
    this.wrap = this.r.createElement('span');
    this.r.addClass(this.wrap, 'file-pick');
    const btn = this.r.createElement('span');
    this.r.addClass(btn, 'btn-sec');
    this.r.addClass(btn, 'file-btn');
    this.r.setAttribute(btn, 'aria-hidden', 'true');
    this.r.appendChild(btn, this.r.createText(`📎 ${this.lhFilePick() || (input.multiple ? 'Dateien wählen' : 'Datei wählen')}`));
    this.name = this.r.createElement('span');
    this.r.addClass(this.name, 'file-name');
    // Das Feld selbst bleibt, wo Angular es angelegt hat; Knopf und Name kommen direkt dahinter.
    this.r.insertBefore(parent, this.wrap, input.nextSibling);
    this.r.appendChild(this.wrap, btn);
    this.r.appendChild(this.wrap, this.name);
    if (!input.closest('label')) this.unlisten = this.r.listen(btn, 'click', () => input.click());
    this.refresh();
  }

  refresh(): void {
    if (this.name) this.name.textContent = filePickText(this.el.nativeElement.files);
  }

  ngOnDestroy(): void {
    this.unlisten?.();
    this.wrap?.remove();
  }
}
