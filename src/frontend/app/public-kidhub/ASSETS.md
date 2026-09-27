# Assets von KidHub (Kinderseite)

Alles unter `public-kidhub/` ist ABGELEITET und laesst sich jederzeit neu erzeugen. Die Vorlagen
liegen in `design/kidhub/` im Repo-Wurzelverzeichnis.

## Stand: Platzhalter aus SVG

Bis es gezeichnete Vorlagen gibt (wie `design/Designer*.png` der Turnierseite), entstehen die
Symbole aus drei SVG-Dateien — Springer aus dem cburnett-Figurensatz (derselbe wie auf dem Brett)
vor einem gelben Stern:

| Vorlage | Rolle |
|---|---|
| `design/kidhub/icon.svg` | normales Symbol, Apple-Touch, favicon — Motiv fuellt die Flaeche, eigener runder Rahmen |
| `design/kidhub/icon-maskable.svg` | maskable — Hintergrund randlos, Motiv innerhalb von 70 % des Radius (Android beschneidet ab 80 %) |
| `design/kidhub/og.svg` | Vorschaubild 1200×630 fuer geteilte Links |

Neu erzeugen (aus `src/frontend/app`):

```bash
node ../../../design/kidhub/render.mjs          # PNGs via playwright-core (Chromium)
python3 -c "from PIL import Image; Image.open('public-kidhub/icons/icon-512.png').convert('RGBA').save('public-kidhub/favicon.ico', sizes=[(16,16),(32,32),(48,48)])"
rm public-kidhub/favicon-48.png
```

## Palette

| Rolle | Wert | Verwendet in |
|---|---|---|
| Grund (Himmelblau) | `#2f8fdc` → `#4db5ff` | Symbol, `theme_color`, `<meta name="theme-color">` |
| Seitenhintergrund | `#eaf6ff` | `background_color`, `--kid-bg` in `src-kidhub/app/app.component.ts` |
| Akzent (Sonnengelb) | `#ffcc33` | Stern, Tipp-Knopf (`--kid-yellow`) |

## Erzeugte Dateien

| Datei | Groesse | Quelle |
|---|---|---|
| `favicon.ico` | 16 + 32 + 48 | `icon.svg` |
| `icons/icon-192.png`, `icons/icon-512.png` | quadratisch | `icon.svg` |
| `icons/icon-192-maskable.png`, `icons/icon-512-maskable.png` | quadratisch | `icon-maskable.svg` |
| `icons/apple-touch-icon.png` | 180×180 | `icon.svg` |
| `og-image.png` | 1200×630 | `og.svg` |

**Bewusst KEIN `icons/icon.svg`**: `public-kidhub/` wird UEBER `public/` gelegt, und was hier fehlt,
faellt still auf RookHubs Datei zurueck (so trug die Turnierseite monatelang das falsche Logo).
`KidHubAssetTests` haelt Manifest und Dateien gegeneinander.
