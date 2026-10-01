FROM node:24-alpine AS build
WORKDIR /app
COPY app/package*.json ./
RUN npm ci
# Welches Projekt des Workspaces gebaut wird: `app` = RookHub, `turnier` = Turnierseite, `kidhub` = Kinderseite,
# `leaguehub` = Aufstellungs-Prognosen, `clubhub` = Kartei der Kinder und Jugendlichen.
# Alle teilen sich node_modules und den Quellbaum (Alias `@rh/*`), deshalb EIN Dockerfile.
# Steht VOR dem Modell-Schritt: dort entscheidet das Argument — ohne ARG davor waere $APP_PROJECT im RUN leer,
# die Bedingung nie wahr, und das RookHub-Image kaeme still ohne Modell aus dem Build.
ARG APP_PROJECT=app
# Maia-3-Modell fuer das Sparring im Analysebrett (45 MB, nicht im Repo): fetch.sh holt es per gepinntem Commit und
# bricht bei falscher Pruefsumme ab. Nur RookHub braucht es — die vier anderen Images holen nichts. Bewusst VOR
# `COPY app/ .`: so kommen die 45 MB bei einer Quelltext-Aenderung aus dem Layer-Cache, neu geladen wird nur, wenn
# sich fetch.sh (also der Pin) oder die Abhaengigkeiten darueber aendern. Eine lokal geholte Datei bleibt per
# .dockerignore draussen.
COPY app/maia-model/fetch.sh maia-model/fetch.sh
RUN if [ "$APP_PROJECT" = "app" ]; then sh maia-model/fetch.sh; fi
COPY app/ .
RUN npx ng build ${APP_PROJECT} --configuration=production

FROM nginx:alpine
# Commit-SHA des Builds (vom CI als --build-arg gesetzt) → als /build-info.json ausgeliefert,
# damit die Admin-CI-Seite den GitHub-Actions-Run des LAUFENDEN Builds markieren kann.
ARG GIT_SHA=unknown
# Ref des Builds (Branch für :dev, Tag-Name für :latest) — die Admin-CI-Seite markiert damit
# genau EINEN Run: bei :dev den master-Push-Run, bei :prod den Tag-Run (gleiche SHA, zwei Runs).
ARG GIT_REF=
ARG APP_PROJECT=app
# Dieselbe nginx-Konfiguration fuer beide Seiten: gleicher /api-Proxy, gleicher Kachel-Cache,
# gleiche Header. Unterschiedlich ist nur, WELCHES Bundle im html-Verzeichnis landet.
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/${APP_PROJECT}/browser /usr/share/nginx/html
RUN printf '{"sha":"%s","ref":"%s"}' "$GIT_SHA" "$GIT_REF" > /usr/share/nginx/html/build-info.json && \
    sed -i 's|/run/nginx.pid|/tmp/nginx.pid|' /etc/nginx/nginx.conf && \
    chown -R nginx:nginx /var/cache/nginx /var/log/nginx /usr/share/nginx/html /etc/nginx/conf.d
EXPOSE 8080
USER nginx
