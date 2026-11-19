using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Domain.Entities;
using FiiAnalytics.Domain.Interfaces;
using Moq;
using Xunit;
using System.Text.Json;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Tests.Application.Handlers 
{
    public class GetRentabilidadeCarteiraHandlerTests
    {
        private sealed record YieldSerializado([property: JsonDecimalFormat("F2")] decimal? Valor);
        private readonly Mock<IFiiRepository> _mockRepo;
        private readonly GetRentabilidadeCarteiraHandler _handler;

        public GetRentabilidadeCarteiraHandlerTests()
        {
            _mockRepo = new Mock<IFiiRepository>();
            _handler = new GetRentabilidadeCarteiraHandler(_mockRepo.Object);
        }

        [Fact]
        public void AtivoAnalise_DevePreservarQuatroCasasNaPerformance()
        {
            var ativo = new FiiAnalytics.Application.DTOs.AtivoAnaliseDto("HGLG11", 1, 150, 160, 1, 8, 0.066666m, null);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ativo));
            Assert.Equal("0.0667", json.RootElement.GetProperty("Performance").GetString());
        }

        [Fact]
        public void YieldOpcional_SerializaEDesserializaValorOuAusencia()
        {
            var valor = JsonSerializer.Serialize(new YieldSerializado(8.5m));
            var ausente = JsonSerializer.Serialize(new YieldSerializado(null));
            Assert.Equal("{\"Valor\":\"8.50\"}", valor);
            Assert.Equal("{\"Valor\":null}", ausente);
            Assert.Equal(8.5m, JsonSerializer.Deserialize<YieldSerializado>(valor)!.Valor);
            Assert.Null(JsonSerializer.Deserialize<YieldSerializado>(ausente)!.Valor);
        }

        [Fact]
        public async Task Handle_EstimaProventosUsandoValoresDaCarteiraSemArredondarAntesDaSoma()
        {
            var carteira = new List<Carteira>
            {
                new() { Ticker = "HGLG11", Quantidade = 10, PrecoMedio = 10 },
                new() { Ticker = "XPLG11", Quantidade = 2, PrecoMedio = 90 }
            };
            var metadados = new List<Fii>
            {
                new() { Ticker = "HGLG11", Cotacao = 11, DividendYield = 8.5m },
                new() { Ticker = "XPLG11", Cotacao = 100, DividendYield = 12.5m }
            };
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync("123")).ReturnsAsync((carteira, metadados));

            var result = await _handler.Handle(new GetRentabilidadeCarteiraQuery("123"));

            Assert.NotNull(result.EstimativaProventos);
            Assert.Equal(34.35m, result.EstimativaProventos.Anual);
            Assert.Equal(2.8625m, result.EstimativaProventos.MediaMensal);
            Assert.Equal(9.35m, result.EstimativaProventos.Ativos[0].Anual);
            Assert.Equal(25m, result.EstimativaProventos.Ativos[1].Anual);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("2.86", json.RootElement.GetProperty("EstimativaProventos").GetProperty("MediaMensal").GetString());
        }

        [Fact]
        public async Task Handle_NaoEstimaComYieldAusenteMasAceitaZeroInformado()
        {
            var carteira = new List<Carteira>
            {
                new() { Ticker = "HGLG11", Quantidade = 1 },
                new() { Ticker = "XPLG11", Quantidade = 1 }
            };
            var metadados = new List<Fii>
            {
                new() { Ticker = "HGLG11", Cotacao = 10, DividendYield = 0 },
                new() { Ticker = "XPLG11", Cotacao = 10, DividendYield = null }
            };
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync("123")).ReturnsAsync((carteira, metadados));

            var result = await _handler.Handle(new GetRentabilidadeCarteiraQuery("123"));

            Assert.Null(result.EstimativaProventos);
            Assert.Equal(0m, result.Ativos[0].DividendYield);
            Assert.Null(result.Ativos[1].DividendYield);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("0.00", json.RootElement.GetProperty("Ativos")[0].GetProperty("DividendYield").GetString());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Ativos")[1].GetProperty("DividendYield").ValueKind);

            metadados[1].DividendYield = 0;
            result = await _handler.Handle(new GetRentabilidadeCarteiraQuery("123"));
            Assert.Equal(0m, result.EstimativaProventos!.Anual);
        }

        [Fact]
        public async Task Handle_DeveCalcularRentabilidadeTotal_QuandoExistiremMultiplosAtivosNaCarteira()
        {
            // Arrange
            var userId = "USER#123";

            // Simulando o retorno da Tupla conforme definido na sua interface
            var carteiraMock = new List<Carteira> { new Carteira { Ticker = "HGLG11" }, new Carteira { Ticker = "XPLG11" } };
            var metadadosMock = new List<Fii> { new Fii { Ticker = "HGLG11", Cotacao = 10 }, new Fii { Ticker = "XPLG11", Cotacao = 20 } };

            // CORREÇÃO: Usando o _mockRepo do escopo da classe
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(userId))
                     .ReturnsAsync((carteiraMock, metadadosMock));

            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act
            var result = await _handler.Handle(query);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task Handle_DeveInformarCotacaoIndisponivel_QuandoNaoHouverMetadadosValidos()
        {
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync("123"))
                .ReturnsAsync((new List<Carteira> { new Carteira { Ticker = "HGLG11", Quantidade = 1, PrecoMedio = 10 } },
                    new List<Fii> { new Fii { Ticker = "HGLG11", Cotacao = 0 } }));

            var erro = await Assert.ThrowsAsync<DadosMercadoIndisponiveisException>(
                () => _handler.Handle(new GetRentabilidadeCarteiraQuery("123")));

            Assert.Contains("HGLG11", erro.Message);
        }

        [Fact]
        public async Task Handle_DeveRetornarResponseVazia_QuandoCarteiraNaoExistirParaUsuario()
        {
            // Arrange
            var userId = "USER_SEM_CARTEIRA";

            // Usando corretamente o _mockRepo definido no construtor
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(userId))
                     .ReturnsAsync((new List<Carteira>(), new List<Fii>()));

            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act
            var result = await _handler.Handle(query);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task Handle_DeveLancarExcecao_QuandoRepositorioFalhar()
        {
            // Arrange
            var userId = "USER_COM_ERRO";

            // Configuramos o Mock para simular uma falha (ex: falha de conexão)
            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(userId))
                     .ThrowsAsync(new System.Exception("Erro de conexão com DynamoDB"));

            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act & Assert
            // O teste verifica se o Handler propaga ou trata o erro conforme esperado
            await Assert.ThrowsAsync<System.Exception>(() => _handler.Handle(query));
        }

        [Fact]
        public async Task Handle_DeveLancarExcecao_QuandoUsuarioIdForInvalido()
        {
            // Arrange
            // Simulando um ID vazio ou null
            var userId = string.Empty;
            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(query));
        }

        [Fact]
        public async Task Handle_DeveCalcularRentabilidadeCorretamente_QuandoAtivoValorizar()
        {
            // Arrange
            var userId = "USER#123";

            // Simula uma carteira onde o usuário investiu 100 reais (PrecoMedio 10, Qtd 10)
            // E a cotação atual subiu para 11 (Lucro de 10%)
             var carteiraMock = new List<Carteira>
             {
                  new Carteira { Ticker = "HGLG11", Quantidade = 10, PrecoMedio = 10m, TotalProventos = 5 }
             };

             var metadadosMock = new List<Fii>
              {
                  new Fii { Ticker = "HGLG11", Cotacao = 11m, PVP = 1.0m, DividendYield = 0.05m }
              };

            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(userId))
                     .ReturnsAsync((carteiraMock, metadadosMock));

            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act
            var result = await _handler.Handle(query);

            // Assert
            // 1. O total investido deve ser 100 (10 * 10)
            Assert.Equal(100m, result.TotalInvestido);

            // 2. O valor atual deve ser 110 (10 * 11)
            Assert.Equal(110m, result.ValorAtual);

            // 3. A rentabilidade global deve ser 0.1 (ou seja, 10%)
            Assert.Equal(0.15m, result.RentabilidadeGlobal);

            // 4. Verifica se o item específico na lista também calculou a rentabilidade
            var ativo = result.Ativos.FirstOrDefault(a => a.Ticker == "HGLG11");
            Assert.NotNull(ativo);
            Assert.Equal(0.1m, ativo.Performance); // Lucro individual do ativo
        }

        [Fact]
        public async Task Handle_DeveRetornarZero_QuandoTotalInvestidoForZero()
        {
            // Arrange
            var userId = "USER#ZERO";

            // Simula uma carteira onde o custo do ativo é zero
            var carteiraMock = new List<Carteira>
            {
                new Carteira { Ticker = "HGLG11", Quantidade = 1, PrecoMedio = 0m, TotalProventos = 5 }
            };
                    var metadadosMock = new List<Fii>
            {
                new Fii { Ticker = "HGLG11", Cotacao = 100m }
            };

            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(userId))
                     .ReturnsAsync((carteiraMock, metadadosMock));

            var query = new GetRentabilidadeCarteiraQuery(userId);

            // Act
            var result = await _handler.Handle(query);

            // Assert
            Assert.Equal(0m, result.RentabilidadeGlobal);
            Assert.Equal(0m, result.TotalInvestido);
        }

        [Theory]
        [InlineData("  USER123  ")] // Com espaços
        [InlineData("user123")]     // Case sensitivity (assumindo que seu repo lida com isso ou deve lidar)
        public async Task Handle_DeveNormalizarUsuarioId_AoBuscarDados(string userIdEntrada)
        {
            // Arrange
            var userIdEsperado = userIdEntrada.Trim();

            _mockRepo.Setup(r => r.ObterCarteiraComMetadadosAsync(It.Is<string>(s => s == userIdEsperado)))
                     .ReturnsAsync((new List<Carteira>(), new List<Fii>()));

            var query = new GetRentabilidadeCarteiraQuery(userIdEntrada);

            // Act
            var result = await _handler.Handle(query);

            // Assert
            _mockRepo.Verify(r => r.ObterCarteiraComMetadadosAsync(userIdEsperado), Times.Once);
        }
    }
}