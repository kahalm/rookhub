# Prompt: RookHub-Wortmarke im Kasten-Stil

Vorlage: `design/vorlage.svg` — zweifarbige Wortmarke, zweite Worthaelfte in einem farbigen
Kasten. Die Zahlen unten sind aus dieser Datei GEMESSEN, nicht geschaetzt.

## Zwei Dinge vorweg

1. **Die Buchstabenformen muessen eigene sein.** Die Vorlage enthaelt die nachgezeichneten
   Original-Glyphen der bekannten Marke. Uebernimmt man diese Pfade, uebernimmt man deren
   Zeichnung — und die Marke, auf die alle sofort tippen. Uebernommen wird der AUFBAU
   (dunkle Flaeche, helles Wort, Farbkasten mit dunklem Wort), nicht die Zeichnung.
2. **Ausgabe als Vektor mit ausgeschriebenen Pfaden.** Ein `<text>`-Element haengt an einer
   Schrift, die auf dem Zielsystem fehlen kann; ein Logo muss ueberall gleich aussehen.

## Der Prompt

> Entwirf eine Wortmarke „RookHub" als SVG, aufgebaut als zweifarbige Sperrschrift mit Farbkasten.
>
> **Aufbau** (Seitenverhaeltnis 2,83 : 1, z. B. 1980 × 700):
> - Vollflaechiger dunkler Grund ueber die gesamte Zeichenflaeche.
> - „Rook" linksbuendig, in Weiss, direkt auf dem Grund.
> - „Hub" in einem abgerundeten Rechteck rechts: Kasten beginnt bei 54,5 % der Breite, ist 40,4 %
>   breit und 71,4 % hoch, senkrecht zentriert (14,3 % Luft oben wie unten), Eckenradius 8 % der
>   Kastenhoehe. Die Schrift IM Kasten ist dunkel (die Grundfarbe), nicht weiss.
> - Beide Worthaelften sitzen auf einer gemeinsamen Grundlinie und haben dieselbe Versalhoehe; der
>   Kasten umschliesst „Hub" mit gleichmaessiger Luft (Ober- und Unterlaenge beruecksichtigen).
>
> **Schrift**: sehr fette, geschlossene Grotesk mit runden Punzen und kurzen Ueberhaengen —
> die Anmutung von Helvetica Black oder Inter ExtraBold. Enge, gleichmaessige Laufweite; das
> Wort soll als kompakter Block wirken, nicht als Zeile. **Keine** Serifen, keine Schattierung,
> kein Verlauf, keine Kontur.
>
> **Farben** (genau diese Werte, nichts dazwischen mischen):
> - Grund: `#fafafa`
> - „Rook": `#0f1116`
> - Kasten: `#3f51b5`
> - Schrift IM Kasten: `#ffffff`
>
> **Nicht** enthalten: Figuren, Bretter, Zinnen, Icons, Slogans, Rahmen, Rand um die Zeichenflaeche.
> Die Marke besteht aus genau drei Elementen: Grundflaeche, Wort, Kasten mit Wort.
>
> Liefere sauberes SVG: alle Buchstaben als `<path>` (keine `<text>`-Elemente, keine eingebettete
> Schrift), `viewBox` gesetzt, keine `width`/`height` in Pixeln, keine `<style>`-Bloecke,
> Farben direkt als `fill`-Attribut.

## Farbvarianten — und warum die naheliegende NICHT geht

Alles unten sind RookHubs eigene Werte: `#3f51b5` ist die Themenfarbe aus
`public/manifest.webmanifest` und die Grundflaeche von `public/icons/icon.svg`, `#fafafa` die
Hintergrundfarbe desselben Manifests, `#172345` die Themenfarbe der Turnierseite.

| Variante | Grund | Wort 1 | Kasten | Wort 2 |
|---|---|---|---|---|
| **Hell** (Vorgabe) | `#fafafa` | `#0f1116` | `#3f51b5` | `#ffffff` |
| **Dunkel** | `#0f1116` | `#ffffff` | `#7986cb` | `#0f1116` |
| **Turnierseite** | `#172345` | `#ffffff` | `#f9ab00` | `#172345` |

**Die Falle: `#3f51b5` mit DUNKLER Schrift im Kasten ergibt 2,75 : 1.** Das ist unter der Grenze
selbst fuer Grossschrift (3 : 1) — der Kasten wird zu einem Fleck, in dem man das Wort erahnt. Die
Vorbild-Marke lebt davon, dass der Kasten HELL und die Schrift darin dunkel ist; ein
Indigo-500-Kasten ist dafuer schlicht zu dunkel. Zwei Auswege, beide oben in der Tabelle:

- **Hell**: die exakte Markenfarbe bleibt, aber sie steht auf HELLEM Grund und traegt weisse
  Schrift. Kasten gegen Grund 6,58 : 1, Schrift im Kasten 6,87 : 1, „Rook" gegen Grund 18,1 : 1.
  Das ist die einzige Variante, die `#3f51b5` unveraendert benutzt.
- **Dunkel**: der Zweiklang aus hellem und dunklem Wort bleibt, dafuer wird der Kasten auf
  Indigo 300 (`#7986cb`) aufgehellt — 5,47 : 1 sowohl fuer die Schrift darin als auch gegen den
  Grund. Auf Indigo 500 waeren es 2,75 : 1.

Gemessene Kontraste zum Vergleich: Amber `#f9ab00` mit dunkler Schrift kommt auf 9,76 : 1, Indigo
200 auf 8,17 : 1, Indigo 300 auf 5,47 : 1, Indigo 400 auf 3,88 : 1, Indigo 500 auf 2,75 : 1.

## Danach pruefen

- **Auf 32 px verkleinern.** Bleibt „Hub" im Kasten lesbar? Wenn nicht, ist die Laufweite zu eng
  oder der Kasten zu knapp — das Symbol der Anwendung ist ohnehin das Turm-Icon, die Wortmarke
  muss nur bis Favicon-Groesse nicht zu Matsch werden.
- **In Graustufen ansehen.** Der Kasten muss sich auch ohne Farbe vom Grund abheben; sonst traegt
  die Marke nur ueber den Farbkanal, und das ist fuer Rot-Gruen-Sehschwaeche und fuer jeden
  Schwarzweissdruck eine leere Flaeche.
- **Nebeneinander mit `public/icons/icon.svg`.** Wortmarke und Symbol sollen dieselbe Strichstaerke
  wirken lassen; das Icon ist sehr fett gezeichnet, eine duenne Wortmarke passt nicht dazu.
