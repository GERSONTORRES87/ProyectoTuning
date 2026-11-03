# API de SUE (turning.API).
#
# El contexto de build es la RAIZ del repositorio: el API referencia Application,
# Domain e Infrastructure, que viven en carpetas hermanas.
#
#   docker build -f src/turning.API/Dockerfile -t sue-back-end:0.2.0 .
#
# La configuracion (cadena de conexion, clave JWT, claves de IA) NO va en la imagen:
# se inyecta por variables de entorno. Nombres en docs/deploy.md.

# ============================
# Etapa 1: Build
# ============================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restaurar primero solo los .csproj: la capa de paquetes se reutiliza mientras no cambien.
COPY src/turning.Domain/turning.Domain.csproj src/turning.Domain/
COPY src/turning.Application/turning.Application.csproj src/turning.Application/
COPY src/turning.Infrastructure/turning.Infrastructure.csproj src/turning.Infrastructure/
COPY src/turning.API/turning.API.csproj src/turning.API/
RUN dotnet restore src/turning.API/turning.API.csproj

COPY src/ src/
RUN dotnet publish src/turning.API/turning.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ============================
# Etapa 2: Runtime
# ============================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# La imagen alpine no trae ICU y .NET cae en modo invariante, donde Microsoft.Data.SqlClient no
# puede abrir SQL Server (Migrate() fallaria antes de /api/health). Se instala icu y se desactiva.
RUN apk add --no-cache icu-libs icu-data-full tzdata

COPY --from=build /app/publish ./

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "turning.API.dll"]
