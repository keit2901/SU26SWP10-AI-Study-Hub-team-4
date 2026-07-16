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

# Railway injects $PORT at runtime. ASPNETCORE_URLS binds Kestrel.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
# Override to "Development" via Railway env to skip reCAPTCHA enforcement
ENV ASPNETCORE_ENVIRONMENT=Production
# Forwarded headers so UseHttpsRedirection works behind Railway TLS proxy
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "AI_Study_Hub_v2.dll"]
