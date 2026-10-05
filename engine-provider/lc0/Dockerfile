# Engine-Provider mit Leela Chess Zero (Lc0) auf einer NVIDIA-GPU — gedacht für den DGX Spark
# (GB10, Grace-ARM + Blackwell, CUDA-Rechenfähigkeit 12.1). Gleicher Provider, gleicher Broker,
# gleiches entrypoint.sh wie der Stockfish-Container eine Ebene höher; nur die Engine ist eine andere.
# Bau-Kontext ist engine-provider/ (nicht dieser Ordner) — siehe compose.yml hier.
#
# Zwei Stufen: die erste baut Lc0 aus den Quellen (Releases gibt es für Linux/ARM nicht als
# Binärdatei), die zweite enthält nur das Ergebnis plus die CUDA-Laufzeit. Gebaut wird auf der
# Spark selbst (`docker compose build`): dann passt die Architektur ohne Emulation.
ARG CUDA_VERSION=13.0.1
ARG UBUNTU=ubuntu24.04

FROM nvidia/cuda:${CUDA_VERSION}-devel-${UBUNTU} AS build
RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates g++ meson ninja-build pkg-config \
      zlib1g-dev libopenblas-dev \
 && rm -rf /var/lib/apt/lists/*

# v0.32.1 ist die letzte stabile Fassung (2025-11); v0.33.0-rc0 (2026-09) geht per Bau-Argument.
ARG LC0_VERSION=v0.32.1
# Rechenfähigkeit der Ziel-GPU. GB10 (DGX Spark) = 12.1 → 121. Andere Karten: RTX PRO 6000 / RTX 50xx
# = 120, H100 = 90, RTX 40xx = 89, RTX 30xx = 86. `nvidia-smi --query-gpu=compute_cap --format=csv`.
ARG CUDA_CC=121
RUN git clone --depth 1 --branch "${LC0_VERSION}" --recurse-submodules \
      https://github.com/LeelaChessZero/lc0.git /src/lc0
WORKDIR /src/lc0
# native_cuda/native_arch AUS: beim Bauen ist keine GPU im Container, „native" wüsste nicht, wofür.
# Dazu das BLAS-Backend (CPU): damit lässt sich der Container auch ohne GPU auf Lebenszeichen prüfen
# (LC0_BACKEND=blas) — langsam, aber derselbe Weg durch Provider, Zwischenschalter und Lc0.
RUN meson setup build --buildtype=release \
      -Dplain_cuda=true -Dcudnn=false -Dnative_cuda=false -Dcc_cuda="${CUDA_CC}" \
      -Dnative_arch=false -Dblas=true -Dopenblas=true -Ddnnl=false -Dopencl=false \
      -Dgtest=false \
 && meson compile -C build lc0 \
 && strip build/lc0 \
 && ./build/lc0 --help >/dev/null

FROM nvidia/cuda:${CUDA_VERSION}-runtime-${UBUNTU}
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl python3 python3-venv libopenblas0 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /src/lc0/build/lc0 /opt/lc0/lc0
COPY --from=build /src/lc0/COPYING /opt/lc0/COPYING

# Das Netz gehört zur Engine wie das NNUE zu Stockfish. Vorgabe: BT4, das stärkste veröffentlichte
# Netz (Transformer, 1024 Breite, 15 Schichten; 383 MB) — für eine GPU gemacht, auf der CPU unbrauchbar.
# Kleiner und schneller: t1-256x10-distilled (37 MB, siehe README-Tabelle). Leerer LC0_NET_URL =
# kein Netz ins Abbild, dann per Volume einhängen und LC0_WEIGHTS setzen.
ARG LC0_NET_URL=https://storage.lczero.org/files/networks-contrib/BT4-1024x15x32h-swa-6147500-policytune-332.pb.gz
ARG LC0_NET_SHA256=e6ada9d6c4a769bfab3aa0848d82caeb809aa45f83e6c605fc58a31d21bdd618
RUN set -eux; mkdir -p /opt/lc0/net; \
    if [ -n "${LC0_NET_URL}" ]; then \
      curl -fsSL -o "/opt/lc0/net/${LC0_NET_URL##*/}" "${LC0_NET_URL}"; \
      if [ -n "${LC0_NET_SHA256}" ]; then echo "${LC0_NET_SHA256}  /opt/lc0/net/${LC0_NET_URL##*/}" | sha256sum -c -; \
      else echo "WARNUNG: LC0_NET_SHA256 leer — Netz wird NICHT geprüft." >&2; fi; \
      ln -s "${LC0_NET_URL##*/}" /opt/lc0/net/default.pb.gz; \
    fi

# Provider: derselbe gepinnte, per Prüfsumme verifizierte Stand wie im Stockfish-Abbild
# (../Dockerfile), dieselbe Korrektur, dasselbe entrypoint.sh und preflight.py.
WORKDIR /opt
COPY requirements.txt /opt/requirements.txt
RUN python3 -m venv /opt/venv && /opt/venv/bin/pip install --no-cache-dir -r /opt/requirements.txt
ENV PATH=/opt/venv/bin:$PATH
ARG PROVIDER_SHA=d0eeb24229bae3cf5eb1e6696c0487ea05ef09ad
ARG PROVIDER_SHA256=aea0bb0258c7afbb4177eba91d1beaed5a9d102d1d46122aae7f6f00433c44a1
RUN curl -fsSL "https://raw.githubusercontent.com/lichess-org/external-engine/${PROVIDER_SHA}/example-provider.py" \
      -o /opt/provider.py \
 && echo "${PROVIDER_SHA256}  /opt/provider.py" | sha256sum -c -
COPY patch_force_close.py /opt/
RUN python3 /opt/patch_force_close.py /opt/provider.py
COPY entrypoint.sh preflight.py /opt/
COPY lc0/lc0-uci.py /opt/lc0/lc0-uci
RUN chmod +x /opt/entrypoint.sh /opt/lc0/lc0-uci

# Der Provider startet ENGINE_PATH — hier der Zwischenschalter, der Lc0 startet.
ENV ENGINE_PATH=/opt/lc0/lc0-uci \
    LC0_WEIGHTS=/opt/lc0/net/default.pb.gz \
    LC0_BACKEND=cuda-fp16

RUN useradd --create-home --uid 10001 provider
USER provider
ENTRYPOINT ["/opt/entrypoint.sh"]
