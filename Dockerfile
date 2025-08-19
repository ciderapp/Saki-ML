FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Saki-ML.sln", "/src/"]
COPY ["Saki-ML.csproj", "/src/"]
RUN dotnet restore "/src/Saki-ML.csproj"

COPY . .
RUN dotnet publish "Saki-ML.csproj" -c Release -o /app/publish -r linux-x64 --self-contained true /p:UseAppHost=true

FROM debian:bookworm-slim AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates tzdata && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["/app/Saki-ML"]


