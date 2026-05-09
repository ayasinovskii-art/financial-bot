# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props FinanceBot.sln ./
COPY src/FinanceBot.Domain/FinanceBot.Domain.csproj src/FinanceBot.Domain/
COPY src/FinanceBot.Application/FinanceBot.Application.csproj src/FinanceBot.Application/
COPY src/FinanceBot.Infrastructure/FinanceBot.Infrastructure.csproj src/FinanceBot.Infrastructure/
COPY src/FinanceBot.Host/FinanceBot.Host.csproj src/FinanceBot.Host/

RUN dotnet restore src/FinanceBot.Host/FinanceBot.Host.csproj

COPY src/ ./src/

RUN dotnet publish src/FinanceBot.Host/FinanceBot.Host.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_ENVIRONMENT=Production

RUN groupadd --system --gid 1000 financebot \
 && useradd  --system --uid 1000 --gid financebot --no-create-home financebot

COPY --from=build /app/publish .
RUN chown -R financebot:financebot /app
USER financebot

EXPOSE 8080
EXPOSE 4053

ENTRYPOINT ["dotnet", "FinanceBot.Host.dll"]
