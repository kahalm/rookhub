/**
 * Der Kreis ums GESICHT im Bild eines Karteiblatts (Wunsch 2026-10-03: „mit einem Kreis sein Gesicht auswählen — das soll
 * beim Abhaken vorn beim Namen dabei sein"). Unabhängig von der Pixelgröße: `x`/`y` = Mittelpunkt als Anteil von Breite
 * und Höhe (0..1), `r` = Radius als Anteil der KÜRZEREN Seite (0,5 = der größte Kreis, der ins Bild passt).
 *
 * `clampFace` ist der Spiegel von `ClubFace.Normalize` am Server (`Services/Club/ClubFace.cs`) — beide Seiten haben einen
 * Test mit denselben LITERALEN Werten (`face.spec.ts` ↔ `ClubFaceTests`). Wer hier etwas ändert, ändert es dort.
 */
export interface Face {
  x: number;
  y: number;
  r: number;
}

export const FACE_MIN_R = 0.05;
export const FACE_MAX_R = 0.5;

/** Womit ein neues Bild beginnt: mittig, etwas über der Mitte — dort ist bei einem Porträt das Gesicht. */
export const DEFAULT_FACE: Face = { x: 0.5, y: 0.4, r: 0.28 };

const round4 = (v: number) => Math.round(v * 1e4) / 1e4;
const clamp = (v: number, min: number, max: number) => Math.min(Math.max(v, min), max);

/**
 * Den Kreis ins Bild holen: Radius in die Grenzen, Mittelpunkt so weit vom Rand, dass der ganze Kreis im Bild liegt.
 * `null`, wenn die Angaben keine Zahlen sind oder das Bild keine Fläche hat.
 */
export function clampFace(face: Face, width: number, height: number): Face | null {
  if (![face.x, face.y, face.r].every(Number.isFinite) || !(width > 0) || !(height > 0)) return null;
  const shorter = Math.min(width, height);
  const r = clamp(face.r, FACE_MIN_R, FACE_MAX_R);
  const radius = r * shorter;
  const cx = clamp(face.x * width, radius, width - radius);
  const cy = clamp(face.y * height, radius, height - radius);
  return { x: round4(cx / width), y: round4(cy / height), r: round4(r) };
}

export interface FaceBox {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** Wo der Kreis über dem Bild liegt — in PROZENT der Bildfläche (so bleibt er beim Verkleinern des Bilds an seinem Ort). */
export function faceBox(face: Face, width: number, height: number): FaceBox {
  const radius = face.r * Math.min(width, height);
  return {
    left: (face.x - radius / width) * 100,
    top: (face.y - radius / height) * 100,
    width: (2 * radius / width) * 100,
    height: (2 * radius / height) * 100,
  };
}
