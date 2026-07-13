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
                    .ReturnsAsync((string pk, string sk) => sk == "METADATA" ? mercado : carteira);

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
        public async Task Handle_DeveRetornarValoresZero_QuandoDadosNaoExistiremNoRepositorio()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var ticker = "SNCI11";

            // Simulando retorno nulo do banco de dados (ativo inexistente)
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync((Dictionary<string, AttributeValue>)null);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);
            var query = new GetAtivoConsolidadoQuery(ticker, "user123");

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ticker, result.Ticker);
            Assert.Equal(0, result.CotacaoAtual); // Verifica tratamento de erro
            Assert.Equal(0, result.PrecoMedio);   // Verifica tratamento de erro
            Assert.Equal(0, result.Performance);  // Performance deve ser 0 se PrecoMedio for 0
        }

        [Fact]
        public async Task Handle_DeveRetornarZeroPerformance_QuandoPrecoMedioForZero()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            // Simula PrecoMedio = 0
            var carteira = new Dictionary<string, AttributeValue> { { "PrecoMedio", new AttributeValue { N = "0" } } };
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(carteira);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            // Act
            var result = await handler.Handle(new GetAtivoConsolidadoQuery("SNCI11", "user123"), CancellationToken.None);

            // Assert
            Assert.Equal(0, result.Performance); // Garante que a lógica precoMedio > 0 trata o erro
        }

        [Fact]
        public async Task Handle_DeveRetornarZero_QuandoDadosEstiveremEmFormatoInvalido()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            // Simula um valor que não é um número válido ("abc")
            var mercado = new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "abc" } } };
            mockRepo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(mercado);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            // Act
            var result = await handler.Handle(new GetAtivoConsolidadoQuery("SNCI11","user123"), CancellationToken.None);

            // Assert
            Assert.Equal(0, result.CotacaoAtual); // O ExtrairDecimal deve retornar 0 ao falhar no TryParse
        }

        [Fact]
        public async Task Handle_DeveNormalizarTickerParaMaiusculas()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);

            // Act
            // Passando ticker em minúsculas
            var result = await handler.Handle(new GetAtivoConsolidadoQuery("snci11","user123"), CancellationToken.None);

            // Assert
            // O mock verifica se a busca foi feita com "SNCI11" (ToUpper)
            mockRepo.Verify(r => r.GetAsync(It.Is<string>(s => s.Contains("SNCI11")), It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Handle_DeveRetornarValoresParciais_QuandoApenasUmRepositorioRetornaDados()
        {
            // Arrange
            var mockRepo = new Mock<IFiiRepository>();
            var ticker = "SNCI11";

            // Simulando: Mercado possui cotação, mas Carteira está vazia (nula)
            var mercado = new Dictionary<string, AttributeValue> { { "Cotacao", new AttributeValue { N = "100.0" } } };

            mockRepo.Setup(r => r.GetAsync($"ATIVO#{ticker}", "METADATA")).ReturnsAsync(mercado);
            mockRepo.Setup(r => r.GetAsync("USER#123456", $"CARTEIRA#{ticker}")).ReturnsAsync((Dictionary<string, AttributeValue>)null);

            var handler = new GetAtivoConsolidadoHandler(mockRepo.Object);
            var query = new GetAtivoConsolidadoQuery(ticker, "user123");

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100.0m, result.CotacaoAtual); // Mercado carregado
            Assert.Equal(0, result.PrecoMedio);        // Carteira inexistente tratada como 0
            Assert.Equal(0, result.Performance);       // Sem preço médio, performance não pode ser calculada
        }
    }
}