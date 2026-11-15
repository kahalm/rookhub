import type { InlinePart, SummaryBlock } from './summary-markdown.util';
import type { GameTextPart } from './summary-game-text.util';

/**
 * Realistischer Text für summary-linking.spec.ts (0.978.1): eine echte Zusammenfassung (36 Züge, verschmolzener Abschnitt
 * „Die Partie“ mit Varianten, Schlüsselstellungen, Literatur) — Namen ersetzt. `SUMMARY_FIXTURE_EXPECTED` ist die
 * Verlinkung, wie sie die Fassung VOR dem Zwischenspeicher der legalen Züge (0.978.0) geliefert hat, als Signatur je Block
 * ({@link linkSignature}). Sie hält fest, dass schnelleres Verlinken dasselbe Ergebnis liefert — nur anpassen, wenn sich
 * die Regeln der Verlinkung mit Absicht ändern.
 */
export const SUMMARY_FIXTURE_PGN = `[White "Verein A"]
[Black "Muster, Max"]
[Result "0-1"]

1. e4 e6 2. d4 d5 3. Bd3 Nf6 4. e5 Nfd7 5. Ne2 c5 6. c3 Nc6 7. O-O Be7 8. Nd2 Rb8 9. a4 cxd4 10. cxd4 Nb4 11. Bb1 b6 12. Nc3 Bb7 13. f4 a6 14. Nf3 Rc8 15. Bd2 Nb8 16. Qe1 N8c6 17. Na2 a5 18. Nxb4 Nxb4 19. Bxb4 Bxb4 20. Qe2 Bc6 21. Bd3 Qd7 22. b3 O-O 23. h3 Rc7 24. Rac1 Bb7 25. Bb5 Qe7 26. Ba6 Rfc8 27. Rxc7 Qxc7 28. Bxb7 Qxb7 29. Qb5 Qc7 30. Kh2 Qd8 31. Ng1 Rc3 32. Rf3 Rc2 33. Rd3 Qh4 34. Qxb6 Qxf4 35. Kh1 Qf1 36. Rg3 Rc1 0-1`;

export const SUMMARY_FIXTURE_MARKDOWN = `# Verein A – Muster, Max (0-1)

Französische Verteidigung mit 3.Ld3, 36 Züge. Genauigkeit: Weiß 66 %, Schwarz 74 %.

## Die Partie

Die Partie begann als Französische Verteidigung, in der Weiß den Läufer früh nach d3 stellte: 1.e4 e6 2.d4 d5 3.Ld3 Sf6 (Die Engine zieht 3...dxe4 4.Lxe4 Sf6 5.Lf3 Le7 vor.) 4.e5 Sfd7 5.Se2 Mit diesem Zug ist die Partie praktisch ohne Vorgänger: Weder in der Meisterpraxis noch bei Lichess findet sich die Stellung. 5...c5 6.c3! stützt d4, damit die Kette d4–e5 auch nach einem Tausch auf d4 stehen bleibt. 6...Sc6 7.0-0 Le7 8.Sd2 Tb8 9.a4 cxd4 10.cxd4! Nur der Bauer darf zurückschlagen (nach 10.Sxd4 Sdxe5 ist der Bauer e5 ersatzlos weg). 10...Sb4 11.Lb1 b6 12.Sc3 Weiß hat Raum, einen sicheren König und den Läufer auf der Diagonale b1–h7. Schwarz hat am Damenflügel gespielt und noch nicht rochiert, obwohl die Engine die Rochade im achten, neunten und zehnten Zug jeweils als besten Zug nennt (+1,7).

Der nächste Entwicklungszug ließ den König ein weiteres Mal in der Mitte: 12...Lb7?! Der Bauer g7 ist ungedeckt, und die Dame kann ihn sofort angreifen. 13.f4? Der Zug baut die Kette aus, lässt aber den stärkeren Damenzug aus (13.Dg4 Kf8 14.Sf3 Tc8 15.h4, und Schwarz hat das Rochaderecht verloren, +3,0) und erlaubt Schwarz, den Königsflügel zu schließen (13...f5 14.Sf3 a6 15.Ld2 g6, +1,5). 13...a6?! 14.Sf3 Tc8?! Schwarz spielt weiter am Damenflügel, und damit ist das Thema der nächsten Züge gesetzt: der Hebel f4-f5. Das Feld f5 deckt nur der Bauer e6, dahinter stehen Turm f1 und Läufer b1 bereit, und der schwarze König steht gleich daneben auf e8. Schwarz konnte den Hebel mit g7-g6 entschärfen, Weiß konnte ihn spielen; beides blieb aus. 15.Ld2 (15.f5 Sf8 16.De1 exf5 17.Lxf5, +2,9) 15...Sb8? 16.De1? Ein Vorbereitungszug für etwas, das schon ging (16.f5 Tc4 17.fxe6 fxe6 18.b3 Tc7 19.Sg5 Lxg5 20.Dh5+ Kd7, und der König muss zu Fuß gehen, +3,9). 16...S8c6? 17.Sa2 Der folgenreichste der ruhigen Züge: Statt die f-Linie zu öffnen (17.f5 Dd7 18.fxe6 fxe6 19.Dg3, +3,6), bietet Weiß Figurentausch an. 17...a5 18.Sxb4 Sxb4 19.Lxb4 Lxb4 Zwei Leichtfigurenpaare sind vom Brett, die beengte schwarze Stellung hat Luft bekommen, und vom Vorteil ist nur ein Rest geblieben (+0,4).

Noch immer stand der schwarze König auf e8: 20.De2 Lc6 21.Ld3 Dd7? Wieder ohne g7-g6, und der Hebel ging ein weiteres Mal (22.f5 h6 23.fxe6 fxe6 24.Sh4 Kd8 25.Sg6, +2,9). 22.b3 0-0? Jetzt rochiert Schwarz, und zwar genau in die Diagonale des Läufers d3 hinein. Den Bauern h7 deckt nur der König, der Springer f3 erreicht g5, die Dame kommt über g4 oder h5 nach, und alle schwarzen Figuren stehen am Damenflügel: Das Läuferopfer auf h7 gewinnt. 23.h3 (23.Lxh7+ Kxh7 24.Sg5+ Kg6 25.Tf3 f5 26.Tg3 Lb7 27.Sxe6+ Kf7 28.Txg7+, +3,6; nach 24...Kh8 25.Dh5+ Kg8 26.Dh7# ist es sofort aus) 23...Tc7?? Der Turm stellt sich auf ein Feld, das ein Springer von e6 aus zusammen mit dem Turm f8 angreift; das Opfer ist dadurch noch stärker geworden. 24.Tac1?? (24.Lxh7+ Kxh7 25.Sg5+ Kg6 26.Dg4 f6 27.Sxe6+ Kh7 28.Tac1 De7 29.Sxc7, +5,3) 24...Lb7?? Zum dritten Mal bleibt h7 ohne Schutz (24...g6 25.g4 Tfc8 26.f5 Dd8 hält die Stellung bei +1,2). 25.Lb5 Der Läufer greift die Dame an und verlässt dafür die Diagonale, auf der er gewonnen hätte (25.Lxh7+ Kh8 26.Sg5 g6 27.Txc7 Dxc7 28.Dg4, +7,0). 25...De7 Die Gelegenheit ist vorbei, die Stellung fast ausgeglichen (+0,3).

Auf der c-Linie wurde nun abgetauscht: 26.La6?! (Die Engine tauscht zuerst die Türme und behält den Läufer: 26.Txc7 Dxc7 27.Ld3, +0,3) 26...Tfc8 27.Txc7 Dxc7 28.Lxb7 Dxb7 Geblieben sind Dame, Turm und eine Leichtfigur auf jeder Seite, und der schwarze Turm steht auf der einzigen offenen Linie. 29.Db5 Dc7?! (Genauer 29...Dc6 30.Dxc6 Txc6 31.g4 Tc3, und der Turm arbeitet gegen b3, −1,0) 30.Kh2? Ein Abwartezug an der Stelle, an der der alte Hebel das Gleichgewicht hielt (30.f5 Dc6 31.fxe6 fxe6 32.Da6, −0,1). 30...Dd8? Die Dame verlässt die c-Linie wieder, über die sie eindringen konnte (30...g6 31.Da6 Dc2, −2,0). 31.Sg1?! Der Springer geht auf die Grundreihe, das Feld c2 bleibt offen (31.Tf2 h5 32.g4, −0,3). 31...Tc3?! (Zuerst 31...g6 32.Se2 Tc2 hielt den schwarzen Vorteil fest, −0,9) 32.Tf3 Tc2 Der Turm steht auf der zweiten Reihe, die Engine sieht aber noch Gleichstand (0,0), sofern Weiß jetzt f4-f5 spielt.

Der Schluss ging schnell: 33.Td3?? Der Turm verlässt die f-Linie, der Bauer f4 verliert seine einzige Deckung und der Vorstoß seine Kraft (33.f5 Dg5 34.Se2 exf5 35.De8+ Lf8 36.Sf4 mit ausreichendem Gegenspiel, 0,0). 33...Dh4 Dame und Turm stehen am weißen König, und f4 hängt mit Schach. 34.Dxb6?? Der Bauerngewinn am Damenflügel übersieht genau das (34.Tf3 g6 35.Dxb6 Tb2 36.Dc7, −2,4: schlechter, aber noch im Spiel). 34...Dxf4+ 35.Kh1?! (Zäher 35.Tg3 h6 36.Db8+ Kh7 37.Db5 Dxd4, −6,6) 35...Df1 36.Tg3 Tc1 Der Springer g1 ist gefesselt, hinter der Dame steht der Turm, und es droht Matt auf g1 (37.Kh2 Dxg1#); die Engine steht bei −8,9.

Durch die ganze Partie zieht sich ein einziger Bauernzug. Achtmal nennt die Engine f4-f5 als besten weißen Zug, zwischen Zug 14 und Zug 33, und achtmal kam etwas anderes; dazu lag dreimal hintereinander das Opfer auf h7 bereit. Schwarz wiederum ließ g7-g6 Zug um Zug aus und rochierte erst, als die Rochade selbst der Fehler war. Entschieden wurde die Partie dann nicht am schwarzen Königsflügel, sondern am weißen: durch einen Turmzug, der f4 freigab, und einen Bauerngewinn, der das Schach auf f4 übersah.

## Schlüsselstellungen

### 13.f4? – die Dame gehörte nach g4

\`\`\`diagram
ply: 24
arrows: d1g4 g4g7
caption: Nach 12...Lb7 – stärker als 13.f4 war **13.Dg4** mit Angriff auf g7.
\`\`\`

Schwarz hat gerade zum vierten Mal einen anderen Zug der Rochade vorgezogen. Der König steht auf e8, der Bauer g7 ist ungedeckt. 13.Dg4 greift ihn an, und die Engine findet für Schwarz nichts Besseres als 13...Kf8: Nach 14.Sf3 Tc8 15.h4 ist das Rochaderecht weg, der Turm h8 steht hinter dem eigenen König fest, und der h-Bauer ist unterwegs (+3,0).

13.f4 gehört zum selben Angriffsplan, ist aber langsamer: Der Bauer droht noch nichts, und Schwarz konnte mit 13...f5 14.Sf3 a6 15.Ld2 g6 den Königsflügel schließen (+1,5). Dass in der Partie 13...a6 kam, gab Weiß stattdessen den Hebel f4-f5 in die Hand. Bewertung: +3,0 → +1,5.

> **Merksatz:** besseren-zug-suchen

### 16.De1? und 17.Sa2 – der Hebel f4-f5, der nie kam

\`\`\`diagram
ply: 32
arrows: f4f5
caption: Nach 16...S8c6 – **17.f5** öffnet die f-Linie neben dem König auf e8.
\`\`\`

Weiß hat rochiert und alle Figuren entwickelt, bei Schwarz stehen König und Turm h8 noch auf ihren Ausgangsfeldern. Das Feld f5 deckt allein der Bauer e6. Die Engine will hier nur einen Zug sehen: 17.f5 Dd7 18.fxe6 fxe6 19.Dg3, die f-Linie ist offen und die Dame zielt auf g7 (+3,6). Schlägt Schwarz selbst, greift nach 17...exf5 18.Lxf5 der Läufer den Turm c8 an.

Dasselbe galt einen Zug früher (16.f5 Tc4 17.fxe6 fxe6 18.b3 Tc7 19.Sg5 Lxg5 20.Dh5+ Kd7, +3,9) und schon im 15. Zug. Die Partiezüge 15.Ld2, 16.De1 und 17.Sa2 bereiten etwas vor, das längst spielbar war, und der letzte leitet dazu noch Abtausch ein: Nach 17...a5 18.Sxb4 Sxb4 19.Lxb4 Lxb4 fehlen zwei der Figuren, die den Angriff getragen hätten, und die beengte schwarze Stellung ist entlastet. Schwarz seinerseits hätte mit 15...g6 oder 16...g6 das Feld f5 ein zweites Mal decken können; die Engine nennt das an beiden Stellen als besten Zug. Bewertung: +3,6 vor 17.Sa2, +0,7 danach.

> **Merksatz:** entwicklungsvorsprung-oeffnen

### 23.h3, 24.Tac1??, 25.Lb5 – dreimal lag das Opfer auf h7 bereit

\`\`\`diagram
ply: 44
arrows: d3h7 f3g5
caption: Nach 22...0-0 – **23.Lxh7+** Kxh7 24.Sg5+ gewinnt.
\`\`\`

Mit 22...0-0? rochiert Schwarz in die Diagonale des Läufers. Sonst gilt, dass der König früh aus der Mitte gehört, und im achten, neunten, zehnten und zwölften Zug war das hier auch der Rat der Engine. In genau diesem Moment aber musste erst 22...g6 kommen (+1,0), weil h7 nur vom König gedeckt ist und keine schwarze Figur am Königsflügel steht.

Nach 23.Lxh7+ Kxh7 24.Sg5+ hat der König keine gute Wahl: Auf 24...Kh8 folgt 25.Dh5+ Kg8 26.Dh7 matt, auf 24...Kg8 droht nach 25.Dh5 dasselbe Matt, und nach 24...Kg6 25.Tf3 f5 26.Tg3 Lb7 27.Sxe6+ Kf7 28.Txg7+ bricht die Königsstellung zusammen (+3,6). In der Partie kam 23.h3 (+0,2). Schwarz antwortete 23...Tc7??, und das Opfer wurde noch stärker, weil der Turm auf c7 in eine Springergabel läuft:

\`\`\`diagram
fen: 5r2/2rq2p1/1pb1Npk1/p2pP3/Pb1P1PQ1/1P5P/6P1/R4RK1 b - - 0 27
arrows: g4g6 e6c7 e6f8
caption: Variante nach 24.Lxh7+ Kxh7 25.Sg5+ Kg6 26.Dg4 f6 27.Sxe6+ – Abzugsschach der Dame, der Springer greift beide Türme an.
\`\`\`

Es folgt 27...Kh7 28.Tac1 De7 29.Sxc7 (+5,3). Auch 24.Tac1?? war für sich ein brauchbarer Zug (+1,2), nur eben nicht der gewinnende. 24...Lb7?? ließ das Opfer ein drittes Mal zu, jetzt mit +7,0: 25.Lxh7+ Kh8 26.Sg5 g6 27.Txc7 Dxc7 28.Dg4 f6 29.exf6 Txf6 30.Lxg6, und nach 25...Kxh7 26.Txc7 Dxc7 27.Sg5+ Kg8 28.Dh5 droht wieder Matt auf h7. 25.Lb5 nahm den Läufer von der Diagonale; nach 25...De7 stand es +0,3.

Das Muster ist jedes Mal dasselbe: Läufer auf der Diagonale nach h7, Springer mit Zugang nach g5, Dame mit Zugang nach h5 oder g4, kein Verteidiger auf f6. In solchen Stellungen gehört das Schach auf h7 als erster Kandidat auf die Liste, vor jedem ruhigen Zug.

> **Merksatz:** kandidatenzuege

### 33.Td3?? – der Turm verlässt die f-Linie

\`\`\`diagram
ply: 64
arrows: f4f5
caption: Nach 32...Tc2 – **33.f5** hält das Gleichgewicht.
\`\`\`

Im 30. und 31. Zug hat Weiß abgewartet: 30.Kh2? (schon dort hielt 30.f5 Dc6 31.fxe6 fxe6 32.Da6 die Waage, −0,1) und 31.Sg1?!. In derselben Zeit hat Schwarz den Turm über c3 auf die zweite Reihe gebracht. Trotzdem steht es hier noch 0,0, und zwar wegen des Zugs, den die Bauernstruktur seit dem 14. Zug verlangt: 33.f5. Mit dem Turm f3 dahinter richtet er sich gegen f7. Auf 33...Dh4 folgt 34.De8+ Lf8 35.fxe6, und 35...fxe6 scheitert an 36.Txf8 matt. Schwarz muss 33...Dg5 finden, und nach 34.Se2 exf5 35.De8+ Lf8 36.Sf4 hat Weiß genug Gegenspiel (0,0).

33.Td3 gibt beides auf: Der Turm steht nicht mehr hinter dem f-Bauern, und f4 ist von keiner Figur mehr gedeckt. Die Engine antwortet 33...g6 (−2,4), wonach f5 nichts mehr droht; in der Partie kam 33...Dh4 mit Angriff auf f4. Bewertung: 0,0 → −2,4.

> **Merksatz:** plan-aus-der-stellung

### 34.Dxb6?? – f4 hängt mit Schach

\`\`\`diagram
ply: 66
arrows: d3f3 h4f4
caption: Nach 33...Dh4 – **34.Tf3** deckt f4; nach 34.Dxb6 fiel der Bauer mit Schach.
\`\`\`

Die Dame h4 greift den ungedeckten Bauern f4 an, und das Schlagen kommt mit Schach. 34.Tf3 deckt ihn wieder: 34...Dxf4+ ginge dann nicht wegen 35.Txf4, und nach 34...g6 35.Dxb6 Tb2 36.Dc7 steht Weiß schlechter, spielt aber noch (−2,4).

34.Dxb6 holt einen Bauern am anderen Flügel und lässt die Dame weit vom eigenen König. Nach 34...Dxf4+ gab es nur zwei Züge: 35.Tg3, wonach 35...h6 36.Db8+ Kh7 37.Db5 Dxd4 ein verlorenes, aber längeres Spiel ergibt (−6,6), und 35.Kh1?!, wonach 35...Df1 36.Tg3 Tc1 mit Dame und Turm gegen den gefesselten Springer g1 spielt (−8,9). Bewertung: −2,4 → −6,5.

> **Merksatz:** schachs-schlaege-drohungen-pruefen

## Besondere Züge

- **6.c3!** – Nach 5...c5 ist d4 angegriffen. Der Bauer c3 deckt ihn so, dass Weiß nach einem Tausch auf d4 mit einem Bauern zurückschlagen kann und die Kette d4–e5 behält.
- **10.cxd4!** – Die Folge davon: Nur der Bauer auf d4 hält e5. Nach 10.Sxd4 Sdxe5 hätte Schwarz den Bauern e5 einfach geschlagen.

## Was man mitnehmen kann

- **Der Hebel gehört zur Struktur.** Mit der Kette d4–e5 und dem Bauern auf f4 ist f4-f5 gegen e6 der Plan, solange f5 nur von e6 gedeckt ist. Achtmal war das hier der erste Zug der Engine. Ist der Hebel spielbar, braucht er keine weitere Vorbereitung; jeder Vorbereitungszug gibt dem Gegner die Chance auf g7-g6.
- **Das Schach auf h7 zuerst rechnen.** Läufer auf der Diagonale nach h7, Springer f3, Bauer e5, gegnerischer König auf g8 ohne Verteidiger auf f6: In dieser Aufstellung kommt Lxh7+ vor jedem ruhigen Zug auf den Prüfstand. Die Linien waren kurz – Matt auf h7 oder Springergabel auf e6.
- **Mit Raumvorteil und laufendem Angriff den Tausch meiden.** 17.Sa2 und die Schläge auf b4 haben zwei Angriffsfiguren vom Brett genommen und die Bewertung von +3,6 auf +0,4 gebracht.
- **Vor einem Bauerngewinn die Schachs des Gegners ansehen.** Vor 34.Dxb6 hing f4 mit Schach; 34.Tf3 deckte und hielt die Partie im Gang.
- **Faustregeln haben ihren Zeitpunkt.** Dass der Gegner spät rochiert, hilft nur, wenn man die Linien öffnet, bevor er es nachholt. Und wenn er es nachholt, lohnt der Blick, ob er gerade in etwas hineinrochiert – hier war die Rochade im 22. Zug selbst der Fehler.

## Empfohlene Literatur

### Für Weiß

\`\`\`book
id: 1391
why: Der Hebel f4-f5 gegen e6 war achtmal der beste Zug und kam nie, dazu wurde mit Raumvorteil abgetauscht – Bauernhebel und ihr richtiger Zeitpunkt sind das Thema dieses Buchs.
\`\`\`

\`\`\`book
id: 734
why: Dreimal hintereinander blieb das Schach auf h7 ungeprüft; Kotovs Methode, zuerst alle Kandidatenzüge zu sammeln, setzt genau dort an.
\`\`\`

\`\`\`book
id: 1536
why: Der Entwicklungsvorsprung zwischen dem 13. und 22. Zug wurde mit Vorbereitungszügen verbraucht; das Buch behandelt, wie man einen vorübergehenden Vorteil in Angriff umsetzt, solange er da ist.
\`\`\`

### Für Schwarz

\`\`\`book
id: 1417
why: Gegen f4-f5 und gegen das Opfer auf h7 half jedes Mal derselbe Zug g7-g6, der Zug um Zug ausblieb – die Frage nach der Absicht des Gegners wird hier systematisch geübt.
\`\`\`

\`\`\`book
id: 1057
why: Der König blieb bis zum 22. Zug in der Mitte, während am Damenflügel manövriert wurde (Tb8, Sb8–c6); Entwicklung und rechtzeitige Rochade sind hier das Thema.
\`\`\`

\`\`\`book
id: 769
why: Drei Züge in Folge (22...0-0, 23...Tc7, 24...Lb7) ließen dieselbe Kombination zu; praktische Fehlerkontrolle vor dem eigenen Zug ist der Kern dieses Buchs.
\`\`\``;

interface LineMove { san: string; uci: string; fen: string; from: string; to: string; }

/** FNV-1a (32 Bit) über die ganze Zugfolge — jede Abweichung in SAN, Feldern oder FEN ändert die Signatur. */
function lineHash(line: readonly LineMove[]): string {
  let h = 0x811c9dc5;
  for (const ch of line.map(m => [m.san, m.uci, m.fen, m.from, m.to].join(' ')).join('|')) {
    h ^= ch.codePointAt(0)!;
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, '0');
}

function style(p: { bold?: boolean; italic?: boolean; code?: boolean }): string {
  return (p.bold ? 'B' : '') + (p.italic ? 'I' : '') + (p.code ? 'C' : '');
}

function inline(parts: readonly InlinePart[]): string {
  return parts.map(p => p.line
    ? `[${style(p)}:${p.text}@${p.at}/${p.line.length}#${lineHash(p.line)}]`
    : `{${style(p)}:${p.text}}`).join('');
}

function gamePart(p: GameTextPart): string {
  if (p.result) return '<result>';
  return p.move
    ? `[${style(p)}:${p.text}~${p.glyph ?? ''}~${p.mark ?? ''}@${p.move.at}/${p.move.line.length}ply${p.move.ply ?? '-'}#${lineHash(p.move.line)}]`
    : `{${style(p)}:${p.text}}`;
}

/** Eine Zeile je Block: Text, Stil, klickbare Züge mit Platz, Länge und Prüfsumme ihrer Zugfolge. */
export function linkSignature(blocks: readonly SummaryBlock[]): string[] {
  return blocks.map(b => {
    switch (b.kind) {
      case 'heading': return `h${b.level} ` + inline(b.parts);
      case 'paragraph': return 'p ' + inline(b.parts);
      case 'quote': return 'q ' + inline(b.parts);
      case 'list': return (b.ordered ? 'ol ' : 'ul ') + b.items.map(inline).join(' ¦ ');
      case 'table': return 'table ' + [b.head, ...b.rows].map(r => r.map(inline).join(' ¦ ')).join(' ¶ ');
      case 'diagram': return `diagram ply=${b.spec.ply ?? ''} fen=${b.spec.fen ?? ''} arrows=${b.spec.arrows.map(a => a.from + a.to).join(',')} `
        + `o=${b.spec.orientation ?? ''} ` + inline(b.spec.caption ?? []);
      case 'book': return `book ${b.id} ` + inline(b.whyParts ?? []);
      case 'maxim': return 'maxim ' + b.value;
      case 'gameText': return 'gameText ' + b.segments.map(s => (s.variation ? '(V)' : '(T)') + s.parts.map(gamePart).join('')).join('');
      default: return b.kind;
    }
  });
}

export const SUMMARY_FIXTURE_EXPECTED: readonly string[] = [
  "h1 {:Verein A – Muster, Max (0-1)}",
  "p {:Französische Verteidigung mit }[:3.Ld3@5/5#a31e63c3]{:, 36 Züge. Genauigkeit: Weiß 66 %, Schwarz 74 %.}",
  "h2 {:Die Partie}",
  "gameText (T){:Die Partie begann als Französische Verteidigung, in der Weiß den Läufer früh nach d3 stellte: }[:1.e4~~@1/6ply1#5efba2a2]{: }[:e6~~@2/6ply2#5efba2a2]{: }[:2.d4~~@3/6ply3#5efba2a2]{: }[:d5~~@4/6ply4#5efba2a2]{: }[:3.Ld3~~@5/6ply5#5efba2a2]{: }[:Sf6~~@6/6ply6#5efba2a2]{: }(V){:(Die Engine zieht }[:3...dxe4~~@6/10ply-#2f25a06c]{: }[:4.Lxe4~~@7/10ply-#2f25a06c]{: }[:Sf6~~@8/10ply-#2f25a06c]{: }[:5.Lf3~~@9/10ply-#2f25a06c]{: }[:Le7~~@10/10ply-#2f25a06c]{: vor.)}(T){: }[:4.e5~~@7/9ply7#9fdd0da6]{: }[:Sfd7~~@8/9ply8#9fdd0da6]{: }[:5.Se2~~@9/9ply9#9fdd0da6]{: Mit diesem Zug ist die Partie praktisch ohne Vorgänger: Weder in der Meisterpraxis noch bei Lichess findet sich die Stellung. }[:5...c5~~@10/11ply10#06b8cf89]{: }[:6.c3~!~good@11/11ply11#06b8cf89]{: stützt d4, damit die Kette d4–e5 auch nach einem Tausch auf d4 stehen bleibt. }[:6...Sc6~~@12/19ply12#330d0a76]{: }[:7.0-0~~@13/19ply13#330d0a76]{: }[:Le7~~@14/19ply14#330d0a76]{: }[:8.Sd2~~@15/19ply15#330d0a76]{: }[:Tb8~~@16/19ply16#330d0a76]{: }[:9.a4~~@17/19ply17#330d0a76]{: }[:cxd4~~@18/19ply18#330d0a76]{: }[:10.cxd4~!~good@19/19ply19#330d0a76]{: Nur der Bauer darf zurückschlagen }(V){:(nach }[:10.Sxd4~~@19/20ply-#13f0bf06]{: }[:Sdxe5~~@20/20ply-#13f0bf06]{: ist der Bauer e5 ersatzlos weg)}(T){:. }[:10...Sb4~~@20/23ply20#31224d51]{: }[:11.Lb1~~@21/23ply21#31224d51]{: }[:b6~~@22/23ply22#31224d51]{: }[:12.Sc3~~@23/23ply23#31224d51]{: Weiß hat Raum, einen sicheren König und den Läufer auf der Diagonale b1–h7. Schwarz hat am Damenflügel gespielt und noch nicht rochiert, obwohl die Engine die Rochade im achten, neunten und zehnten Zug jeweils als besten Zug nennt }{:(+1,7)}{:.}",
  "gameText (T){:Der nächste Entwicklungszug ließ den König ein weiteres Mal in der Mitte: }[:12...Lb7~?!~bad@24/24ply24#68ebfe3e]{: Der Bauer g7 ist ungedeckt, und die Dame kann ihn sofort angreifen. }[:13.f4~?~bad@25/25ply25#3224dcbf]{: Der Zug baut die Kette aus, lässt aber den stärkeren Damenzug aus }(V){:(}[:13.Dg4~~@25/29ply-#35351fee]{: }[:Kf8~~@26/29ply-#35351fee]{: }[:14.Sf3~~@27/29ply-#35351fee]{: }[:Tc8~~@28/29ply-#35351fee]{: }[:15.h4~~@29/29ply-#35351fee]{:, und Schwarz hat das Rochaderecht verloren, +3,0)}(T){: und erlaubt Schwarz, den Königsflügel zu schließen }(V){:(}[:13...f5~~@26/30ply-#069c39f3]{: }[:14.Sf3~~@27/30ply-#069c39f3]{: }[:a6~~@28/30ply-#069c39f3]{: }[:15.Ld2~~@29/30ply-#069c39f3]{: }[:g6~~@30/30ply-#069c39f3]{:, +1,5)}(T){:. }[:13...a6~?!~bad@26/28ply26#9a9eb7de]{: }[:14.Sf3~~@27/28ply27#9a9eb7de]{: }[:Tc8~?!~bad@28/28ply28#9a9eb7de]{: Schwarz spielt weiter am Damenflügel, und damit ist das Thema der nächsten Züge gesetzt: der Hebel f4-f5. Das Feld f5 deckt nur der Bauer e6, dahinter stehen Turm f1 und Läufer b1 bereit, und der schwarze König steht gleich daneben auf e8. Schwarz konnte den Hebel mit g7-g6 entschärfen, Weiß konnte ihn spielen; beides blieb aus. }[:15.Ld2~~@29/29ply29#dd00e638]{: }(V){:(}[:15.f5~~@29/33ply-#27c36af2]{: }[:Sf8~~@30/33ply-#27c36af2]{: }[:16.De1~~@31/33ply-#27c36af2]{: }[:exf5~~@32/33ply-#27c36af2]{: }[:17.Lxf5~~@33/33ply-#27c36af2]{:, +2,9)}(T){: }[:15...Sb8~?~bad@30/31ply30#11677b4f]{: }[:16.De1~?~bad@31/31ply31#11677b4f]{: Ein Vorbereitungszug für etwas, das schon ging }(V){:(}[:16.f5~~@31/40ply-#e2ee76cd]{: }[:Tc4~~@32/40ply-#e2ee76cd]{: }[:17.fxe6~~@33/40ply-#e2ee76cd]{: }[:fxe6~~@34/40ply-#e2ee76cd]{: }[:18.b3~~@35/40ply-#e2ee76cd]{: }[:Tc7~~@36/40ply-#e2ee76cd]{: }[:19.Sg5~~@37/40ply-#e2ee76cd]{: }[:Lxg5~~@38/40ply-#e2ee76cd]{: }[:20.Dh5+~~@39/40ply-#e2ee76cd]{: }[:Kd7~~@40/40ply-#e2ee76cd]{:, und der König muss zu Fuß gehen, +3,9)}(T){:. }[:16...S8c6~?~bad@32/33ply32#575c10cd]{: }[:17.Sa2~~@33/33ply33#575c10cd]{: Der folgenreichste der ruhigen Züge: Statt die f-Linie zu öffnen }(V){:(}[:17.f5~~@33/37ply-#3a334ab6]{: }[:Dd7~~@34/37ply-#3a334ab6]{: }[:18.fxe6~~@35/37ply-#3a334ab6]{: }[:fxe6~~@36/37ply-#3a334ab6]{: }[:19.Dg3~~@37/37ply-#3a334ab6]{:, +3,6)}(T){:, bietet Weiß Figurentausch an. }[:17...a5~~@34/38ply34#a5423b09]{: }[:18.Sxb4~~@35/38ply35#a5423b09]{: }[:Sxb4~~@36/38ply36#a5423b09]{: }[:19.Lxb4~~@37/38ply37#a5423b09]{: }[:Lxb4~~@38/38ply38#a5423b09]{: Zwei Leichtfigurenpaare sind vom Brett, die beengte schwarze Stellung hat Luft bekommen, und vom Vorteil ist nur ein Rest geblieben }{:(+0,4)}{:.}",
  "gameText (T){:Noch immer stand der schwarze König auf e8: }[:20.De2~~@39/42ply39#0ec7bdff]{: }[:Lc6~~@40/42ply40#0ec7bdff]{: }[:21.Ld3~~@41/42ply41#0ec7bdff]{: }[:Dd7~?~bad@42/42ply42#0ec7bdff]{: Wieder ohne g7-g6, und der Hebel ging ein weiteres Mal }(V){:(}[:22.f5~~@43/49ply-#f4088f3b]{: }[:h6~~@44/49ply-#f4088f3b]{: }[:23.fxe6~~@45/49ply-#f4088f3b]{: }[:fxe6~~@46/49ply-#f4088f3b]{: }[:24.Sh4~~@47/49ply-#f4088f3b]{: }[:Kd8~~@48/49ply-#f4088f3b]{: }[:25.Sg6~~@49/49ply-#f4088f3b]{:, +2,9)}(T){:. }[:22.b3~~@43/44ply43#e5941c20]{: }[:0-0~?~bad@44/44ply44#e5941c20]{: Jetzt rochiert Schwarz, und zwar genau in die Diagonale des Läufers d3 hinein. Den Bauern h7 deckt nur der König, der Springer f3 erreicht g5, die Dame kommt über g4 oder h5 nach, und alle schwarzen Figuren stehen am Damenflügel: Das Läuferopfer auf h7 gewinnt. }[:23.h3~~@45/45ply45#0bea0cc1]{: }(V){:(}[:23.Lxh7+~~@45/55ply-#553d9f9a]{: }[:Kxh7~~@46/55ply-#553d9f9a]{: }[:24.Sg5+~~@47/55ply-#553d9f9a]{: }[:Kg6~~@48/55ply-#553d9f9a]{: }[:25.Tf3~~@49/55ply-#553d9f9a]{: }[:f5~~@50/55ply-#553d9f9a]{: }[:26.Tg3~~@51/55ply-#553d9f9a]{: }[:Lb7~~@52/55ply-#553d9f9a]{: }[:27.Sxe6+~~@53/55ply-#553d9f9a]{: }[:Kf7~~@54/55ply-#553d9f9a]{: }[:28.Txg7+~~@55/55ply-#553d9f9a]{:, +3,6; nach }[:24...Kh8~~@48/51ply-#16cef4df]{: }[:25.Dh5+~~@49/51ply-#16cef4df]{: }[:Kg8~~@50/51ply-#16cef4df]{: }[:26.Dh7#~~@51/51ply-#16cef4df]{: ist es sofort aus)}(T){: }[:23...Tc7~??~bad@46/46ply46#c1e31248]{: Der Turm stellt sich auf ein Feld, das ein Springer von e6 aus zusammen mit dem Turm f8 angreift; das Opfer ist dadurch noch stärker geworden. }[:24.Tac1~??~bad@47/47ply47#08b534a0]{: }(V){:(}[:24.Lxh7+~~@47/57ply-#17dea15e]{: }[:Kxh7~~@48/57ply-#17dea15e]{: }[:25.Sg5+~~@49/57ply-#17dea15e]{: }[:Kg6~~@50/57ply-#17dea15e]{: }[:26.Dg4~~@51/57ply-#17dea15e]{: }[:f6~~@52/57ply-#17dea15e]{: }[:27.Sxe6+~~@53/57ply-#17dea15e]{: }[:Kh7~~@54/57ply-#17dea15e]{: }[:28.Tac1~~@55/57ply-#17dea15e]{: }[:De7~~@56/57ply-#17dea15e]{: }[:29.Sxc7~~@57/57ply-#17dea15e]{:, +5,3)}(T){: }[:24...Lb7~??~bad@48/48ply48#884e073f]{: Zum dritten Mal bleibt h7 ohne Schutz }(V){:(}[:24...g6~~@48/52ply-#d61b18a8]{: }[:25.g4~~@49/52ply-#d61b18a8]{: }[:Tfc8~~@50/52ply-#d61b18a8]{: }[:26.f5~~@51/52ply-#d61b18a8]{: }[:Dd8~~@52/52ply-#d61b18a8]{: hält die Stellung bei +1,2)}(T){:. }[:25.Lb5~~@49/49ply49#92224294]{: Der Läufer greift die Dame an und verlässt dafür die Diagonale, auf der er gewonnen hätte }(V){:(}[:25.Lxh7+~~@49/55ply-#b3f4c13f]{: }[:Kh8~~@50/55ply-#b3f4c13f]{: }[:26.Sg5~~@51/55ply-#b3f4c13f]{: }[:g6~~@52/55ply-#b3f4c13f]{: }[:27.Txc7~~@53/55ply-#b3f4c13f]{: }[:Dxc7~~@54/55ply-#b3f4c13f]{: }[:28.Dg4~~@55/55ply-#b3f4c13f]{:, +7,0)}(T){:. }[:25...De7~~@50/50ply50#8321db1c]{: Die Gelegenheit ist vorbei, die Stellung fast ausgeglichen }{:(+0,3)}{:.}",
  "gameText (T){:Auf der c-Linie wurde nun abgetauscht: }[:26.La6~?!~bad@51/51ply51#12845454]{: }(V){:(Die Engine tauscht zuerst die Türme und behält den Läufer: }[:26.Txc7~~@51/53ply-#9abf8d18]{: }[:Dxc7~~@52/53ply-#9abf8d18]{: }[:27.Ld3~~@53/53ply-#9abf8d18]{:, +0,3)}(T){: }[:26...Tfc8~~@52/56ply52#0dee6df7]{: }[:27.Txc7~~@53/56ply53#0dee6df7]{: }[:Dxc7~~@54/56ply54#0dee6df7]{: }[:28.Lxb7~~@55/56ply55#0dee6df7]{: }[:Dxb7~~@56/56ply56#0dee6df7]{: Geblieben sind Dame, Turm und eine Leichtfigur auf jeder Seite, und der schwarze Turm steht auf der einzigen offenen Linie. }[:29.Db5~~@57/58ply57#298084c8]{: }[:Dc7~?!~bad@58/58ply58#298084c8]{: }(V){:(Genauer }[:29...Dc6~~@58/62ply-#f3cea8e2]{: }[:30.Dxc6~~@59/62ply-#f3cea8e2]{: }[:Txc6~~@60/62ply-#f3cea8e2]{: }[:31.g4~~@61/62ply-#f3cea8e2]{: }[:Tc3~~@62/62ply-#f3cea8e2]{:, und der Turm arbeitet gegen b3, −1,0)}(T){: }[:30.Kh2~?~bad@59/59ply59#05b20c58]{: Ein Abwartezug an der Stelle, an der der alte Hebel das Gleichgewicht hielt }(V){:(}[:30.f5~~@59/63ply-#693bc824]{: }[:Dc6~~@60/63ply-#693bc824]{: }[:31.fxe6~~@61/63ply-#693bc824]{: }[:fxe6~~@62/63ply-#693bc824]{: }[:32.Da6~~@63/63ply-#693bc824]{:, −0,1)}(T){:. }[:30...Dd8~?~bad@60/60ply60#fbc35cc3]{: Die Dame verlässt die c-Linie wieder, über die sie eindringen konnte }(V){:(}[:30...g6~~@60/62ply-#fadf50d0]{: }[:31.Da6~~@61/62ply-#fadf50d0]{: }[:Dc2~~@62/62ply-#fadf50d0]{:, −2,0)}(T){:. }[:31.Sg1~?!~bad@61/61ply61#2808047f]{: Der Springer geht auf die Grundreihe, das Feld c2 bleibt offen }(V){:(}[:31.Tf2~~@61/63ply-#f230c898]{: }[:h5~~@62/63ply-#f230c898]{: }[:32.g4~~@63/63ply-#f230c898]{:, −0,3)}(T){:. }[:31...Tc3~?!~bad@62/62ply62#339af642]{: }(V){:(Zuerst }[:31...g6~~@62/64ply-#efb8fe10]{: }[:32.Se2~~@63/64ply-#efb8fe10]{: }[:Tc2~~@64/64ply-#efb8fe10]{: hielt den schwarzen Vorteil fest, −0,9)}(T){: }[:32.Tf3~~@63/64ply63#146e2f19]{: }[:Tc2~~@64/64ply64#146e2f19]{: Der Turm steht auf der zweiten Reihe, die Engine sieht aber noch Gleichstand }{:(0,0)}{:, sofern Weiß jetzt f4-f5 spielt.}",
  "gameText (T){:Der Schluss ging schnell: }[:33.Td3~??~bad@65/65ply65#d60f64d2]{: Der Turm verlässt die f-Linie, der Bauer f4 verliert seine einzige Deckung und der Vorstoß seine Kraft }(V){:(}[:33.f5~~@65/71ply-#d2fee9e2]{: }[:Dg5~~@66/71ply-#d2fee9e2]{: }[:34.Se2~~@67/71ply-#d2fee9e2]{: }[:exf5~~@68/71ply-#d2fee9e2]{: }[:35.De8+~~@69/71ply-#d2fee9e2]{: }[:Lf8~~@70/71ply-#d2fee9e2]{: }[:36.Sf4~~@71/71ply-#d2fee9e2]{: mit ausreichendem Gegenspiel, 0,0)}(T){:. }[:33...Dh4~~@66/66ply66#1a33c8ab]{: Dame und Turm stehen am weißen König, und f4 hängt mit Schach. }[:34.Dxb6~??~bad@67/67ply67#e8cb1cf7]{: Der Bauerngewinn am Damenflügel übersieht genau das }(V){:(}[:34.Tf3~~@67/71ply-#a43c56ff]{: }[:g6~~@68/71ply-#a43c56ff]{: }[:35.Dxb6~~@69/71ply-#a43c56ff]{: }[:Tb2~~@70/71ply-#a43c56ff]{: }[:36.Dc7~~@71/71ply-#a43c56ff]{:, −2,4: schlechter, aber noch im Spiel)}(T){:. }[:34...Dxf4+~~@68/69ply68#ccb6e0a3]{: }[:35.Kh1~?!~bad@69/69ply69#ccb6e0a3]{: }(V){:(Zäher }[:35.Tg3~~@69/74ply-#0d55b9c0]{: }[:h6~~@70/74ply-#0d55b9c0]{: }[:36.Db8+~~@71/74ply-#0d55b9c0]{: }[:Kh7~~@72/74ply-#0d55b9c0]{: }[:37.Db5~~@73/74ply-#0d55b9c0]{: }[:Dxd4~~@74/74ply-#0d55b9c0]{:, −6,6)}(T){: }[:35...Df1~~@70/72ply70#0fc97f29]{: }[:36.Tg3~~@71/72ply71#0fc97f29]{: }[:Tc1~~@72/72ply72#0fc97f29]<result>{: Der Springer g1 ist gefesselt, hinter der Dame steht der Turm, und es droht Matt auf g1 }(V){:(}[:37.Kh2~~@73/74ply-#8ead7060]{: }[:Dxg1#~~@74/74ply-#8ead7060]{:)}(T){:; die Engine steht bei −8,9.}",
  "gameText (T){:Durch die ganze Partie zieht sich ein einziger Bauernzug. Achtmal nennt die Engine f4-f5 als besten weißen Zug, zwischen Zug 14 und Zug 33, und achtmal kam etwas anderes; dazu lag dreimal hintereinander das Opfer auf h7 bereit. Schwarz wiederum ließ g7-g6 Zug um Zug aus und rochierte erst, als die Rochade selbst der Fehler war. Entschieden wurde die Partie dann nicht am schwarzen Königsflügel, sondern am weißen: durch einen Turmzug, der f4 freigab, und einen Bauerngewinn, der das Schach auf f4 übersah.}",
  "h2 {:Schlüsselstellungen}",
  "h3 [:13.f4?@25/25#3224dcbf]{: – die Dame gehörte nach g4}",
  "diagram ply=24 fen= arrows=d1g4,g4g7 o= {:Nach }[:12...Lb7@24/24#68ebfe3e]{: – stärker als }[:13.f4@25/25#3224dcbf]{: war }[B:13.Dg4@25/25#a1902686]{: mit Angriff auf g7.}",
  "p {:Schwarz hat gerade zum vierten Mal einen anderen Zug der Rochade vorgezogen. Der König steht auf e8, der Bauer g7 ist ungedeckt. }[:13.Dg4@25/25#a1902686]{: greift ihn an, und die Engine findet für Schwarz nichts Besseres als }[:13...Kf8@26/26#05c58942]{:: Nach }[:14.Sf3@27/29#35351fee]{: }[:Tc8@28/29#35351fee]{: }[:15.h4@29/29#35351fee]{: ist das Rochaderecht weg, der Turm h8 steht hinter dem eigenen König fest, und der h-Bauer ist unterwegs (+3,0).}",
  "p [:13.f4@25/25#3224dcbf]{: gehört zum selben Angriffsplan, ist aber langsamer: Der Bauer droht noch nichts, und Schwarz konnte mit }[:13...f5@26/30#069c39f3]{: }[:14.Sf3@27/30#069c39f3]{: }[:a6@28/30#069c39f3]{: }[:15.Ld2@29/30#069c39f3]{: }[:g6@30/30#069c39f3]{: den Königsflügel schließen (+1,5). Dass in der Partie }[:13...a6@26/26#d383640b]{: kam, gab Weiß stattdessen den Hebel f4-f5 in die Hand. Bewertung: +3,0 → +1,5.}",
  "maxim besseren-zug-suchen",
  "h3 [:16.De1?@31/31#11677b4f]{: und }[:17.Sa2@33/33#575c10cd]{: – der Hebel f4-f5, der nie kam}",
  "diagram ply=32 fen= arrows=f4f5 o= {:Nach }[:16...S8c6@32/32#3596208a]{: – }[B:17.f5@33/33#291908ea]{: öffnet die f-Linie neben dem König auf e8.}",
  "p {:Weiß hat rochiert und alle Figuren entwickelt, bei Schwarz stehen König und Turm h8 noch auf ihren Ausgangsfeldern. Das Feld f5 deckt allein der Bauer e6. Die Engine will hier nur einen Zug sehen: }[:17.f5@33/37#3a334ab6]{: }[:Dd7@34/37#3a334ab6]{: }[:18.fxe6@35/37#3a334ab6]{: }[:fxe6@36/37#3a334ab6]{: }[:19.Dg3@37/37#3a334ab6]{:, die f-Linie ist offen und die Dame zielt auf g7 (+3,6). Schlägt Schwarz selbst, greift nach }[:17...exf5@34/35#2fd0c275]{: }[:18.Lxf5@35/35#2fd0c275]{: der Läufer den Turm c8 an.}",
  "p {:Dasselbe galt einen Zug früher (}[:16.f5@31/40#e2ee76cd]{: }[:Tc4@32/40#e2ee76cd]{: }[:17.fxe6@33/40#e2ee76cd]{: }[:fxe6@34/40#e2ee76cd]{: }[:18.b3@35/40#e2ee76cd]{: }[:Tc7@36/40#e2ee76cd]{: }[:19.Sg5@37/40#e2ee76cd]{: }[:Lxg5@38/40#e2ee76cd]{: }[:20.Dh5+@39/40#e2ee76cd]{: }[:Kd7@40/40#e2ee76cd]{:, +3,9) und schon im 15. Zug. Die Partiezüge }[:15.Ld2@29/29#dd00e638]{:, }[:16.De1@31/31#11677b4f]{: und }[:17.Sa2@33/33#575c10cd]{: bereiten etwas vor, das längst spielbar war, und der letzte leitet dazu noch Abtausch ein: Nach }[:17...a5@34/38#a5423b09]{: }[:18.Sxb4@35/38#a5423b09]{: }[:Sxb4@36/38#a5423b09]{: }[:19.Lxb4@37/38#a5423b09]{: }[:Lxb4@38/38#a5423b09]{: fehlen zwei der Figuren, die den Angriff getragen hätten, und die beengte schwarze Stellung ist entlastet. Schwarz seinerseits hätte mit }[:15...g6@30/30#cf98c296]{: oder }[:16...g6@32/32#3ea264d8]{: das Feld f5 ein zweites Mal decken können; die Engine nennt das an beiden Stellen als besten Zug. Bewertung: +3,6 vor }[:17.Sa2@33/33#7c490c2b]{:, +0,7 danach.}",
  "maxim entwicklungsvorsprung-oeffnen",
  "h3 [:23.h3@45/45#0bea0cc1]{:, }[:24.Tac1??@47/47#08b534a0]{:, }[:25.Lb5@49/49#92224294]{: – dreimal lag das Opfer auf h7 bereit}",
  "diagram ply=44 fen= arrows=d3h7,f3g5 o= {:Nach }[:22...0-0@44/44#e5941c20]{: – }[B:23.Lxh7+@45/45#45cb0785]{: Kxh7 }[:24.Sg5+@47/47#f8b952ef]{: gewinnt.}",
  "p {:Mit }[:22...0-0?@44/44#e5941c20]{: rochiert Schwarz in die Diagonale des Läufers. Sonst gilt, dass der König früh aus der Mitte gehört, und im achten, neunten, zehnten und zwölften Zug war das hier auch der Rat der Engine. In genau diesem Moment aber musste erst }[:22...g6@44/44#a368f801]{: kommen (+1,0), weil h7 nur vom König gedeckt ist und keine schwarze Figur am Königsflügel steht.}",
  "p {:Nach }[:23.Lxh7+@45/47#4efd6516]{: }[:Kxh7@46/47#4efd6516]{: }[:24.Sg5+@47/47#4efd6516]{: hat der König keine gute Wahl: Auf }[:24...Kh8@48/48#d498afd4]{: folgt }[:25.Dh5+@49/51#16cef4df]{: }[:Kg8@50/51#16cef4df]{: }[:26.Dh7@51/51#16cef4df]{: matt, auf }[:24...Kg8@48/48#08835ead]{: droht nach }[:25.Dh5@49/49#cc1374aa]{: dasselbe Matt, und nach }[:24...Kg6@48/55#553d9f9a]{: }[:25.Tf3@49/55#553d9f9a]{: }[:f5@50/55#553d9f9a]{: }[:26.Tg3@51/55#553d9f9a]{: }[:Lb7@52/55#553d9f9a]{: }[:27.Sxe6+@53/55#553d9f9a]{: }[:Kf7@54/55#553d9f9a]{: }[:28.Txg7+@55/55#553d9f9a]{: bricht die Königsstellung zusammen (+3,6). In der Partie kam }[:23.h3@45/45#0bea0cc1]{: (+0,2). Schwarz antwortete }[:23...Tc7??@46/46#c1e31248]{:, und das Opfer wurde noch stärker, weil der Turm auf c7 in eine Springergabel läuft:}",
  "diagram ply= fen=5r2/2rq2p1/1pb1Npk1/p2pP3/Pb1P1PQ1/1P5P/6P1/R4RK1 b - - 0 27 arrows=g4g6,e6c7,e6f8 o= {:Variante nach }[:24.Lxh7+@47/53#fe291193]{: }[:Kxh7@48/53#fe291193]{: }[:25.Sg5+@49/53#fe291193]{: }[:Kg6@50/53#fe291193]{: }[:26.Dg4@51/53#fe291193]{: }[:f6@52/53#fe291193]{: }[:27.Sxe6+@53/53#fe291193]{: – Abzugsschach der Dame, der Springer greift beide Türme an.}",
  "p {:Es folgt 27...Kh7 28.Tac1 De7 29.Sxc7 (+5,3). Auch }[:24.Tac1??@47/47#08b534a0]{: war für sich ein brauchbarer Zug (+1,2), nur eben nicht der gewinnende. }[:24...Lb7??@48/48#884e073f]{: ließ das Opfer ein drittes Mal zu, jetzt mit +7,0: }[:25.Lxh7+@49/59#b43f345f]{: }[:Kh8@50/59#b43f345f]{: }[:26.Sg5@51/59#b43f345f]{: }[:g6@52/59#b43f345f]{: }[:27.Txc7@53/59#b43f345f]{: }[:Dxc7@54/59#b43f345f]{: }[:28.Dg4@55/59#b43f345f]{: }[:f6@56/59#b43f345f]{: }[:29.exf6@57/59#b43f345f]{: }[:Txf6@58/59#b43f345f]{: }[:30.Lxg6@59/59#b43f345f]{:, und nach }[:25...Kxh7@50/55#663220ba]{: }[:26.Txc7@51/55#663220ba]{: }[:Dxc7@52/55#663220ba]{: }[:27.Sg5+@53/55#663220ba]{: }[:Kg8@54/55#663220ba]{: }[:28.Dh5@55/55#663220ba]{: droht wieder Matt auf h7. }[:25.Lb5@49/49#92224294]{: nahm den Läufer von der Diagonale; nach }[:25...De7@50/50#8321db1c]{: stand es +0,3.}",
  "p {:Das Muster ist jedes Mal dasselbe: Läufer auf der Diagonale nach h7, Springer mit Zugang nach g5, Dame mit Zugang nach h5 oder g4, kein Verteidiger auf f6. In solchen Stellungen gehört das Schach auf h7 als erster Kandidat auf die Liste, vor jedem ruhigen Zug.}",
  "maxim kandidatenzuege",
  "h3 [:33.Td3??@65/65#d60f64d2]{: – der Turm verlässt die f-Linie}",
  "diagram ply=64 fen= arrows=f4f5 o= {:Nach }[:32...Tc2@64/64#146e2f19]{: – }[B:33.f5@65/65#d8824be1]{: hält das Gleichgewicht.}",
  "p {:Im 30. und 31. Zug hat Weiß abgewartet: }[:30.Kh2?@59/59#05b20c58]{: (schon dort hielt }[:30.f5@59/63#693bc824]{: }[:Dc6@60/63#693bc824]{: }[:31.fxe6@61/63#693bc824]{: }[:fxe6@62/63#693bc824]{: }[:32.Da6@63/63#693bc824]{: die Waage, −0,1) und }[:31.Sg1?!@61/61#2808047f]{:. In derselben Zeit hat Schwarz den Turm über c3 auf die zweite Reihe gebracht. Trotzdem steht es hier noch 0,0, und zwar wegen des Zugs, den die Bauernstruktur seit dem 14. Zug verlangt: }[:33.f5@65/65#d8824be1]{:. Mit dem Turm f3 dahinter richtet er sich gegen f7. Auf }[:33...Dh4@66/66#3d454136]{: folgt }[:34.De8+@67/69#4d48a577]{: }[:Lf8@68/69#4d48a577]{: }[:35.fxe6@69/69#4d48a577]{:, und }[:35...fxe6@70/70#29323d4d]{: scheitert an }[:36.Txf8@71/71#a18606bf]{: matt. Schwarz muss }[:33...Dg5@66/66#720b6b62]{: finden, und nach }[:34.Se2@67/71#d2fee9e2]{: }[:exf5@68/71#d2fee9e2]{: }[:35.De8+@69/71#d2fee9e2]{: }[:Lf8@70/71#d2fee9e2]{: }[:36.Sf4@71/71#d2fee9e2]{: hat Weiß genug Gegenspiel (0,0).}",
  "p [:33.Td3@65/65#d60f64d2]{: gibt beides auf: Der Turm steht nicht mehr hinter dem f-Bauern, und f4 ist von keiner Figur mehr gedeckt. Die Engine antwortet }[:33...g6@66/66#37024006]{: (−2,4), wonach f5 nichts mehr droht; in der Partie kam }[:33...Dh4@66/66#1a33c8ab]{: mit Angriff auf f4. Bewertung: 0,0 → −2,4.}",
  "maxim plan-aus-der-stellung",
  "h3 [:34.Dxb6??@67/67#e8cb1cf7]{: – f4 hängt mit Schach}",
  "diagram ply=66 fen= arrows=d3f3,h4f4 o= {:Nach }[:33...Dh4@66/66#1a33c8ab]{: – }[B:34.Tf3@67/67#1ccb9106]{: deckt f4; nach }[:34.Dxb6@67/67#e8cb1cf7]{: fiel der Bauer mit Schach.}",
  "p {:Die Dame h4 greift den ungedeckten Bauern f4 an, und das Schlagen kommt mit Schach. }[:34.Tf3@67/67#1ccb9106]{: deckt ihn wieder: }[:34...Dxf4+@68/68#eddd6637]{: ginge dann nicht wegen }[:35.Txf4@69/69#8b15f285]{:, und nach }[:34...g6@68/71#a43c56ff]{: }[:35.Dxb6@69/71#a43c56ff]{: }[:Tb2@70/71#a43c56ff]{: }[:36.Dc7@71/71#a43c56ff]{: steht Weiß schlechter, spielt aber noch (−2,4).}",
  "p [:34.Dxb6@67/67#e8cb1cf7]{: holt einen Bauern am anderen Flügel und lässt die Dame weit vom eigenen König. Nach }[:34...Dxf4+@68/68#4cee2d37]{: gab es nur zwei Züge: }[:35.Tg3@69/69#c3f9d171]{:, wonach }[:35...h6@70/74#0d55b9c0]{: }[:36.Db8+@71/74#0d55b9c0]{: }[:Kh7@72/74#0d55b9c0]{: }[:37.Db5@73/74#0d55b9c0]{: }[:Dxd4@74/74#0d55b9c0]{: ein verlorenes, aber längeres Spiel ergibt (−6,6), und }[:35.Kh1?!@69/69#ccb6e0a3]{:, wonach }[:35...Df1@70/72#0fc97f29]{: }[:36.Tg3@71/72#0fc97f29]{: }[:Tc1@72/72#0fc97f29]{: mit Dame und Turm gegen den gefesselten Springer g1 spielt (−8,9). Bewertung: −2,4 → −6,5.}",
  "maxim schachs-schlaege-drohungen-pruefen",
  "h2 {:Besondere Züge}",
  "ul [B:6.c3!@11/11#06b8cf89]{: – Nach }[:5...c5@10/10#5c665a65]{: ist d4 angegriffen. Der Bauer c3 deckt ihn so, dass Weiß nach einem Tausch auf d4 mit einem Bauern zurückschlagen kann und die Kette d4–e5 behält.} ¦ [B:10.cxd4!@19/19#330d0a76]{: – Die Folge davon: Nur der Bauer auf d4 hält e5. Nach }[:10.Sxd4@19/20#13f0bf06]{: }[:Sdxe5@20/20#13f0bf06]{: hätte Schwarz den Bauern e5 einfach geschlagen.}",
  "h2 {:Was man mitnehmen kann}",
  "ul {B:Der Hebel gehört zur Struktur.}{: Mit der Kette d4–e5 und dem Bauern auf f4 ist f4-f5 gegen e6 der Plan, solange f5 nur von e6 gedeckt ist. Achtmal war das hier der erste Zug der Engine. Ist der Hebel spielbar, braucht er keine weitere Vorbereitung; jeder Vorbereitungszug gibt dem Gegner die Chance auf g7-g6.} ¦ {B:Das Schach auf h7 zuerst rechnen.}{: Läufer auf der Diagonale nach h7, Springer f3, Bauer e5, gegnerischer König auf g8 ohne Verteidiger auf f6: In dieser Aufstellung kommt Lxh7+ vor jedem ruhigen Zug auf den Prüfstand. Die Linien waren kurz – Matt auf h7 oder Springergabel auf e6.} ¦ {B:Mit Raumvorteil und laufendem Angriff den Tausch meiden.}{: }[:17.Sa2@33/33#575c10cd]{: und die Schläge auf b4 haben zwei Angriffsfiguren vom Brett genommen und die Bewertung von +3,6 auf +0,4 gebracht.} ¦ {B:Vor einem Bauerngewinn die Schachs des Gegners ansehen.}{: Vor }[:34.Dxb6@67/67#e8cb1cf7]{: hing f4 mit Schach; }[:34.Tf3@67/67#1ccb9106]{: deckte und hielt die Partie im Gang.} ¦ {B:Faustregeln haben ihren Zeitpunkt.}{: Dass der Gegner spät rochiert, hilft nur, wenn man die Linien öffnet, bevor er es nachholt. Und wenn er es nachholt, lohnt der Blick, ob er gerade in etwas hineinrochiert – hier war die Rochade im 22. Zug selbst der Fehler.}",
  "h2 {:Empfohlene Literatur}",
  "h3 {:Für Weiß}",
  "book 1391 {:Der Hebel f4-f5 gegen e6 war achtmal der beste Zug und kam nie, dazu wurde mit Raumvorteil abgetauscht – Bauernhebel und ihr richtiger Zeitpunkt sind das Thema dieses Buchs.}",
  "book 734 {:Dreimal hintereinander blieb das Schach auf h7 ungeprüft; Kotovs Methode, zuerst alle Kandidatenzüge zu sammeln, setzt genau dort an.}",
  "book 1536 {:Der Entwicklungsvorsprung zwischen dem 13. und 22. Zug wurde mit Vorbereitungszügen verbraucht; das Buch behandelt, wie man einen vorübergehenden Vorteil in Angriff umsetzt, solange er da ist.}",
  "h3 {:Für Schwarz}",
  "book 1417 {:Gegen f4-f5 und gegen das Opfer auf h7 half jedes Mal derselbe Zug g7-g6, der Zug um Zug ausblieb – die Frage nach der Absicht des Gegners wird hier systematisch geübt.}",
  "book 1057 {:Der König blieb bis zum 22. Zug in der Mitte, während am Damenflügel manövriert wurde (Tb8, Sb8–c6); Entwicklung und rechtzeitige Rochade sind hier das Thema.}",
  "book 769 {:Drei Züge in Folge (}[:22...0-0@44/44#e5941c20]{:, }[:23...Tc7@46/46#c1e31248]{:, }[:24...Lb7@48/48#884e073f]{:) ließen dieselbe Kombination zu; praktische Fehlerkontrolle vor dem eigenen Zug ist der Kern dieses Buchs.}",
];
