# Eigene Symbole für die Turnierseite

Die Turnierseite (`turnier.oberschmid.homes`) benutzt heute die Symbole von RookHub — den
weißen Turm auf Indigo aus `public/icons/`. Auf dem Startbildschirm eines Handys stehen damit
zwei Apps mit demselben Bild, und im Reiter-Wechsler ist nicht zu erkennen, welche welche ist.
Die Dateien unten gehören deshalb in **dieses** Verzeichnis (`public-turnier/icons/`): der
Build kopiert `public-turnier/` ÜBER `public/`, ein Symbol hier ersetzt also das von RookHub,
ohne dass irgendwo ein Pfad geändert werden muss.

## Was gebraucht wird

| Datei | Größe | Zweck |
|---|---|---|
| `icons/icon.svg` | 512×512 Viewbox | Browser-Reiter (bevorzugt), skaliert verlustfrei |
| `icons/icon-192.png` | 192×192 | Startbildschirm, Reiter-Fallback |
| `icons/icon-512.png` | 512×512 | Splash, Store-Eintrag |
| `icons/icon-192-maskable.png` | 192×192 | Android, **beschnitten** |
| `icons/icon-512-maskable.png` | 512×512 | Android, **beschnitten** |
| `icons/apple-touch-icon.png` | 180×180 | iOS-Startbildschirm (kein Alphakanal, iOS rundet selbst) |
| `favicon.ico` | 16+32+48 | alte Browser, Lesezeichenleisten |
| `og-turnier.png` | 1200×630 | Vorschaubild geteilter Turnierlinks (`og:image`) |

**Die maskable-Fassungen sind keine Kopien.** Android schneidet sie auf einen Kreis oder ein
abgerundetes Quadrat zu und darf dabei bis zu 20 % vom Rand wegnehmen. Alles Erkennbare muss
deshalb innerhalb des mittleren Kreises mit **40 % Radius** liegen (bei 512 px: ein Kreis mit
Radius 205 px um die Mitte), und die Fläche außerhalb ist durchgehend gefüllter Hintergrund —
keine Rundung, kein Rand, keine Transparenz. Die normalen Fassungen tragen dagegen ihre eigene
abgerundete Ecke (heute `rx=96` bei 512).

---

## Der Prompt

> Entwirf ein App-Symbol für eine Website, die **Schachturniere findet**: einen Turnierkalender
> mit Karte, Umkreissuche und Turnierverzeichnis. Sie ist die Schwesterseite eines
> Schach-Trainingsportals namens RookHub, dessen Symbol ein weißer Schachturm auf indigoblauem,
> abgerundetem Quadrat ist. Das neue Symbol soll **sichtbar zur selben Familie gehören und
> trotzdem auf einen Blick unterscheidbar sein** — beide liegen nebeneinander auf einem
> Startbildschirm.
>
> **Motiv:** nicht noch ein Turm. Gesucht ist das *Finden* eines Turniers, nicht das Spielen —
> also ein Kartenzeichen (Ortsmarke, unten spitz und oben rund), ein Kalenderblatt oder die
> Verbindung aus beidem. Genau eine dieser Ideen, klar und groß. Eine Schachfigur darf als
> Detail darin vorkommen (etwa als Silhouette im Kopf der Ortsmarke), aber nicht das Bild
> beherrschen.
>
> **Stil:** flach, geometrisch, zweifarbig. Weiße Form auf gesättigtem Indigo `#3f51b5`.
> Keine Farbverläufe, keine Schlagschatten, keine Perspektive, keine Umrisslinien dünner als
> 1/24 der Bildbreite. Kräftige, geschlossene Flächen statt feiner Striche.
>
> **Kein Text, keine Buchstaben, keine Zahlen** — auch nicht im Kalenderblatt. Bei 16 Pixeln
> ist jede Schrift ein grauer Fleck.
>
> **Der Test, den es bestehen muss:** auf 16×16 Pixel verkleinert muss die Form noch als genau
> diese Form erkennbar sein. Das heißt: eine Silhouette, die man mit zwei Wörtern beschreiben
> kann, viel Abstand zwischen den Teilen, und keine Fläche schmaler als etwa 6 % der Bildbreite.
>
> **Liefere zwei Fassungen desselben Motivs:**
> 1. **Randfassung** — 512×512, Motiv auf einem abgerundeten Quadrat (Eckradius 96), das Motiv
>    füllt etwa 60–70 % der Breite und sitzt optisch zentriert.
> 2. **Beschnittfassung** — 512×512, derselbe Hintergrund **randlos und ohne Rundung** bis in
>    alle vier Ecken, das Motiv vollständig innerhalb eines mittigen Kreises mit Radius 205 px.
>    Diese wird zugeschnitten; was außerhalb liegt, verschwindet.
>
> **Format:** SVG, ganzzahlige Koordinaten auf einer 512er-Viewbox, keine eingebetteten
> Rasterbilder, keine Schriftarten, keine Filter. Flächen als `<path>`/`<rect>`/`<polygon>`
> mit `fill`, nichts als `stroke`.

### Zusatz-Prompt für das Vorschaubild geteilter Links (1200×630)

> Entwirf ein Vorschaubild im Format 1200×630 für geteilte Links auf einzelne Schachturniere.
> Links ein großzügiger freier Bereich, in den später Turniername und Datum als Text gelegt
> werden — dort darf **kein** Motiv liegen. Rechts das App-Symbol groß, dazu eine dezente,
> stark abstrahierte Landkarten-Andeutung (Umrisslinien, Ortsmarken) in einem nur wenig vom
> Hintergrund abweichenden Ton. Hintergrund indigoblau `#3f51b5`, Motive weiß, flach und
> zweifarbig wie das Symbol. Kein Text im Bild. Am Rand mindestens 60 px Abstand halten — die
> Vorschauen mancher Dienste beschneiden auf 1200×600.

---

## Danach

1. Die Dateien nach `public-turnier/icons/` legen (und `favicon.ico` samt `og-turnier.png`
   direkt nach `public-turnier/`).
2. `manifest.webmanifest` in diesem Verzeichnis prüfen: die Pfade stimmen schon, nur
   `theme_color`/`background_color` anpassen, falls sich der Farbton ändert.
3. `src-turnier/index.html` braucht keine Änderung — es verweist auf dieselben Namen.
4. `twa/twa-manifest.json` und `twa-manifest.dev.json` zeigen für die Android-App auf die
   Symbol-URLs; nach einem Deploy dort neu bauen, sonst behält die installierte App das alte
   Bild.
5. Prüfen: Reiter bei 100 % und 400 % Zoom, Startbildschirm auf Android (maskable!) und iOS,
   und ein geteilter Turnierlink in einem Messenger.
