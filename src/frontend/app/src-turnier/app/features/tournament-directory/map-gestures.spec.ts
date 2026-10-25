import * as L from 'leaflet';
import { MapGestureHint, wireMapGestures } from './map-gestures';

describe('wireMapGestures', () => {
  let el: HTMLDivElement;
  let map: L.Map;
  let hints: MapGestureHint[];

  beforeEach(() => {
    el = document.createElement('div');
    el.style.width = '300px';
    el.style.height = '200px';
    document.body.appendChild(el);
    map = L.map(el, { center: [47.7, 13.4], zoom: 6, scrollWheelZoom: false });
    hints = [];
  });

  afterEach(() => {
    map.remove();
    el.remove();
  });

  it('zoomt mit dem Mausrad erst nach einem Klick und nur, solange der Zeiger darauf bleibt', () => {
    wireMapGestures(map, el, false, k => hints.push(k));

    el.dispatchEvent(new WheelEvent('wheel', { bubbles: true }));
    expect(hints).toEqual(['wheel']);

    el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect(map.scrollWheelZoom.enabled()).toBeTrue();
    el.dispatchEvent(new WheelEvent('wheel', { bubbles: true }));
    expect(hints).withContext('Hinweis trotz freigegebenem Rad').toEqual(['wheel']);

    el.dispatchEvent(new MouseEvent('mouseleave'));
    expect(map.scrollWheelZoom.enabled()).toBeFalse();
  });

  it('gibt das Rad auch frei, wenn die Karte per Tastatur den Fokus bekommt', () => {
    wireMapGestures(map, el, false, k => hints.push(k));

    map.fire('focus');

    expect(map.scrollWheelZoom.enabled()).toBeTrue();
  });

  it('meldet über Leaflets Knöpfen keinen Hinweis', () => {
    wireMapGestures(map, el, false, k => hints.push(k));
    const control = el.querySelector('.leaflet-control') as HTMLElement;

    control.dispatchEvent(new WheelEvent('wheel', { bubbles: true }));

    expect(hints).toEqual([]);
  });

  it('meldet auf Touch-Geräten nur EINEN Finger, nicht zwei', () => {
    wireMapGestures(map, el, true, k => hints.push(k));
    const touch = (n: number) => {
      const e = new Event('touchmove', { bubbles: true });
      Object.defineProperty(e, 'touches', { value: { length: n } });
      el.dispatchEvent(e);
    };

    touch(2);
    expect(hints).toEqual([]);
    touch(1);
    expect(hints).toEqual(['touch']);
  });
});
