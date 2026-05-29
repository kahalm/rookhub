FROM node:24-alpine AS build
WORKDIR /app
COPY app/package*.json ./
RUN npm ci
COPY app/ .
RUN npx ng build --configuration=production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/app/browser /usr/share/nginx/html
RUN sed -i 's|/run/nginx.pid|/tmp/nginx.pid|' /etc/nginx/nginx.conf && \
    chown -R nginx:nginx /var/cache/nginx /var/log/nginx /usr/share/nginx/html /etc/nginx/conf.d
EXPOSE 8080
USER nginx
