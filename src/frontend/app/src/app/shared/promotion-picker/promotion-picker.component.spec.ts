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
