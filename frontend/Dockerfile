FROM node:24-alpine AS build
WORKDIR /app
RUN corepack enable
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/ .
RUN pnpm run build

FROM nginx:1.29-alpine
ENV API_UPSTREAM=http://api:8080
ENV NGINX_ENVSUBST_FILTER=API_UPSTREAM
COPY frontend/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/usage-billing-web/browser /usr/share/nginx/html
EXPOSE 8080