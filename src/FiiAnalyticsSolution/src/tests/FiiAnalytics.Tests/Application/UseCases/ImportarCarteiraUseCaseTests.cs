using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;
using FiiAnalytics.Application.UseCases;
using Moq;
using Xunit;

namespace FiiAnalytics.Tests.Application.UseCases;

public class ImportarCarteiraUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_PropagaErroQuandoPublicacaoFalhar()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .ReturnsAsync("arquivo");
        var queue = new Mock<IQueueService>();
        queue.Setup(q => q.PublishEventAsync(It.IsAny<string>(), It.IsAny<CarteiraImportadaEvent>()))
            .ThrowsAsync(new InvalidOperationException("Fila indisponível"));
        var importacoes = new Mock<IImportacaoRepository>();
        var useCase = new ImportarCarteiraUseCase(storage.Object, queue.Object, importacoes.Object);
        using var stream = new MemoryStream(new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecutarAsync(new ImportarCarteiraInput("123", "carteira.csv", stream)));

        storage.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), stream), Times.Once);
        importacoes.Verify(r => r.MarcarFalhaPublicacaoAsync("123", It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
