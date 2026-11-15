#!/usr/bin/env python3
"""Erzeugt die Kartengeometrie der Seite „Besucher-Herkunft“ (/visitors) als statisches Asset.

  python3 tools/visitor-map/build_map.py <countries-50m.json> [<ziel.json>]

Eingabe: world-atlas `countries-50m.json` (TopoJSON aus Natural Earth, gemeinfrei) —
  https://cdn.jsdelivr.net/npm/world-atlas@2/countries-50m.json
Ausgabe (Vorgabe): src/frontend/app/public/assets/geo/world-countries.json

  { "w": 960, "h": 400,
    "views": { "world": [x, y, w, h], "eu": […], "na": […], "sa": […], "af": […], "as": […], "oc": […] },
    "countries": [ { "id": "AT", "c": "eu", "d": "M…Z" }, … ] }

Projektion: gleichabständig (Länge/Breite linear), 960 × 400, Breite 83° N … 58° S (Antarktis entfällt).
Vereinfachung: Douglas-Peucker je Ring, sehr kleine Inseln fallen weg (der größte Ring eines Landes bleibt immer);
Pfade mit relativen Koordinaten, eine Nachkommastelle. Ringe, die den 180. Längengrad kreuzen (Russland, Fidschi),
werden dort aufgetrennt.

Kontinent je Land: feste Tabelle unten (ISO 3166-1 alpha-2). Länder auf zwei Kontinenten sind EINEM zugeordnet:
Russland → Europa, Türkei/Kasachstan/Georgien/Armenien/Aserbaidschan → Asien, Zypern → Europa, Ägypten → Afrika,
Mittelamerika und Karibik → Nordamerika. Die Tabelle steht mit im Asset (`c`), die Seite rechnet die
Kontinent-Ansicht daraus.
"""
import json, math, os, sys

W, H = 960, 400
LAT_TOP, LAT_BOTTOM = 83.0, -58.0
TOLERANCE = 0.16      # Douglas-Peucker in Karteneinheiten (≈ 0,06°) — Europa bleibt im Zoom lesbar
MIN_RING_AREA = 0.35  # Karteneinheiten²; kleinere Nebenringe (Inselchen) fallen weg

CONTINENTS = {
    'eu': 'AD AL AT AX BA BE BG BY CH CY CZ DE DK EE ES FI FO FR GB GG GI GR HR HU IE IM IS IT JE LI LT LU LV MC MD ME MK '
          'MT NL NO PL PT RO RS RU SE SI SJ SK SM UA VA XK',
    'na': 'AG AI AW BB BL BM BQ BS BZ CA CR CU CW DM DO GD GL GP GT HN HT JM KN KY LC MF MQ MS MX NI PA PM PR SV SX TC TT '
          'US VC VG VI',
    'sa': 'AR BO BR CL CO EC FK GF GY PE PY SR UY VE',
    'af': 'AO BF BI BJ BW CD CF CG CI CM CV DJ DZ EG EH ER ET GA GH GM GN GQ GW KE KM LR LS LY MA MG ML MR MU MW MZ NA NE '
          'NG RE RW SC SD SH SL SN SO SS ST SZ TD TG TN TZ UG YT ZA ZM ZW',
    'as': 'AE AF AM AZ BD BH BN BT CN GE HK ID IL IN IQ IR JO JP KG KH KP KR KW KZ LA LB LK MM MN MO MV MY NP OM PH PK PS '
          'QA SA SG SY TH TJ TL TM TR TW UZ VN YE IO',
    'oc': 'AS AU CK FJ FM GU KI MH MP NC NF NR NU NZ PF PG PN PW SB TK TO TV VU WF WS',
}
# Kartenausschnitt je Kontinent als (West, Süd, Ost, Nord) in Grad.
BOXES = {
    'eu': (-25, 34, 50, 72),
    'na': (-170, 6, -50, 83),
    'sa': (-84, -57, -32, 14),
    'af': (-20, -36, 53, 39),
    'as': (25, -12, 150, 56),
    'oc': (110, -49, 180, 2),
}
# ISO 3166-1 numerisch → alpha-2 (world-atlas trägt die numerische Kennung).
NUM2ISO = dict(p.split(':') for p in (
    '004:AF 008:AL 010:AQ 012:DZ 016:AS 020:AD 024:AO 028:AG 031:AZ 032:AR 036:AU 040:AT 044:BS 048:BH 050:BD 051:AM '
    '052:BB 056:BE 060:BM 064:BT 068:BO 070:BA 072:BW 076:BR 084:BZ 086:IO 090:SB 092:VG 096:BN 100:BG 104:MM 108:BI '
    '112:BY 116:KH 120:CM 124:CA 132:CV 136:KY 140:CF 144:LK 148:TD 152:CL 156:CN 158:TW 170:CO 174:KM 175:YT 178:CG '
    '180:CD 184:CK 188:CR 191:HR 192:CU 196:CY 203:CZ 204:BJ 208:DK 212:DM 214:DO 218:EC 222:SV 226:GQ 231:ET 232:ER '
    '233:EE 234:FO 238:FK 239:GS 242:FJ 246:FI 248:AX 250:FR 254:GF 258:PF 260:TF 262:DJ 266:GA 268:GE 270:GM 275:PS '
    '276:DE 288:GH 292:GI 296:KI 300:GR 304:GL 308:GD 312:GP 316:GU 320:GT 324:GN 328:GY 332:HT 334:HM 336:VA 340:HN '
    '344:HK 348:HU 352:IS 356:IN 360:ID 364:IR 368:IQ 372:IE 376:IL 380:IT 384:CI 388:JM 392:JP 398:KZ 400:JO 404:KE '
    '408:KP 410:KR 414:KW 417:KG 418:LA 422:LB 426:LS 428:LV 430:LR 434:LY 438:LI 440:LT 442:LU 446:MO 450:MG 454:MW '
    '458:MY 462:MV 466:ML 470:MT 474:MQ 478:MR 480:MU 484:MX 492:MC 496:MN 498:MD 499:ME 500:MS 504:MA 508:MZ 512:OM '
    '516:NA 520:NR 524:NP 528:NL 531:CW 533:AW 534:SX 535:BQ 540:NC 548:VU 554:NZ 558:NI 562:NE 566:NG 570:NU 574:NF '
    '578:NO 580:MP 583:FM 584:MH 585:PW 586:PK 591:PA 598:PG 600:PY 604:PE 608:PH 612:PN 616:PL 620:PT 624:GW 626:TL '
    '630:PR 634:QA 638:RE 642:RO 643:RU 646:RW 652:BL 654:SH 659:KN 660:AI 662:LC 663:MF 666:PM 670:VC 674:SM 678:ST '
    '682:SA 686:SN 688:RS 690:SC 694:SL 702:SG 703:SK 704:VN 705:SI 706:SO 710:ZA 716:ZW 724:ES 728:SS 729:SD 732:EH '
    '740:SR 744:SJ 748:SZ 752:SE 756:CH 760:SY 762:TJ 764:TH 768:TG 772:TK 776:TO 780:TT 784:AE 788:TN 792:TR 795:TM '
    '796:TC 798:TV 800:UG 804:UA 807:MK 818:EG 826:GB 831:GG 832:JE 833:IM 834:TZ 840:US 850:VI 854:BF 858:UY 860:UZ '
    '862:VE 876:WF 882:WS 887:YE 894:ZM').split())
# world-atlas-Einträge ohne numerische Kennung, nach Namen.
NAME2ISO = {'Kosovo': 'XK'}
# Namenlose Gebiete, die im Land ringsum aufgehen (sonst bliebe ein Loch in der Karte).
MERGE_INTO = {'Somaliland': 'SO', 'N. Cyprus': 'CY'}

ISO2CONT = {iso: cont for cont, isos in CONTINENTS.items() for iso in isos.split()}


def project(lon, lat):
    lat = max(min(lat, LAT_TOP), LAT_BOTTOM)
    return ((lon + 180.0) / 360.0 * W, (LAT_TOP - lat) / (LAT_TOP - LAT_BOTTOM) * H)


def view_box(box):
    west, south, east, north = box
    x0, y0 = project(west, north)
    x1, y1 = project(east, south)
    return [round(x0, 1), round(y0, 1), round(x1 - x0, 1), round(y1 - y0, 1)]


def simplify(points, tol):
    """Douglas-Peucker, iterativ."""
    if len(points) < 3:
        return points
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        a, b = stack.pop()
        ax, ay = points[a]
        bx, by = points[b]
        dx, dy = bx - ax, by - ay
        norm = math.hypot(dx, dy)
        best, idx = 0.0, -1
        for i in range(a + 1, b):
            px, py = points[i]
            dist = abs(dy * (px - ax) - dx * (py - ay)) / norm if norm else math.hypot(px - ax, py - ay)
            if dist > best:
                best, idx = dist, i
        if best > tol and idx > 0:
            keep[idx] = True
            stack.append((a, idx))
            stack.append((idx, b))
    return [p for p, k in zip(points, keep) if k]


def area(points):
    return abs(sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1]))) / 2.0


def fmt(v):
    s = f'{v:.1f}'
    if s.endswith('.0'):
        s = s[:-2]
    return '0' if s in ('-0', '') else s


def ring_path(ring_lonlat):
    """Ein Ring als Pfadstück(e); am 180. Längengrad aufgetrennt."""
    pieces, current, prev_lon = [], [], None
    for lon, lat in ring_lonlat:
        if prev_lon is not None and abs(lon - prev_lon) > 180:
            pieces.append(current)
            current = []
        current.append(project(lon, lat))
        prev_lon = lon
    pieces.append(current)
    out = []
    for piece in pieces:
        pts = simplify(piece, TOLERANCE)
        # gerundet aufeinanderfallende Punkte entfernen
        rounded = []
        for x, y in pts:
            p = (round(x, 1), round(y, 1))
            if not rounded or rounded[-1] != p:
                rounded.append(p)
        if len(rounded) > 1 and rounded[0] == rounded[-1]:
            rounded.pop()
        if len(rounded) < 3:
            continue
        out.append(rounded)
    return out


def path_d(rings):
    d = []
    for pts in rings:
        x, y = pts[0]
        seg = [f'M{fmt(x)},{fmt(y)}l']
        rel = []
        for nx, ny in pts[1:]:
            rel.append(f'{fmt(nx - x)},{fmt(ny - y)}')
            x, y = nx, ny
        d.append(seg[0] + ' '.join(rel) + 'z')
    return ''.join(d)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    here = os.path.dirname(os.path.abspath(__file__))
    target = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        here, '..', '..', 'src', 'frontend', 'app', 'public', 'assets', 'geo', 'world-countries.json')
    topo = json.load(open(sys.argv[1], encoding='utf-8'))
    sx, sy = topo['transform']['scale']
    tx, ty = topo['transform']['translate']
    arcs = []
    for arc in topo['arcs']:
        x = y = 0
        pts = []
        for dx, dy in arc:
            x += dx
            y += dy
            pts.append((x * sx + tx, y * sy + ty))
        arcs.append(pts)

    def ring(indices):
        pts = []
        for i in indices:
            a = arcs[i] if i >= 0 else arcs[~i][::-1]
            pts += a[1:] if pts else a
        return pts

    by_iso = {}
    skipped = []
    for g in topo['objects']['countries']['geometries']:
        name = g.get('properties', {}).get('name', '')
        iso = NUM2ISO.get(g.get('id') or '') or NAME2ISO.get(name) or MERGE_INTO.get(name)
        if iso is None or iso not in ISO2CONT:
            skipped.append(name or g.get('id'))
            continue
        polys = g['arcs'] if g['type'] == 'MultiPolygon' else [g['arcs']]
        for poly in polys:
            # nur der Außenring: Löcher (Lesotho in Südafrika) deckt das Land darin selbst ab
            by_iso.setdefault(iso, []).extend(ring_path(ring(poly[0])))

    countries = []
    for iso in sorted(by_iso):
        rings = by_iso[iso]
        if not rings:
            continue   # zu klein für die Karte (Monaco, Vatikan …)
        biggest = max(rings, key=area)
        rings = [r for r in rings if r is biggest or area(r) >= MIN_RING_AREA]
        # nur was im Kartenfeld liegt (nicht ganz an den oberen/unteren Rand geklemmt)
        rings = [r for r in rings if any(0 < y < H for _, y in r)]
        if rings:
            countries.append({'id': iso, 'c': ISO2CONT[iso], 'd': path_d(rings)})

    views = {'world': [0, 0, W, H]}
    views.update({k: view_box(b) for k, b in BOXES.items()})
    out = {'w': W, 'h': H, 'views': views, 'countries': countries}
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, separators=(',', ':'))
        f.write('\n')
    size = os.path.getsize(target)
    print(f'{len(countries)} Länder, {size / 1024:.0f} KB → {os.path.normpath(target)}')
    print('ohne Zuordnung ausgelassen:', ', '.join(sorted(str(s) for s in skipped)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
