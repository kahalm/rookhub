# Assets von KidHub (Kinderseite)

Alles unter `public-kidhub/` ist ABGELEITET und laesst sich jederzeit neu erzeugen:

```bash
python3 design/kidhub/derive.py      # aus dem Repo-Wurzelverzeichnis; braucht numpy + Pillow
```

## Vorlagen (`design/kidhub/`)

| Vorlage | Rolle | Stand |
|---|---|---|
| `KidHub.png` (1254×1254) | normales Symbol, Apple-Touch, favicon — Motiv fuellt die Flaeche, eigener runder Rahmen | gezeichnet (Bild-KI, 2026-09-27) |
| `KidHub2.png` (1254×1254) | maskable — randloser Grund, Motiv mittig | gezeichnet (2026-09-27). Das Motiv reicht dort nur bis ~23 % der Breite (vom Mittelpunkt) — das Skript schneidet mittig um das Motiv so zu, dass es bis 32 % reicht (Android garantiert den Kreis bis 40 %, also 80 % der sicheren Zone). Fehlt die Datei, entsteht die Fassung aus dem Motiv von `KidHub.png` |
| `KidHub3.png` (1536×1024) | Vorschaubild — Motiv links, rechte Hälfte leer | gezeichnet (2026-09-27). Das Skript verkleinert auf 1200 breit, schneidet senkrecht UM DAS MOTIV auf 630 zu (mittig geschnitten bliebe unten nur ein Streifen von 23 px) und schreibt „KidHub" + „Schach-Puzzles für Kinder – ganz einfach!" in die rechte Hälfte: weiß mit dunkelblauem Umriss wie die Zeichnung, URW Gothic Demi (sonst DejaVu Sans Bold), Schriftgröße passt sich der Breite an. Fehlt die Datei, setzt das Skript `KidHub.png` links auf den Verlauf |

## Was das Skript an `KidHub.png` korrigiert

Die Bild-KI lieferte zwei Maengel, die `derive.py` bei JEDEM Lauf behebt (`KidHub2.png`/`KidHub3.png` haben sie nicht — randloser Grund, kein Fremdton):

1. **Kein Alphakanal** — die Ecken ausserhalb des abgerundeten Quadrats sind WEISS. Freigestellt wird
   nur, was mit einer Bildecke verbunden ist (ein weisses Funkeln im Blau bleibt stehen), die Randpixel
   weich nach ihrem Weissanteil und in der Farbe des Grunds — sonst saeumt ein weisser Rand das Symbol
   auf dunklem Grund.
2. **Ein Stueck Umriss an der Maehne ist rotlila** statt dunkelblau (~4000 Pixel um x 850 / y 780) —
   umgefaerbt auf die Linie Dunkelblau↔Weiss nach Helligkeit, damit die Kantenglaettung erhalten bleibt.

Der blaue Grund wird je ZEILE gemessen (senkrechter Verlauf von (57,171,248) nach (28,130,224)) — daraus
entstehen die randlosen Flaechen fuer Apple-Symbol, maskable und Vorschaubild, ohne sichtbare Naht.

## Erzeugte Dateien

| Datei | Groesse | Quelle |
|---|---|---|
| `favicon.ico` | 16 + 32 + 48 | `KidHub.png`, freigestellt |
| `icons/icon-192.png`, `icons/icon-512.png` | quadratisch, transparente Ecken | `KidHub.png` |
| `icons/apple-touch-icon.png` | 180×180, randlos (iOS rundet selbst) | `KidHub.png` auf dem Verlauf |
| `icons/icon-192-maskable.png`, `icons/icon-512-maskable.png` | quadratisch, randlos | `KidHub2.png` zugeschnitten, sonst abgeleitet |
| `og-image.png` | 1200×630 | `KidHub3.png` + Schrift, sonst zusammengesetzt |

**Bewusst KEIN `icons/icon.svg`**: `public-kidhub/` wird UEBER `public/` gelegt, und was hier fehlt,
faellt still auf RookHubs Datei zurueck (so trug die Turnierseite monatelang das falsche Logo).
`KidHubAssetTests` haelt Manifest, `index.html` und Dateien gegeneinander.
