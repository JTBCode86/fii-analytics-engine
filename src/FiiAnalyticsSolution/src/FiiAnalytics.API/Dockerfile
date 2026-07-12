FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# 1. Copia os arquivos de solução e projeto
COPY **/*.sln ./
COPY src/FiiAnalytics.Domain/ ./FiiAnalytics.Domain/
COPY src/FiiAnalytics.Application/ ./FiiAnalytics.Application/
COPY src/FiiAnalytics.Infrastructure/ ./FiiAnalytics.Infrastructure/
COPY src/FiiAnalytics.API/ ./FiiAnalytics.API/
COPY src/tests/FiiAnalytics.Tests/ ./tests/FiiAnalytics.Tests/

# 2. Agora o restore vai encontrar tudo
RUN dotnet restore 

# 3. Copia o restante do código
COPY . .

# 4. Publica
RUN dotnet publish FiiAnalytics.API/FiiAnalytics.API.csproj -c Release -o /out --no-restore

# Estágio de Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 80
ENTRYPOINT ["dotnet", "FiiAnalytics.API.dll"]