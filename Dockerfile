# ==============================================================================
# STAGE 1: Next.js (React) Frontend Statischer Export
# ==============================================================================
FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend

# Abhängigkeiten installieren
COPY frontend/package*.json ./
RUN npm ci || npm install

# Frontend-Code kopieren & statisches Produktions-Bundle nach /src/backend/wwwroot exportieren
COPY frontend/ ./
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ==============================================================================
# STAGE 2: C# ASP.NET Core (.NET 10) Backend Build & Publish
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src/backend

# Projektdatei wiederherstellen
COPY backend/HardwareCheck.Api.csproj ./
RUN dotnet restore "HardwareCheck.Api.csproj"

# Backend-Quellcode und gebaute Frontend-Dateien (wwwroot) kopieren
COPY backend/ ./
COPY --from=frontend-build /src/backend/wwwroot ./wwwroot

# Release-Build veröffentlichen
RUN dotnet publish "HardwareCheck.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# STAGE 3: Produktions-Laufzeitumgebung (Port 8085 & 5085, Zeitzone Europe/Berlin)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Deutsche Zeitzone (MEZ/MESZ) für Aufzeichnung & Absturz-Protokollierung konfigurieren
ENV TZ=Europe/Berlin
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Veröffentlichte Backend-Binärdateien und statisches Next.js-Frontend kopieren
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /src/backend/wwwroot ./wwwroot

# Daten-Verzeichnis für gespeicherte Geräte (endpoints.json), Aufzeichnung & Absturz-Protokoll (.log)
RUN mkdir -p /app/data

# Port 8085 (Libre Hardware Monitor Web & API) sowie Port 5085 freigeben
EXPOSE 8085
EXPOSE 5085

ENTRYPOINT ["dotnet", "HardwareCheck.Api.dll"]
