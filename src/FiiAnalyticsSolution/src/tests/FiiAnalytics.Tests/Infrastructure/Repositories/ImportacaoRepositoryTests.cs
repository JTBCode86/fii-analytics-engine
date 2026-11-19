using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace FiiAnalytics.Tests.Infrastructure.Repositories;

public class ImportacaoRepositoryTests
{
    [Fact]
    public async Task ListarRecentesAsync_LePaginasOrdenaPorDataEIgnoraControle()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var primeira = Guid.NewGuid();
        var segunda = Guid.NewGuid();
        var chave = new Dictionary<string, AttributeValue> { ["PK"] = new() { S = "USER#123" }, ["SK"] = new() { S = "IMPORTACAO#CONTROLE" } };
        Dictionary<string, AttributeValue> Item(Guid id, string data) => new()
        {
            ["SK"] = new() { S = $"IMPORTACAO#{id}" },
            ["Status"] = new() { S = "FALHOU" },
            ["AtualizadoEm"] = new() { S = data }
        };
        client.SetupSequence(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = [chave, Item(primeira, "2026-01-01T00:00:00Z")], LastEvaluatedKey = chave })
            .ReturnsAsync(new QueryResponse { Items = [Item(segunda, "2026-01-02T00:00:00Z")] });

        var result = await new ImportacaoRepository(client.Object).ListarRecentesAsync("123", CancellationToken.None);

        Assert.Equal(new[] { segunda, primeira }, result.Select(item => item.ImportacaoId));
        client.Verify(c => c.QueryAsync(It.Is<QueryRequest>(r =>
            r.ConsistentRead == true && r.ExpressionAttributeValues[":pk"].S == "USER#123"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ListarRecentesAsync_LimitaAosVinteMaisRecentes()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var itens = Enumerable.Range(0, 25).Select(i => new Dictionary<string, AttributeValue>
        {
            ["SK"] = new() { S = $"IMPORTACAO#{Guid.NewGuid()}" },
            ["Status"] = new() { S = "CONCLUIDO" },
            ["AtualizadoEm"] = new() { S = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i).ToString("O") }
        }).ToList();
        client.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = itens });

        var result = await new ImportacaoRepository(client.Object).ListarRecentesAsync("123", CancellationToken.None);

        Assert.Equal(20, result.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 24, 0, TimeSpan.Zero), result[0].AtualizadoEm);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero), result[19].AtualizadoEm);
    }

    [Fact]
    public async Task ObterAsync_RetornaStatusEDataDaImportacao()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["Status"] = new() { S = "CONCLUIDO" },
                    ["AtualizadoEm"] = new() { S = "2026-01-10T12:00:00+00:00" },
                    ["Mensagem"] = new() { S = "Carteira salva" }
                }
            });
        var id = Guid.NewGuid();

        var result = await new ImportacaoRepository(client.Object).ObterAsync("123", id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(id, result.ImportacaoId);
        Assert.Equal("CONCLUIDO", result.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-01-10T12:00:00+00:00"), result.AtualizadoEm);
        client.Verify(c => c.GetItemAsync(It.Is<GetItemRequest>(request =>
            request.ConsistentRead == true && request.Key["PK"].S == "USER#123" &&
            request.Key["SK"].S == $"IMPORTACAO#{id}"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
