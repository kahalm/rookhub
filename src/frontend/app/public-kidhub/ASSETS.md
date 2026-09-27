# Assets von KidHub (Kinderseite)

Alles unter `public-kidhub/` ist ABGELEITET und laesst sich jederzeit neu erzeugen:

```bash
python3 design/kidhub/derive.py      # aus dem Repo-Wurzelverzeichnis; braucht numpy + Pillow
```

## Vorlagen (`design/kidhub/`)

| Vorlage | Rolle | Stand |
|---|---|---|
| `KidHub.png` (1254×1254) | normales Symbol, Apple-Touch, favicon — Motiv fuellt die Flaeche, eigener runder Rahmen | gezeichnet (Bild-KI, 2026-09-27) |
| `KidHub2.png` (optional) | maskable — randloser Grund, Motiv innerhalb von 60–70 % des Radius | FEHLT — bis dahin baut das Skript die Fassung aus dem Motiv von `KidHub.png` (blauer Grund herausgerechnet, auf 72 % verkleinert, randloser Verlauf) |
| `KidHub3.png` (optional, 1536×1024) | Vorschaubild — Motiv links, rechts Platz | FEHLT — bis dahin setzt das Skript `KidHub.png` links auf den Verlauf und schreibt „KidHub" daneben (DejaVu Sans Bold) |

## Was das Skript an `KidHub.png` korrigiert

Die Bild-KI lieferte zwei Maengel, die `derive.py` bei JEDEM Lauf behebt:

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
| `icons/icon-192-maskable.png`, `icons/icon-512-maskable.png` | quadratisch, randlos | `KidHub2.png`, sonst abgeleitet |
| `og-image.png` | 1200×630 | `KidHub3.png`, sonst zusammengesetzt |

**Bewusst KEIN `icons/icon.svg`**: `public-kidhub/` wird UEBER `public/` gelegt, und was hier fehlt,
faellt still auf RookHubs Datei zurueck (so trug die Turnierseite monatelang das falsche Logo).
`KidHubAssetTests` haelt Manifest, `index.html` und Dateien gegeneinander.
