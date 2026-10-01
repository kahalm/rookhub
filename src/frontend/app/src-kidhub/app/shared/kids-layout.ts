/**
 * Aufbau der Aufgabenseite (Stufe, Endlos, Kurs) je Fenster — EINE Stelle fuer alle Komponenten, die daran
 * haengen: die App-Huelle setzt dazu die Groessen (`--kid-board`, `--kid-row`), `KidsPuzzleComponent` das Raster,
 * die drei Seiten ihre Kopfzeile. Wandert eine Grenze nur in einer Datei, sitzen Kopfzeile und Brett schief.
 *
 * <p>Vorher hing alles an einer festen Breite (bis 760 px untereinander): am iPad hochkant (768–834 px) stand das
 * Brett mit 300 px neben einer halbleeren Seite, und am Handy quer lief seine unterste Reihe aus dem Bild
 * (Codereview 2026-09-29, UX-028).</p>
 */

/**
 * Untereinander (Aufgabe, Brett, Eule): schmal, hochkant — daneben bliebe fuers Brett nur ein Streifen — oder bis
 * 760 px Breite mit genug Hoehe. Schliesst {@link KID_SHORT} aus; bis 760 px Breite ergaenzen sich die beiden genau.
 * Bereichs-Schreibweise (`<`, `>`) statt `max-width: 599px`/`min-height: 501px`: dazwischen lagen sonst 1-px-Luecken,
 * in denen bei krummen Fenstermassen (Zoom, Pixeldichte 2,625 …) die PC-Formel mit 300 px Untergrenze griff.
 */
export const KID_STACKED = '(width < 600px), (max-aspect-ratio: 1/1), (width <= 760px) and (height > 500px)';

/**
 * Handy quer: wenig Hoehe — das Brett nimmt die Hoehe ganz, die Aufgabe steht daneben. Die App-Huelle rechnet dafuer
 * mit genau EINER Titelzeile ueber dem Brett; die drei Seiten (Stufe, Endlos, Kurs) halten sie deshalb einzeilig
 * und kuerzen einen langen Titel, statt ihn umzubrechen.
 */
export const KID_SHORT = '(width >= 600px) and (height <= 500px)';
