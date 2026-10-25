import type * as L from 'leaflet';

/** Welcher Gesten-Hinweis ueber der Karte steht (siehe `TournamentMapComponent.gestureHint`). */
export type MapGestureHint = 'wheel' | 'touch';

/**
 * Schaltet das Mausrad-Zoomen mit dem Klick in die Karte ein und beim Verlassen wieder aus;
 * meldet ueber `showHint`, wenn eine Geste die Karte nicht bewegt. `mouseleave` und nicht Leaflets
 * `mouseout`: Letzteres feuert auch beim Verlassen eines Punktes und schaltete das Rad mitten
 * auf der Karte wieder ab. Die beiden Hinweis-Lauscher sind `passive` — sie duerfen das
 * Scrollen der Seite, das sie begleiten, nicht aufhalten.
 *
 * <p>Auf Touch-Geraeten (`coarse`) scrollt EIN Finger die Seite — der Hinweis sagt dann, dass
 * zwei die Karte verschieben. Herausgezogen aus `TournamentMapComponent` (Zerlegung Z27).</p>
 */
export function wireMapGestures(
  map: L.Map, host: HTMLElement, coarse: boolean, showHint: (kind: MapGestureHint) => void): void {
  // In einem Popup (Turnierliste eines Punktes) oder auf Leaflets Knoepfen ist ein Finger
  // richtig — dort scrollt bzw. tippt man, die Karte soll sich gar nicht bewegen.
  const onMapItself = (e: Event) =>
    !(e.target as Element | null)?.closest?.('.leaflet-popup, .leaflet-control');
  if (coarse) {
    host.addEventListener('touchmove', e => {
      if (e.touches.length === 1 && onMapItself(e)) showHint('touch');
    }, { passive: true });
    return;
  }
  const enableWheel = () => map.scrollWheelZoom.enable();
  host.addEventListener('mousedown', enableWheel);
  map.on('focus', enableWheel);   // auch per Tab in die Karte
  host.addEventListener('mouseleave', () => map.scrollWheelZoom.disable());
  host.addEventListener('wheel', e => {
    if (!map.scrollWheelZoom.enabled() && onMapItself(e)) showHint('wheel');
  }, { passive: true });
}
