# syntax=docker/dockerfile:1
# Multi-stage build: Angular SPA -> .NET publish -> slim runtime.
# One image is the whole monolith (D4). Deployed on DO App Platform.

# ---- Stage 1: build the Angular SPA ----------------------------------------
FROM node:22-alpine AS client
WORKDIR /client
COPY src/ClientApp/package*.json ./
RUN npm ci
COPY src/ClientApp/ ./
RUN npm run build -- --configuration production
# Angular outputs to dist/ClientApp/browser; that dir becomes the API's wwwroot below.

# ---- Stage 2: build & publish the .NET API ---------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj
COPY src/Api/ src/Api/
# Bring the built SPA into wwwroot so it ships inside the same image.
COPY --from=client /client/dist/ClientApp/browser/ /src/src/Api/wwwroot/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app /p:UseAppHost=false

# ---- Stage 3: runtime ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
