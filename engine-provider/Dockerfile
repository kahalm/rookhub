# Engine-Provider für die RookHub-Analyse: verbindet eine lokale UCI-Engine (Standard:
# Stockfish) mit dem Lichess-External-Engine-Broker. Der Container öffnet KEINEN Port —
# er baut die Verbindung von sich aus nach außen auf (Long-Poll). Siehe README.md.
FROM python:3.13-slim

# ca-certificates wird zur LAUFZEIT gebraucht (HTTPS zu lichess.org), curl beim Bauen.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

# Stockfish 19 als OFFIZIELLE Binärdatei statt aus Debian. Grund ist nicht nur die Version:
# das Debian-Paket ist ein generischer Build ohne AVX2/BMI2-Nutzung. Auf diesem Server gemessen
# (bench 128 8 16): Debian-Stockfish 17.1 = 4,98 Mio Knoten/s, offizielles 18-avx2 = 8,39 Mio.
#
# Seit Stockfish 19 (2026-09-05) gibt es KEINE Varianten-Dateien mehr (…-avx2, …-bmi2, …).
# Die Veröffentlichung liefert UNIVERSELLE Binärdateien, die die CPU-Merkmale zur Laufzeit
# erkennen und den passenden Code fahren. Nachgeprüft auf dieser Maschine: `stockfish compiler`
# meldet „Compilation architecture: x86-64-bmi2" — die Auswahl, die hier vorher von Hand
# getroffen wurde, trifft die Binärdatei jetzt selbst. `SF_VARIANT` ist deshalb weg, `SF_ASSET`
# nennt nur noch die Datei der Veröffentlichung (arm64/riscv64 gibt es dort ebenso).
#
# Der Entpack-Schritt bleibt bewusst `tar xf` OHNE `z`: bis Stockfish 18 hiessen die Dateien
# `…-avx2.tar` (unkomprimiert), ab 19 `…-universal.tar.gz`. GNU tar erkennt die Kompression
# selbst — so baut dieselbe Datei BEIDE Fassungen, und genau das braucht der zweite Container,
# der absichtlich auf 18 bleibt (siehe compose.yml).
#
# Warum 19 statt 18: die Veröffentlichung nennt gegen Stockfish 18 bis zu 44 Elo und dreimal so
# viele gewonnene wie verlorene Partienpaare, neue Netzarchitektur SFNNv16. Knoten pro Sekunde
# taugen dafür NICHT als Maßstab — ein größeres Netz rechnet langsamer und spielt trotzdem
# stärker; wer vergleichen will, vergleicht Elo, nicht nps.
#
# ⚠️ EINE VERHALTENSÄNDERUNG, die uns betrifft: Stockfish 19 prüft Stellungen streng und BEENDET
# sich bei einer ungültigen — nachgestellt mit einer königlosen Diagramm-Stellung, wie sie in
# Chessable-Info-Linien vorkommt:
#   info string CRITICAL ERROR: Command `position fen 8/8/8/4p3/8/8/8/8 w - - 0 1` failed.
#   Reason: Unsupported position. Incorrect number of kings.
# Danach ist der Prozess tot. Der Provider stirbt mit ihm und `restart: unless-stopped` zieht den
# Container neu hoch (siehe entrypoint.sh) — die Analyse ist also nicht dauerhaft kaputt, aber
# eine einzige krumme Stellung kostet den ganzen Engine-Pool einen Neustart. Die Stellungen VOR
# dem Senden zu prüfen steht in TODO.md.
ARG SF_VERSION=sf_19
ARG SF_ASSET=stockfish-linux-x86-64-universal.tar.gz
ARG SF_SHA256=9defc0d4e55d49c65a6d042f3e571a39fcea499ade6dbe741b53b8c65e03611f
RUN set -eux; \
    curl -fsSL -o /tmp/sf.tar.gz \
      "https://github.com/official-stockfish/Stockfish/releases/download/${SF_VERSION}/${SF_ASSET}"; \
    if [ -n "${SF_SHA256}" ]; then \
      echo "${SF_SHA256}  /tmp/sf.tar.gz" | sha256sum -c -; \
    else \
      echo "WARNUNG: SF_SHA256 leer — Stockfish-Download wird NICHT geprueft." >&2; \
    fi; \
    tar xf /tmp/sf.tar.gz -C /tmp; \
    mkdir -p /opt/stockfish; \
    binaer="$(find /tmp/stockfish -maxdepth 1 -type f -name 'stockfish-*' ! -name '*.txt' | head -1)"; \
    test -n "${binaer}"; \
    mv "${binaer}" /opt/stockfish/stockfish; \
    chmod +x /opt/stockfish/stockfish; \
    cp /tmp/stockfish/Copying.txt /tmp/stockfish/AUTHORS /opt/stockfish/; \
    rm -rf /tmp/sf.tar.gz /tmp/stockfish; \
    /opt/stockfish/stockfish --help >/dev/null 2>&1 || true; \
    printf 'uci\nquit\n' | /opt/stockfish/stockfish | grep -q '^id name Stockfish'
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

# EIN Eingriff in den geholten Provider: ein Lebenszeichen im Analyse-Stream, sonst schliesst der
# Broker die zwischen zwei tiefen MultiPV-Iterationen minutenlang stumme Verbindung (Begruendung
# im Skript). Absichtlich NACH der Pruefsumme: gepatcht wird nur der verifizierte Originaltext,
# und aendert der sich, bricht der Build ab statt still ungepatcht durchzulaufen.
COPY patch_provider.py /opt/
RUN python3 /opt/patch_provider.py /opt/provider.py

COPY entrypoint.sh preflight.py /opt/
RUN chmod +x /opt/entrypoint.sh

# Unprivilegiert laufen: der Provider braucht keine Rechte außer ausgehendem Netz.
RUN useradd --create-home --uid 10001 provider
USER provider

ENTRYPOINT ["/opt/entrypoint.sh"]
