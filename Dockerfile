# syntax=docker/dockerfile:1.6
# ----------------------------------------------------------------------------
# Build stage
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

COPY src/WordToPdfService/WordToPdfService.csproj src/WordToPdfService/
RUN dotnet restore src/WordToPdfService/WordToPdfService.csproj

COPY src/ src/
RUN dotnet publish src/WordToPdfService/WordToPdfService.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ----------------------------------------------------------------------------
# Runtime stage: ASP.NET 8 + headless LibreOffice
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS runtime

ENV DEBIAN_FRONTEND=noninteractive \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HOME=/tmp

# Install LibreOffice (core + writer is enough for .docx -> PDF) plus
# fonts that cover Latin/Cyrillic/CJK so most documents render correctly.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libreoffice-core \
        libreoffice-writer \
        libreoffice-common \
        fonts-dejavu \
        fonts-liberation \
        fonts-liberation2 \
        fonts-noto-core \
        fonts-noto-cjk \
        fontconfig \
        ca-certificates \
        curl \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

# Run as non-root for safety
RUN useradd -m -u 10001 appuser
WORKDIR /app
COPY --from=build /app/publish ./

# Pre-warm LibreOffice user profile to speed up first request
RUN mkdir -p /tmp/lo-warmup \
    && soffice -env:UserInstallation=file:///tmp/lo-warmup \
        --headless --norestore --nologo --nofirststartwizard --terminate_after_init \
        || true \
    && chown -R appuser:appuser /app /tmp

USER appuser
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "WordToPdfService.dll"]
