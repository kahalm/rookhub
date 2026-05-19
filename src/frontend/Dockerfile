FROM node:24-alpine AS build
ARG BUILD_ENV=prod
WORKDIR /app
COPY app/package*.json ./
RUN npm ci
COPY app/ .
RUN if [ "$BUILD_ENV" != "dev" ]; then \
      sed -i 's/production: false/production: true/' src/environments/environment.ts; \
    fi
RUN npx ng build --configuration=production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/app/browser /usr/share/nginx/html
EXPOSE 80
