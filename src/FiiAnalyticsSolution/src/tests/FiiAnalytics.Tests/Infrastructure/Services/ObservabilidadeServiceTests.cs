using Amazon.SQS;
using Amazon.SQS.Model;
using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;
using FiiAnalytics.Infrastructure.Services;
using Moq;
using Xunit;

namespace FiiAnalytics.Tests.Infrastructure.Services;

public class ObservabilidadeServiceTests
{
    [Fact]
    public async Task ConsultarAsync_RetornaContagensReaisEHistoricoDoUsuario()
    {
        var importacoes = new Mock<IImportacaoRepository>();
        var id = Guid.NewGuid();
        importacoes.Setup(r => r.ListarRecentesAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ImportacaoStatusResponse(id, "FALHOU", DateTimeOffset.UtcNow, "Erro")]);
        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetQueueUrlRequest request, CancellationToken _) => new GetQueueUrlResponse { QueueUrl = request.QueueName });
        sqs.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "3",
                    ["ApproximateNumberOfMessagesNotVisible"] = "2",
                    ["ApproximateNumberOfMessagesDelayed"] = "1"
                }
            });

        var result = await new ObservabilidadeService(importacoes.Object, sqs.Object).ConsultarAsync("123", CancellationToken.None);

        Assert.Equal(id, Assert.Single(result.Importacoes).ImportacaoId);
        Assert.Equal(4, result.Filas.Count);
        Assert.All(result.Filas, fila => Assert.Equal((3, 2, 1), (fila.Disponiveis, fila.EmProcessamento, fila.Atrasadas)));
    }

    [Fact]
    public async Task ConsultarAsync_NaoConverteContagemAusenteEmZero()
    {
        var importacoes = new Mock<IImportacaoRepository>();
        importacoes.Setup(r => r.ListarRecentesAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ImportacaoStatusResponse>());
        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "fila" });
        sqs.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse { Attributes = new Dictionary<string, string>() });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ObservabilidadeService(importacoes.Object, sqs.Object).ConsultarAsync("123", CancellationToken.None));
    }
}
