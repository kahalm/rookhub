import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Face } from '../core/face';
import { FacePickerComponent } from './face-picker.component';

/** Ein echtes Bild in Wunschgröße (als Daten-Adresse) — der Kreis braucht die Maße des geladenen Bilds. */
function picture(width: number, height: number): string {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#7a9';
  ctx.fillRect(0, 0, width, height);
  return canvas.toDataURL('image/png');
}

describe('FacePickerComponent (das Gesicht mit einem Kreis wählen)', () => {
  let fixture: ComponentFixture<FacePickerComponent>;
  let emitted: Face[];
  const el = () => fixture.nativeElement as HTMLElement;
  const ring = () => el().querySelector<HTMLElement>('.face-ring');
  const stage = () => el().querySelector<HTMLElement>('.face-stage')!;

  /** Aufbauen und warten, bis das Bild geladen ist (oder sicher nicht lädt). */
  async function create(src: string, face: Face | null = null, disabled = false): Promise<void> {
    TestBed.configureTestingModule({ imports: [FacePickerComponent] });
    fixture = TestBed.createComponent(FacePickerComponent);
    fixture.componentRef.setInput('src', src);
    fixture.componentRef.setInput('face', face);
    fixture.componentRef.setInput('disabled', disabled);
    emitted = [];
    fixture.componentInstance.faceChange.subscribe(f => emitted.push(f));
    fixture.detectChanges();
    // Immer auf das Ereignis warten: `img.complete` ist bei einer Daten-Adresse schon wahr, BEVOR „load" gemeldet wurde.
    const img = el().querySelector('img')!;
    await new Promise<void>(done => { img.addEventListener('load', () => done()); img.addEventListener('error', () => done()); });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const pointer = (type: string, target: Element, x: number, y: number) =>
    target.dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, pointerId: 7, bubbles: true, cancelable: true }));

  it('zeigt über dem geladenen Bild den Anfangskreis — gemeldet wird erst, wenn man ihn anfasst', async () => {
    await create(picture(300, 400));
    const s = ring()!.style;
    expect([s.left, s.top, s.width, s.height].map(v => Math.round(parseFloat(v) * 100) / 100)).toEqual([22, 19, 56, 42]);
    const box = ring()!.getBoundingClientRect();
    expect(Math.round(box.width)).toBe(Math.round(box.height));                           // auf dem Schirm wirklich ein Kreis
    expect(el().querySelector<HTMLInputElement>('input[name=faceSize]')!.value).toBe('28');
    expect(emitted).toEqual([]);
  });

  it('ein gespeicherter Kreis steht an seinem Ort', async () => {
    await create(picture(400, 300), { x: 0.25, y: 0.5, r: 0.2 });                         // Radius 60 px
    const s = ring()!.style;
    expect([s.left, s.top, s.width, s.height].map(v => Math.round(parseFloat(v) * 100) / 100)).toEqual([10, 30, 30, 40]);
  });

  it('ein Tipp ins Bild setzt den Kreis dorthin — am Rand so weit hinein, dass er ganz im Bild liegt', async () => {
    await create(picture(300, 400));
    const r = stage().getBoundingClientRect();
    stage().querySelector('img')!.dispatchEvent(new MouseEvent('click', { clientX: r.left + r.width * 0.2, clientY: r.top + r.height * 0.15, bubbles: true }));
    expect(emitted).toEqual([{ x: 0.28, y: 0.21, r: 0.28 }]);
  });

  it('den Kreis ziehen verschiebt ihn; nach dem Loslassen folgt er dem Zeiger nicht mehr', async () => {
    await create(picture(300, 400));
    const r = stage().getBoundingClientRect();
    const [x0, y0] = [r.left + r.width / 2, r.top + r.height * 0.4];
    pointer('pointerdown', ring()!, x0, y0);
    pointer('pointermove', ring()!, x0 + r.width * 0.1, y0 + r.height * 0.1);
    expect(emitted).toEqual([{ x: 0.6, y: 0.5, r: 0.28 }]);
    pointer('pointerup', ring()!, x0, y0);
    pointer('pointermove', ring()!, x0 + 50, y0 + 50);
    expect(emitted.length).toBe(1);
  });

  it('der Griff am Rand bestimmt die Größe: Abstand vom Mittelpunkt', async () => {
    await create(picture(300, 400));
    const r = stage().getBoundingClientRect();
    const [cx, cy] = [r.left + r.width / 2, r.top + r.height * 0.4];
    const grip = el().querySelector('.face-grip')!;
    pointer('pointerdown', grip, cx + 60, cy + 60);
    pointer('pointermove', grip, cx + r.width * 0.3, cy);                                 // 0,3 der kürzeren Seite (Breite)
    expect(emitted).toEqual([{ x: 0.5, y: 0.4, r: 0.3 }]);
  });

  it('der Regler ändert die Größe; Pfeiltasten verschieben, Plus/Minus ändern die Größe', async () => {
    await create(picture(300, 400));
    const range = el().querySelector<HTMLInputElement>('input[name=faceSize]')!;
    expect([range.min, range.max]).toEqual(['5', '50']);
    range.value = '40';
    range.dispatchEvent(new Event('input'));
    ring()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    ring()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    ring()!.dispatchEvent(new KeyboardEvent('keydown', { key: '-', bubbles: true }));
    ring()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true }));     // andere Tasten: nichts
    expect(emitted).toEqual([{ x: 0.5, y: 0.4, r: 0.4 }, { x: 0.51, y: 0.4, r: 0.28 }, { x: 0.5, y: 0.39, r: 0.28 }, { x: 0.5, y: 0.4, r: 0.27 }]);
  });

  it('gesperrt (während gespeichert wird) meldet er nichts', async () => {
    await create(picture(300, 400), null, true);
    const r = stage().getBoundingClientRect();
    stage().querySelector('img')!.dispatchEvent(new MouseEvent('click', { clientX: r.left + 10, clientY: r.top + 10, bubbles: true }));
    pointer('pointerdown', ring()!, r.left + 150, r.top + 160);
    pointer('pointermove', ring()!, r.left + 180, r.top + 200);
    ring()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(emitted).toEqual([]);
    expect(el().querySelector<HTMLInputElement>('input[name=faceSize]')!.disabled).toBeTrue();
  });

  it('lädt das Bild nicht, gibt es weder Kreis noch Regler', async () => {
    await create('data:image/png;base64,kaputt');
    expect(ring()).toBeNull();
    expect(el().querySelector('input[name=faceSize]')).toBeNull();
  });
});
