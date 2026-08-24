# Engine-Provider für die RookHub-Analyse: verbindet eine lokale UCI-Engine (Standard:
# Stockfish) mit dem Lichess-External-Engine-Broker. Der Container öffnet KEINEN Port —
# er baut die Verbindung von sich aus nach außen auf (Long-Poll). Siehe README.md.
FROM python:3.13-slim

# Stockfish aus Debian (trixie = Stockfish 17, inkl. NNUE-Netz): ein portabler Build, der
# auch auf älteren CPUs läuft. Wer eine auf die eigene CPU optimierte Binärdatei (oder das
# UCI-Tunnel-Binary eines Cloud-Anbieters) nutzen will, hängt sie ein und setzt ENGINE_PATH.
# ca-certificates wird zur LAUFZEIT gebraucht (HTTPS zu lichess.org), curl nur beim Bauen.
RUN apt-get update \
 && apt-get install -y --no-install-recommends stockfish ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

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
