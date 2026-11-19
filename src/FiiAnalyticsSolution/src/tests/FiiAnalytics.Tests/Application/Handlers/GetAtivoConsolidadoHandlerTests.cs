using Moq;
using Xunit;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Domain.Interfaces;
using Amazon.DynamoDBv2.Model;

namespace FiiAnalytics.Tests.Application.Handlers
{
    public class GetAtivoConsolidadoHandlerTests
    {
        [Fact]
        public async Task Handle_DeveRetornarFundoResponse_QuandoDadosExistirem()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var ticker = "SNCI11";

            // Simulando retorno do DynamoDB
            var mercado = new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "87.1" } } };
            var carteira = new Dictionary<string, AttributeValue> { { "PrecoMedio", new AttributeValue { N = "85.6" } } };

            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(mercado);
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(carteira);
            
            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
                    .ReturnsAsync(true);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);
            var query = new GetAtivoConsolidadoQuery(ticker, "user123");

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ticker, result.Ticker);
            Assert.Equal(87.1m, result.CotacaoAtual, precision: 1);
            Assert.Equal(85.6m, result.PrecoMedio, precision: 1);
            Assert.True(result.Performance > 0); // (87.1 / 85.6) - 1 > 0
        }

        [Fact]
        public async Task Handle_DeveInformarCotacaoIndisponivel_QuandoDadosNaoExistiremNoRepositorio()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var ticker = "SNCI11";

            // Simulando retorno nulo do banco de dados (ativo inexistente)
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync((Dictionary<string, AttributeValue>)null);
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(new Dictionary<string, AttributeValue> { ["PrecoMedio"] = new() { N = "10" } });

            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
                    .ReturnsAsync(true);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);
            var query = new GetAtivoConsolidadoQuery(ticker, "user123");

            // Act
            await Assert.ThrowsAsync<DadosMercadoIndisponiveisException>(
                () => handler.Handle(query, CancellationToken.None));
        }

        [Fact]
        public async Task Handle_DeveRetornarZeroPerformance_QuandoPrecoMedioForZero()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            // Simula PrecoMedio = 0
            var carteira = new Dictionary<string, AttributeValue>
            {
                { "PrecoMedio", new AttributeValue { N = "0" } },
                { "Cotacao", new AttributeValue { N = "100" } }
            };
            
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(carteira);
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(carteira);

            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
                    .ReturnsAsync(true);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            // Act
            var result = await handler.Handle(new GetAtivoConsolidadoQuery("SNCI11", "user123"), CancellationToken.None);

            // Assert
            Assert.Equal(0, result.Performance); // Garante que a lógica precoMedio > 0 trata o erro
        }

        [Fact]
        public async Task Handle_DeveInformarCotacaoIndisponivel_QuandoCotacaoEstiverEmFormatoInvalido()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            // Simula um valor que não é um número válido ("abc")
            var mercado = new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "abc" } } };

            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
                    .ReturnsAsync(true);

            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(mercado);
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(new Dictionary<string, AttributeValue> { ["PrecoMedio"] = new() { N = "10" } });

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            // Act
            await Assert.ThrowsAsync<DadosMercadoIndisponiveisException>(
                () => handler.Handle(new GetAtivoConsolidadoQuery("SNCI11","user123"), CancellationToken.None));
        }

        [Fact]
        public async Task Handle_DeveNormalizarTickerParaMaiusculas()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
         .ReturnsAsync(true);
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "100" } } });
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, AttributeValue> { ["PrecoMedio"] = new() { N = "10" } });

            // Act
            // Passando ticker em minúsculas
            var result = await handler.Handle(new GetAtivoConsolidadoQuery("snci11","user123"), CancellationToken.None);

            // Assert
            // O mock verifica se a busca foi feita com "SNCI11" (ToUpper)
            mockRepo.Verify(r => r.GetAsync(It.Is<string>(s => s.Contains("SNCI11")), It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Handle_DeveInformarAtivoInexistente_QuandoNaoEstiverNaCarteira()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            mockRepo.Setup(repo => repo.UsuarioExisteAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            var ticker = "SNCI11";

            // Simulando: Mercado possui cotação, mas Carteira está vazia (nula)
            var mercado = new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "100.0" } } };

            mockRepo.Setup(r => r.GetAsync($"ATIVO#{ticker}", "METADATA")).ReturnsAsync(mercado);
            mockRepo.Setup(r => r.ObterAtivoCarteiraAsync(It.IsAny<string>(), ticker))
                .ReturnsAsync(new Dictionary<string, AttributeValue>());

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);
            var query = new GetAtivoConsolidadoQuery(ticker, "user123");

            // Act
            await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(query, CancellationToken.None));
        }
    }
}