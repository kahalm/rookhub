# Engine-Provider für die RookHub-Analyse: verbindet eine lokale UCI-Engine (Standard:
# Stockfish) mit dem Lichess-External-Engine-Broker. Der Container öffnet KEINEN Port —
# er baut die Verbindung von sich aus nach außen auf (Long-Poll). Siehe README.md.
FROM python:3.13-slim

# ca-certificates wird zur LAUFZEIT gebraucht (HTTPS zu lichess.org), curl beim Bauen.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

# Stockfish 18 als OFFIZIELLE Binärdatei statt aus Debian. Grund ist nicht nur die Version:
# das Debian-Paket ist ein generischer Build ohne AVX2-Nutzung. Auf diesem Server gemessen
# (bench 128 8 16): Debian-Stockfish 17.1 = 4,98 Mio Knoten/s, offizielles 18-avx2 = 8,39 Mio
# — also rund zwei Drittel mehr Rechenleistung bei gleicher CPU.
#
# Variante `avx2` als Vorgabe (CPUs ab ~2013: Intel Haswell, AMD Excavator/Zen). Ebenfalls
# gemessen: `bmi2` liegt auf Zen 3 nur 0,03 % darüber — der schmalere Kompatibilitätsbereich
# lohnt dafür nicht. Andere Variante (avx512, sse41-popcnt für alte CPUs, …)? SF_VARIANT UND
# SF_SHA256 zusammen setzen, sonst schlägt die Prüfsummen-Kontrolle fehl; SF_SHA256="" schaltet
# sie ab (dann bekommt die Engine ungeprüft die Rechenzeit dieser Maschine).
ARG SF_VERSION=sf_18
ARG SF_VARIANT=x86-64-avx2
ARG SF_SHA256=536c0c2c0cf06450df0bfb5e876ef0d3119950703a8f143627f990c7b5417964
RUN set -eux; \
    curl -fsSL -o /tmp/sf.tar \
      "https://github.com/official-stockfish/Stockfish/releases/download/${SF_VERSION}/stockfish-ubuntu-${SF_VARIANT}.tar"; \
    if [ -n "${SF_SHA256}" ]; then \
      echo "${SF_SHA256}  /tmp/sf.tar" | sha256sum -c -; \
    else \
      echo "WARNUNG: SF_SHA256 leer — Stockfish-Download wird NICHT geprueft." >&2; \
    fi; \
    tar xf /tmp/sf.tar -C /tmp; \
    mkdir -p /opt/stockfish; \
    mv "/tmp/stockfish/stockfish-ubuntu-${SF_VARIANT}" /opt/stockfish/stockfish; \
    chmod +x /opt/stockfish/stockfish; \
    cp /tmp/stockfish/Copying.txt /tmp/stockfish/AUTHORS /opt/stockfish/; \
    rm -rf /tmp/sf.tar /tmp/stockfish; \
    /opt/stockfish/stockfish --help >/dev/null 2>&1 || true
# Alles in EINEM RUN: das 109-MB-Archiv soll keine eigene Image-Schicht hinterlassen.
# Copying.txt + AUTHORS bleiben liegen — Stockfish ist GPL-3.0, der Lizenztext gehört dazu.

RUN pip install --no-cache-dir "requests==2.32.5"

# Der OFFIZIELLE Lichess-Provider (lichess-org/external-engine, GPL-3.0 wie RookHub selbst).
# Bewusst zur Bauzeit geholt statt ins Repo kopiert: so ist die Herkunft eindeutig und ein
# Update ist ein Zeilenwechsel. Auf einen Commit GEPINNT (Tags/Branches könnten sich unter
# uns ändern) und per Prüfsumme verifiziert — ein manipuliertes Skript bekäme sonst den
# Lichess-Token zu sehen.
ARG PROVIDER_SHA=a6ef15a8e395eb609535857aabf18837ea7696cf
ARG PROVIDER_SHA256=2396a90368b2fa8ea7b33796788af7bc99047e13e9e4bb3dfb49ce213c04ac0b
RUN curl -fsSL "https://raw.githubusercontent.com/lichess-org/external-engine/${PROVIDER_SHA}/example-provider.py" \
      -o /opt/provider.py \
 && echo "${PROVIDER_SHA256}  /opt/provider.py" | sha256sum -c -

COPY entrypoint.sh preflight.py /opt/
RUN chmod +x /opt/entrypoint.sh

# Unprivilegiert laufen: der Provider braucht keine Rechte außer ausgehendem Netz.
RUN useradd --create-home --uid 10001 provider
USER provider

ENTRYPOINT ["/opt/entrypoint.sh"]
