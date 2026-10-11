# Multi-stage Dockerfile for Angular 22 Frontend SPA
FROM node:22-alpine AS build
WORKDIR /app

# Cache package dependencies
COPY frontend/package*.json ./
RUN npm ci

# Copy source code and build production distribution
COPY frontend/ ./
RUN npm run build -- --configuration production

# Production serving stage using lightweight Nginx
FROM nginx:alpine AS runtime

# Copy custom Nginx configuration for Angular HTML5 client-side routing
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf

# Copy compiled SPA assets
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
