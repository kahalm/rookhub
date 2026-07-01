FROM node:24-alpine AS build
WORKDIR /app
COPY app/package*.json ./
RUN npm ci
COPY app/ .
RUN npx ng build --configuration=production

FROM nginx:alpine
# Commit-SHA des Builds (vom CI als --build-arg gesetzt) → als /build-info.json ausgeliefert,
# damit die Admin-CI-Seite den GitHub-Actions-Run des LAUFENDEN Builds markieren kann.
ARG GIT_SHA=unknown
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/app/browser /usr/share/nginx/html
RUN printf '{"sha":"%s"}' "$GIT_SHA" > /usr/share/nginx/html/build-info.json && \
    sed -i 's|/run/nginx.pid|/tmp/nginx.pid|' /etc/nginx/nginx.conf && \
    chown -R nginx:nginx /var/cache/nginx /var/log/nginx /usr/share/nginx/html /etc/nginx/conf.d
EXPOSE 8080
USER nginx
