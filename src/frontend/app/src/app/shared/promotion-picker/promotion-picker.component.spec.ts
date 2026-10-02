import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PromotionPickerComponent } from './promotion-picker.component';
import { Key } from 'chessground/types';

describe('PromotionPickerComponent', () => {
  function create(dest: Key, orientation: 'white' | 'black' = 'white', color: 'w' | 'b' = 'w'): PromotionPickerComponent {
    const comp = new PromotionPickerComponent();
    comp.dest = dest;
    comp.orientation = orientation;
    comp.color = color;
    comp.ngOnInit();
    return comp;
  }

  it('positioniert die Auswahl auf der Umwandlungs-Datei (weiße Orientierung)', () => {
    const comp = create('a8' as Key);
    expect(comp.filePercent).toBe(0);
    expect(comp.fromBottom).toBeFalse();
  });

  it('spiegelt die Datei bei gedrehtem Brett', () => {
    const comp = create('a1' as Key, 'black', 'b');
    // a -> fileIndex 0 -> bei schwarzer Orientierung gespiegelt: 7-0 = 7 -> 87.5 %
    expect(comp.filePercent).toBe(87.5);
    expect(comp.fromBottom).toBeFalse();         // black-Orientierung + Rang 1 -> nicht von unten
  });

  it('Rang-1-Umwandlung erscheint bei weißer Orientierung von unten', () => {
    const comp = create('h1' as Key, 'white');
    expect(comp.filePercent).toBe(87.5);        // h -> 7 -> 87.5
    expect(comp.fromBottom).toBeTrue();
  });

  it('verwirft den Ghost-Tap (choose) innerhalb des Guard-Fensters', () => {
    const comp = create('a8' as Key);
    const choose = spyOn(comp.choose, 'emit');
    comp.onChoose('q');
    expect(choose).not.toHaveBeenCalled();
  });

  it('verwirft den Ghost-Tap (dismiss) innerhalb des Guard-Fensters', () => {
    const comp = create('a8' as Key);
    const dismiss = spyOn(comp.dismiss, 'emit');
    comp.onDismiss();
    expect(dismiss).not.toHaveBeenCalled();
  });

  it('emittiert die gewählte Figur nach Ablauf des Guard-Fensters', () => {
    const comp = create('a8' as Key);
    (comp as unknown as { guardUntil: number }).guardUntil = Date.now() - 1;
    const choose = spyOn(comp.choose, 'emit');
    comp.onChoose('n');
    expect(choose).toHaveBeenCalledWith('n');
  });

  it('liefert die korrekte Figurengrafik je Farbe', () => {
    expect(create('a8' as Key, 'white', 'w').image('q')).toBe(`url('/piece/cburnett/wQ.svg')`);
    expect(create('a1' as Key, 'white', 'b').image('r')).toBe(`url('/piece/cburnett/bR.svg')`);
  });
});

// Codereview F8-018: Die vier Figuren waren <div (click)> ohne Beschriftung, Esc brach (außer im Puzzle-Brett) nicht ab —
// per Tastatur ließ sich im offenen Wähler keine Figur wählen, ein Screenreader las keine vor. Den Umwandlungszug selbst
// zieht weiterhin nur ein Zeigegerät (chessground hat keine Tastatur-Eingabe). Gerendert, mit echtem Fokus.
describe('PromotionPickerComponent Tastatur (F8-018)', () => {
  let opener: HTMLButtonElement;

  function render() {
    TestBed.configureTestingModule({
      imports: [PromotionPickerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(PromotionPickerComponent);
    fixture.componentRef.setInput('color', 'w');
    fixture.componentRef.setInput('dest', 'a8');
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    opener = document.createElement('button');   // z. B. das Zug-Eingabefeld, aus dem die Umwandlung kam
    document.body.appendChild(opener);
    opener.focus();
  });
  afterEach(() => opener.remove());

  it('die Figuren sind benannte Knöpfe, beim Öffnen hat die Dame den Fokus', () => {
    const el = render().nativeElement as HTMLElement;
    const buttons = Array.from(el.querySelectorAll('button.promotion-piece'));
    expect(buttons.map(b => b.getAttribute('aria-label')))
      .toEqual(['promotion.q', 'promotion.r', 'promotion.b', 'promotion.n']);
    expect(buttons.every(b => b.getAttribute('type') === 'button')).toBeTrue();
    expect(el.querySelector('[role="group"]')!.getAttribute('aria-label')).toBe('promotion.group');
    expect(document.activeElement).toBe(buttons[0]);
  });

  it('Esc bricht ab — auch im Guard-Fenster — und erreicht einen umgebenden Dialog nicht', () => {
    const fixture = render();
    const dismiss = spyOn(fixture.componentInstance.dismiss, 'emit');
    const outer = jasmine.createSpy('keydown am body');
    document.body.addEventListener('keydown', outer);
    try {
      document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    } finally {
      document.body.removeEventListener('keydown', outer);
    }
    expect(dismiss).toHaveBeenCalledTimes(1);
    expect(outer).not.toHaveBeenCalled();
  });

  it('gibt den Fokus beim Schließen dorthin zurück, wo er vorher war', () => {
    const fixture = render();
    expect(document.activeElement).not.toBe(opener);
    fixture.destroy();
    expect(document.activeElement).toBe(opener);
  });
});
