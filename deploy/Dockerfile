# Imagem do ChecklistPlantao.Server.
# Construir a partir da RAIZ do repositório:
#   docker build -f deploy/Dockerfile -t checklistplantao-server .

FROM mcr.microsoft.com/dotnet/sdk:10.0.201 AS build
WORKDIR /src

# Restaura primeiro apenas os manifestos: assim uma alteração de código não invalida
# a camada de restore, que é a mais cara.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ChecklistPlantao.Domain/*.csproj src/ChecklistPlantao.Domain/
COPY src/ChecklistPlantao.Contracts/*.csproj src/ChecklistPlantao.Contracts/
COPY src/ChecklistPlantao.Application/*.csproj src/ChecklistPlantao.Application/
COPY src/ChecklistPlantao.Infrastructure/*.csproj src/ChecklistPlantao.Infrastructure/
COPY src/ChecklistPlantao.Server/*.csproj src/ChecklistPlantao.Server/
RUN dotnet restore src/ChecklistPlantao.Server/ChecklistPlantao.Server.csproj

COPY src/ChecklistPlantao.Domain/ src/ChecklistPlantao.Domain/
COPY src/ChecklistPlantao.Contracts/ src/ChecklistPlantao.Contracts/
COPY src/ChecklistPlantao.Application/ src/ChecklistPlantao.Application/
COPY src/ChecklistPlantao.Infrastructure/ src/ChecklistPlantao.Infrastructure/
COPY src/ChecklistPlantao.Server/ src/ChecklistPlantao.Server/

RUN dotnet publish src/ChecklistPlantao.Server/ChecklistPlantao.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app

# A imagem runtime não inclui wget/curl. Instalar somente o cliente usado pelo health check e
# remover o índice do apt mantém a camada pequena e torna o HEALTHCHECK realmente executável.
# Depois disso o processo da aplicação continua rodando sem privilégios.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 10001 --create-home --shell /usr/sbin/nologin checklist \
    && mkdir -p /data && chown -R checklist:checklist /data

COPY --from=build --chown=checklist:checklist /app/publish .

USER checklist

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Path=/data/checklistplantao.db

EXPOSE 8080

# O health check é o mesmo endpoint que o orquestrador e o operador usam.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--max-time", "5", "http://localhost:8080/health/ready"]

ENTRYPOINT ["dotnet", "ChecklistPlantao.Server.dll"]
