# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore dependencies (layer cached unless csproj changes)
COPY AI_Study_Hub_v2/AI_Study_Hub_v2.csproj AI_Study_Hub_v2/
RUN dotnet restore "AI_Study_Hub_v2/AI_Study_Hub_v2.csproj"

# Copy source and publish
COPY AI_Study_Hub_v2/ AI_Study_Hub_v2/
WORKDIR /src/AI_Study_Hub_v2
RUN dotnet publish "AI_Study_Hub_v2.csproj" -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# .NET 8 runtime images provide the non-root app user.
USER app

# Railway injects PORT at runtime; use 8080 only for local Docker runs.
ENTRYPOINT ["sh", "-c", "exec dotnet AI_Study_Hub_v2.dll --urls http://0.0.0.0:${PORT:-8080}"]
