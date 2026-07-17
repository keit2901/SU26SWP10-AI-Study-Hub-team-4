# ============================================================================
# AI Study Hub v2 — Dockerfile
# .NET 8 Blazor Interactive Server + MudBlazor
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["AI_Study_Hub_v2/AI_Study_Hub_v2.csproj", "AI_Study_Hub_v2/"]
RUN dotnet restore "AI_Study_Hub_v2/AI_Study_Hub_v2.csproj"

COPY . .
WORKDIR /src/AI_Study_Hub_v2
RUN dotnet publish "AI_Study_Hub_v2.csproj" -c Release -o /app/publish --no-restore --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN addgroup --system --gid 1000 app && adduser --system --uid 1000 --gid 1000 app

COPY --from=build /app/publish .
USER app
EXPOSE 8080

ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "AI_Study_Hub_v2.dll"]
