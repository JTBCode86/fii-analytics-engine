using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Domain.Entities;
using FiiAnalytics.Domain.Interfaces;
using Moq;
using Xunit;

namespace FiiAnalytics.Tests.Application.Handlers 
{
    public class GetRentabilidadeCarteiraHandlerTests
    {
        private readonly Mock<IFiiRepository> _mockRepo;
        private readonly GetRentabilidadeCarteiraHandler _handler;

        public GetRentabilidadeCarteiraHandlerTests()
        {
            _mockRepo = new Mock<IFiiRepository>();
            _handler = new GetRentabilidadeCarteiraHandler(_mockRepo.Object);
        }

        [Fact]
        public async Task Handle_DeveCalcularRentabilidadeTotal_QuandoExistiremMultiplosAtivosNaCarteira()
        {
            // Arrange
            var userId = "USER#123";

            // Simulando o retorno da Tupla conforme definido na sua interface
            var carteiraMock = new List<Carteira> { new Carteira(), new Carteira() };
            var metadadosMock = new List<Fii> { new Fii(), new Fii() };

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
                  new Carteira { Ticker = "HGLG11", Quantidade = 10, PrecoMedio = 10m }
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
            Assert.Equal(0.1m, result.RentabilidadeGlobal);

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
                new Carteira { Ticker = "HGLG11", Quantidade = 1, PrecoMedio = 0m }
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