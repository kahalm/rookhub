# Backt init-kibana.sh in ein kleines Image, damit das Dashboard beim normalen
# `docker compose up -d --build` automatisch mitwandert (Script aendert sich ->
# Image-Layer aendert sich -> Container wird neu erstellt -> Import laeuft erneut,
# idempotent via _import?overwrite=true). Kein git pull / manuelles Recreate noetig.
# Build-Context = Repo-Root (siehe compose: build.context: .).
FROM curlimages/curl:latest
COPY --chmod=0755 init-kibana.sh /init-kibana.sh
# Importiert beim Start (idempotent), bleibt danach idle "running" (fuer Monitoring/Arcane).
ENTRYPOINT ["/bin/sh", "-c", "/init-kibana.sh; echo 'init done, idling'; exec tail -f /dev/null"]
