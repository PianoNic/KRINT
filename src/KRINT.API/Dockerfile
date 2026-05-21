# Multi-stage build:
#   - frontend-build: Angular dist via bun
#   - backend-deps:   dotnet restore (cached on csproj changes only)
#   - backend-build:  dotnet publish
#   - final:          aspnet runtime + published app + frontend dist as wwwroot
#
# Build from the REPO ROOT so the build context can see src/KRINT.Frontend and the other
# projects:
#   docker build -t krint -f src/KRINT.API/Dockerfile .

# ---------- Stage 1: frontend ----------
FROM oven/bun:1.3-debian AS frontend-build
WORKDIR /src/frontend
# Cache the lock first
COPY src/KRINT.Frontend/package.json src/KRINT.Frontend/bun.lock* ./
RUN bun install --frozen-lockfile
# Copy the rest of the frontend tree and build a production bundle.
COPY src/KRINT.Frontend .
RUN bunx ng build --configuration production --output-path /artifacts/frontend

# ---------- Stage 2: backend restore ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-deps
WORKDIR /src
COPY ["src/KRINT.API/KRINT.API.csproj",                    "src/KRINT.API/"]
COPY ["src/KRINT.Application/KRINT.Application.csproj",    "src/KRINT.Application/"]
COPY ["src/KRINT.Infrastructure/KRINT.Infrastructure.csproj","src/KRINT.Infrastructure/"]
COPY ["src/KRINT.Domain/KRINT.Domain.csproj",              "src/KRINT.Domain/"]
COPY ["src/Directory.Build.props",                         "src/"]
RUN dotnet restore "src/KRINT.API/KRINT.API.csproj"

# ---------- Stage 3: backend publish ----------
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY src ./src
RUN dotnet publish "src/KRINT.API/KRINT.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ---------- Stage 4: runtime (distroless Azure Linux) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-azurelinux3.0-distroless AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /artifacts/frontend ./wwwroot
# Default app config — overridable at runtime by setting KRINT_CONFIG to a mounted file.
COPY krint.yaml ./krint.yaml

ENTRYPOINT ["dotnet", "KRINT.API.dll"]
