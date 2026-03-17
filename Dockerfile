# ── Stage 1: Build ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ProffieOS.Workbench/ProffieOS.Workbench.csproj ProffieOS.Workbench/
RUN dotnet restore ProffieOS.Workbench/ProffieOS.Workbench.csproj

COPY ProffieOS.Workbench/ ProffieOS.Workbench/
RUN dotnet publish ProffieOS.Workbench/ProffieOS.Workbench.csproj \
    -c Release \
    -o /publish

# ── Stage 2: Serve ─────────────────────────────────────────────────────────────
FROM nginx:alpine AS serve

# Install envsubst (part of gettext)
RUN apk add --no-cache gettext

COPY --from=build /publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf

# Entrypoint: inject BASE_PATH into index.html, then start nginx
# BASE_PATH defaults to "/" — set it to e.g. "/myapp/" for a sub-path deployment.
ENV BASE_PATH=/
CMD ["/bin/sh", "-c", \
    "sed -i \"s|<base href=\\\"/\\\" />|<base href=\\\"${BASE_PATH}\\\" />|g\" /usr/share/nginx/html/index.html && \
     cp /usr/share/nginx/html/index.html /usr/share/nginx/html/404.html && \
     nginx -g 'daemon off;'"]

EXPOSE 80
