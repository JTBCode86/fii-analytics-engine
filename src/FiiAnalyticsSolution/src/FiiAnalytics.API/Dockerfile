# Estágio de Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# 1. Copiar os arquivos .csproj mantendo a estrutura exata de pastas
COPY src/FiiAnalytics.Domain/FiiAnalytics.Domain.csproj ./src/FiiAnalytics.Domain/
COPY src/FiiAnalytics.Application/FiiAnalytics.Application.csproj ./src/FiiAnalytics.Application/
COPY src/FiiAnalytics.Infrastructure/FiiAnalytics.Infrastructure.csproj ./src/FiiAnalytics.Infrastructure/
COPY src/FiiAnalytics.API/FiiAnalytics.API.csproj ./src/FiiAnalytics.API/

# 2. Executar o restore a partir da raiz (/app), mantendo a resolução do ".." correta
RUN dotnet restore ./src/FiiAnalytics.API/FiiAnalytics.API.csproj

# 3. Copiar todo o resto do código do monorepo
COPY . .

# 4. Compilar e publicar os binários apontando para a API
RUN dotnet publish ./src/FiiAnalytics.API/FiiAnalytics.API.csproj -c Release -o /out

# Estágio de Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 80
ENTRYPOINT ["dotnet", "FiiAnalytics.API.dll"]