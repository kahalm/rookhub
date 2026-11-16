#!/usr/bin/env python3
"""Google-Adressbereiche in der Netzliste der Besucher-Herkunft auffrischen.

Die Liste `src/api/RookHub.Api/Assets/visitor-networks.json` hält fest, welche Adressen die Besucher-Herkunft
(`VisitorNetworkClassifier`) NICHT zählt (Google: Linkvorschau, Vorabladen, Crawler) und welche sie als „VPN“
führt. Dieses Skript ersetzt nur `googleNetworks` durch Googles veröffentlichte Liste aller eigenen Bereiche
(https://www.gstatic.com/ipranges/goog.json — enthält auch Google Cloud, das ist gewollt). Alles andere
(AS-Nummern, VPN-Netze) wird von Hand gepflegt. Ohne Netz: `update_google.py <pfad/zu/goog.json>`.

Danach: Tests (`VisitorNetworkClassifierTests` prüft u. a. „keine Überlappung“), Version + Changelog, Commit.
"""
import ipaddress, json, pathlib, sys, urllib.request

ASSET = pathlib.Path(__file__).resolve().parents[2] / 'src/api/RookHub.Api/Assets/visitor-networks.json'
URL = 'https://www.gstatic.com/ipranges/goog.json'

def main():
    if len(sys.argv) > 1:
        goog = json.loads(pathlib.Path(sys.argv[1]).read_text())
    else:
        with urllib.request.urlopen(URL, timeout=30) as r:
            goog = json.load(r)
    nets = [ipaddress.ip_network(p.get('ipv4Prefix') or p.get('ipv6Prefix')) for p in goog['prefixes']]
    # Überlappungen zusammenfassen — der Dienst sucht per Binärsuche und verlangt disjunkte Bereiche.
    merged = list(ipaddress.collapse_addresses(n for n in nets if n.version == 4)) + \
             list(ipaddress.collapse_addresses(n for n in nets if n.version == 6))
    if len(merged) < 20:
        sys.exit(f'nur {len(merged)} Bereiche — das ist nicht die vollständige Google-Liste, nichts geändert')
    data = json.loads(ASSET.read_text())
    data['googleNetworksSource'] = f"{URL} (creationTime {goog.get('creationTime', '?')})"
    data['googleNetworks'] = [str(n) for n in merged]
    ASSET.write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n')
    print(f'{len(merged)} Google-Bereiche nach {ASSET}')

if __name__ == '__main__':
    main()
