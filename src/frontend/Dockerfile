FROM node:24-alpine AS build
WORKDIR /app
COPY app/package*.json ./
RUN npm ci
COPY app/ .
# Welches der beiden Projekte des Workspaces gebaut wird: `app` = RookHub, `turnier` = Turnierseite.
# Beide teilen sich node_modules und den Quellbaum (Alias `@rh/*`), deshalb EIN Dockerfile.
ARG APP_PROJECT=app
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
