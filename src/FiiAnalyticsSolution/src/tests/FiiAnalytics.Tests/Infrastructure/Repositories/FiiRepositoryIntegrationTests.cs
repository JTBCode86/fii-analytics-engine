using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using DotNet.Testcontainers.Builders;
using Testcontainers.LocalStack;
using Xunit;
using FiiAnalytics.Infrastructure.Repositories;

namespace FiiAnalytics.Tests.Infrastructure.Repositories
{
    public class FiiRepositoryIntegrationTests : IAsyncLifetime
    {
        private readonly LocalStackContainer _localStack = new LocalStackBuilder()
            .WithImage("localstack/localstack:3.7.2")
            .WithEnvironment("SERVICES", "dynamodb")
            .Build();

        private IAmazonDynamoDB _dynamoClient = null!;
        
        public async Task InitializeAsync()
        {
            await _localStack.StartAsync();

            var credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");
            _dynamoClient = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig
            {
                ServiceURL = _localStack.GetConnectionString(),
                AuthenticationRegion = "us-east-1",
            });

            await CreateTableAsync(); //1. Crie a tabela antes de iniciar os testes
            await Task.Delay(3000);   // 2. Adicione este delay para garantir que o DynamoDB do LocalStack processou a criação
        }

        public async Task DisposeAsync() => await _localStack.DisposeAsync();

        #region Métodos Auxiliares de Seed e Infra

        private async Task CreateTableAsync()
        {
            var request = new CreateTableRequest
            {
                TableName = "FiiAnalyticsDb",
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition("PK", ScalarAttributeType.S),
                    new AttributeDefinition("SK", ScalarAttributeType.S)
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement("PK", KeyType.HASH),
                    new KeySchemaElement("SK", KeyType.RANGE)
                },
                BillingMode = BillingMode.PAY_PER_REQUEST
            };

            await _dynamoClient.CreateTableAsync(request);

            // ADIÇÃO: Aguardar a tabela ficar disponível
            bool isTableAvailable = false;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var response = await _dynamoClient.DescribeTableAsync("FiiAnalyticsDb");
                    if (response.Table.TableStatus == TableStatus.ACTIVE)
                    {
                        isTableAvailable = true;
                        break;
                    }
                }
                catch { /* Ignora erro de ainda não existir */ }
                await Task.Delay(1000);
            }

            if (!isTableAvailable) throw new Exception("Falha ao criar/ativar tabela FiiAnalyticsDb no LocalStack.");
        }

        private async Task SeedDataAsync(string usuarioId, int quantidadeInicial = 0, int quantidadeAtualizada = 0)
        {
            var quantidadeFinal = quantidadeAtualizada > 0 ? quantidadeAtualizada : quantidadeInicial;

            var request = new PutItemRequest
            {
                TableName = "FiiAnalyticsDb",
                Item = new Dictionary<string, AttributeValue>
            {
                // O repositório espera o prefixo "USER#"
                { "PK", new AttributeValue { S = $"USER#{usuarioId}" } }, 
                // Sua query não define SK, mas se houver um, deve ser consistente
                { "SK", new AttributeValue { S = "CARTEIRA" } },
                { "Quantidade", new AttributeValue { N = quantidadeFinal.ToString() } },
                { "Ticker", new AttributeValue { S = "HGLG11" } } // Adicionado para evitar erro na leitura
            }
                };
            await _dynamoClient.PutItemAsync(request);
        }

        private async Task SeedDataCompletaAsync(string usuarioId, string ticker, decimal precoMedio, decimal cotacao)
        {
            // Para o teste de Mapeamento, precisamos inserir dois registros:
            // 1. O item do usuário (para a Query retornar algo)
            await SeedDataAsync(usuarioId, quantidadeInicial: 1);

            // 2. O item de metadados (para o BatchGetItem encontrar o ativo)
            var request = new PutItemRequest
            {
                TableName = "FiiAnalyticsDb",
                Item = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"ATIVO#{ticker.ToUpper()}" } },
                { "SK", new AttributeValue { S = "METADATA" } },
                { "Cotacao", new AttributeValue { N = cotacao.ToString() } },
                { "PrecoMedio", new AttributeValue { N = precoMedio.ToString() } }
            }
                };
            await _dynamoClient.PutItemAsync(request);
        }

        #endregion

        #region Testes de Integração

        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveRetornarDados_QuandoExistiremNoDynamo()
        {
            var repo = new FiiRepository(_dynamoClient);
            var usuarioId = "USER#123";

            await SeedDataAsync(usuarioId, quantidadeInicial: 10);

            var result = await repo.ObterCarteiraComMetadadosAsync(usuarioId);

            Assert.NotNull(result.Carteira);
            Assert.NotEmpty(result.Carteira!);
        }

        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveRetornarVazio_QuandoUsuarioNaoExistirNoDynamo()
        {
            var repo = new FiiRepository(_dynamoClient);
            var result = await repo.ObterCarteiraComMetadadosAsync("USER#999999");

            Assert.NotNull(result.Carteira);
            Assert.Empty(result.Carteira!);
        }

        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveRetornarDadosAtualizados_QuandoItemForAtualizadoNoDynamo()
        {
            var repo = new FiiRepository(_dynamoClient);
            var usuarioId = "USER#123";

            await SeedDataAsync(usuarioId, quantidadeInicial: 10);
            await SeedDataAsync(usuarioId, quantidadeAtualizada: 50);

            var result = await repo.ObterCarteiraComMetadadosAsync(usuarioId);

            var item = result.Carteira.FirstOrDefault();
            Assert.NotNull(item);
            Assert.Equal(50, item!.Quantidade);
        }

        
        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveMapearCamposCorretamente_QuandoDadosExistirem()
        {
            // Arrange
            var repo = new FiiRepository(_dynamoClient);
            var usuarioId = "MAP";
            var ticker = "HGLG11";
            var precoMedio = 10.5m;
            var cotacao = 150.0m;
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            // 1. Inserir o item do Usuário (O repositório usa PK = "USER#" + usuarioId)
            var requestCarteira = new PutItemRequest
            {
                TableName = "FiiAnalyticsDb",
                Item = new Dictionary<string, AttributeValue>
                {
                    { "PK", new AttributeValue { S = $"USER#{usuarioId}" } },
                    { "SK", new AttributeValue { S = "CARTEIRA" } },
                    { "Ticker", new AttributeValue { S = ticker } },
                    { "Quantidade", new AttributeValue { N = "10" } },
                    { "PrecoMedio", new AttributeValue { N = precoMedio.ToString(culture) } }
                }
            };
            await _dynamoClient.PutItemAsync(requestCarteira);

            // 2. Inserir o item do Ativo (O repositório usa PK = "ATIVO#" + ticker)
            var requestAtivo = new PutItemRequest
            {
                TableName = "FiiAnalyticsDb",
                Item = new Dictionary<string, AttributeValue>
                {
                    { "PK", new AttributeValue { S = $"ATIVO#{ticker.ToUpper()}" } },
                    { "SK", new AttributeValue { S = "METADATA" } },
                    { "Cotacao", new AttributeValue { N = cotacao.ToString(culture) } }
                }
            };
            await _dynamoClient.PutItemAsync(requestAtivo);

            // Act
            // Passamos apenas "MAP" porque o repositório concatena com "USER#"
            var result = await repo.ObterCarteiraComMetadadosAsync(usuarioId);

            // Assert
            Assert.NotNull(result.Carteira);
            var ativo = result.Carteira.FirstOrDefault(x => x.Ticker == ticker);

            
            Assert.NotNull(ativo); // Se falhar aqui, verifique se a PK no Seed está idêntica à do repositório
            Assert.Equal(precoMedio, ativo!.PrecoMedio);
            Assert.Equal(ticker, ativo.Ticker);
        }

        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveRetornarListasVazias_QuandoUsuarioNaoTiverCarteira()
        {
            // Arrange
            var repo = new FiiRepository(_dynamoClient);
            var usuarioIdInexistente = "USER_SEM_DADOS";

            // Act
            // Não realizamos nenhum Seed de dados para este ID
            var result = await repo.ObterCarteiraComMetadadosAsync(usuarioIdInexistente);

            // Assert
            Assert.NotNull(result.Carteira);
            Assert.NotNull(result.Metadados);

            Assert.Empty(result.Carteira); // Garante que a lista está vazia (Count == 0)
            Assert.Empty(result.Metadados); // Garante que não tentou buscar metadados para itens inexistentes
        }

        [Fact]
        public async Task ObterCarteiraComMetadadosAsync_DeveRetornarCarteiraComMetadadosVazios_QuandoAtivoNaoTiverMetadados()
        {
            // Arrange
            var repo = new FiiRepository(_dynamoClient);
            var usuarioId = "USER_INCONSISTENTE";
            var ticker = "FII_SEM_META"; // Ativo que não terá metadados cadastrados

            // Inserimos apenas a carteira (sem o item de metadados)
            var requestCarteira = new PutItemRequest
            {
                TableName = "FiiAnalyticsDb",
                Item = new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"USER#{usuarioId}" } },
            { "SK", new AttributeValue { S = "CARTEIRA" } },
            { "Ticker", new AttributeValue { S = ticker } },
            { "Quantidade", new AttributeValue { N = "1" } },
            { "PrecoMedio", new AttributeValue { N = "10.0" } }
        }
            };
            await _dynamoClient.PutItemAsync(requestCarteira);

            // Act
            var result = await repo.ObterCarteiraComMetadadosAsync(usuarioId);

            // Assert
            Assert.Single(result.Carteira); // Deve trazer o item da carteira
            Assert.Empty(result.Metadados); // Não deve encontrar metadados, mas não deve quebrar
            Assert.Equal(ticker, result.Carteira.First().Ticker);
        }
        #endregion
    }
}