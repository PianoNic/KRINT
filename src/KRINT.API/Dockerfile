# syntax=docker/dockerfile:1.7

# ---------- Stage 1: frontend ----------
FROM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend
COPY src/KRINT.Frontend/package.json src/KRINT.Frontend/bun.lock ./
RUN bun install
COPY src/KRINT.Frontend .
RUN bun run build:agent --output-path /artifacts/frontend

# ---------- Stage 2: backend restore ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS backend-deps
WORKDIR /src/backend
COPY ["src/KRINT.API/KRINT.API.csproj",                       "KRINT.API/"]
COPY ["src/KRINT.Application/KRINT.Application.csproj",       "KRINT.Application/"]
COPY ["src/KRINT.Infrastructure/KRINT.Infrastructure.csproj", "KRINT.Infrastructure/"]
COPY ["src/KRINT.Infrastructure.Migrations.Sqlite/KRINT.Infrastructure.Migrations.Sqlite.csproj", "KRINT.Infrastructure.Migrations.Sqlite/"]
COPY ["src/KRINT.Domain/KRINT.Domain.csproj",                 "KRINT.Domain/"]
COPY ["src/Directory.Build.props",                            "./"]
RUN dotnet restore "KRINT.API/KRINT.API.csproj"

# ---------- Stage 3: backend publish ----------
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY src .
RUN dotnet publish "KRINT.API/KRINT.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------- Stage 4: runtime (distroless Azure Linux) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-azurelinux3.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production

# Run as root so /var/run/docker.sock is accessible out of the box on every host
# (Docker Desktop on Windows/Mac exposes the socket as root:root 0660; Linux exposes
# it as root:docker 0660 with a host-specific GID). KRINT needs full Docker API access
# to provision sibling containers - dropping privilege inside the container while
# holding the socket gains nothing, since socket access is root-equivalent on the host
# either way. Override with `user: "<uid>:<gid>"` in compose if you have a different
# threat model and a known GID to map.
USER root

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /artifacts/frontend/browser ./wwwroot

ENTRYPOINT ["dotnet", "KRINT.API.dll"]