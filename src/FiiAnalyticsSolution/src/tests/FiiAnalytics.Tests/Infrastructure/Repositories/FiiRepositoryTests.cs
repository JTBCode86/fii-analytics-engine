using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace FiiAnalytics.Tests.Infrastructure.Repositories;

public class FiiRepositoryTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("inválido", null)]
    [InlineData("-1", null)]
    [InlineData("0", "0")]
    [InlineData("8.5", "8.5")]
    public async Task ObterPorTickerAsync_DistingueYieldAusenteInvalidoEZero(string? valor, string? esperado)
    {
        var client = new Mock<IAmazonDynamoDB>();
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = "FII#HGLG11" },
            ["Cotacao"] = new() { S = "100" }
        };
        if (valor != null) item["DividendYield"] = new() { S = valor };
        client.Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = item });

        var fii = await new FiiRepository(client.Object).ObterPorTickerAsync("HGLG11");

        Assert.NotNull(fii);
        Assert.Equal(esperado == null ? null : decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), fii.DividendYield);
    }

    [Fact]
    public async Task ObterCarteiraComMetadadosAsync_PaginaCarteiraEDivideERetentaLotes()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse());
        var ultimaChave = new Dictionary<string, AttributeValue> { ["PK"] = new() { S = "USER#123" }, ["SK"] = new() { S = "CARTEIRA#99" } };
        var chamadasQuery = 0;
        client.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryRequest request, CancellationToken _) =>
            {
                chamadasQuery++;
                if (chamadasQuery == 1)
                {
                    Assert.True(request.ExclusiveStartKey == null || request.ExclusiveStartKey.Count == 0);
                    return new QueryResponse
                    {
                        Items = Enumerable.Range(0, 100).Select(Carteira).ToList(),
                        LastEvaluatedKey = ultimaChave
                    };
                }

                Assert.Equal(ultimaChave, request.ExclusiveStartKey);
                return new QueryResponse { Items = new List<Dictionary<string, AttributeValue>> { Carteira(100) } };
            });

        var tamanhos = new List<int>();
        client.Setup(c => c.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BatchGetItemRequest request, CancellationToken _) =>
            {
                var keys = request.RequestItems["FiiAnalyticsDb"].Keys;
                tamanhos.Add(keys.Count);
                var pendentes = tamanhos.Count == 1 ? keys.Take(1).ToList() : new List<Dictionary<string, AttributeValue>>();
                var processadas = tamanhos.Count == 1 ? keys.Skip(1) : keys;
                return new BatchGetItemResponse
                {
                    Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
                    {
                        ["FiiAnalyticsDb"] = processadas.Select(key => new Dictionary<string, AttributeValue>
                        {
                            ["PK"] = key["PK"],
                            ["Cotacao"] = new() { S = "10" }
                        }).ToList()
                    },
                    UnprocessedKeys = pendentes.Count == 0
                        ? new Dictionary<string, KeysAndAttributes>()
                        : new Dictionary<string, KeysAndAttributes> { ["FiiAnalyticsDb"] = new() { Keys = pendentes } }
                };
            });

        var (carteira, metadados) = await new FiiRepository(client.Object).ObterCarteiraComMetadadosAsync("123");

        Assert.Equal(2, chamadasQuery);
        Assert.Equal(new[] { 100, 1, 1 }, tamanhos);
        Assert.Equal(101, carteira.Count);
        Assert.Equal(101, metadados.Count);
        Assert.Equal(101, metadados.Select(m => m.Ticker).Distinct().Count());
    }

    [Fact]
    public async Task ObterCarteiraComMetadadosAsync_ConsultaSomenteSnapshotAtivo()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue> { ["CarteiraAtiva"] = new() { S = "nova" } } });
        client.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryRequest request, CancellationToken _) =>
            {
                Assert.Equal("SNAPSHOT#123#nova", request.ExpressionAttributeValues[":v_pk"].S);
                Assert.Equal("ATIVO#", request.ExpressionAttributeValues[":prefixo"].S);
                Assert.True(request.ConsistentRead);
                return new QueryResponse { Items = new List<Dictionary<string, AttributeValue>>() };
            });

        var result = await new FiiRepository(client.Object).ObterCarteiraComMetadadosAsync("123");

        Assert.Empty(result.Carteira);
    }

    [Fact]
    public async Task ObterCarteiraComMetadadosAsync_FalhaSeDynamoNaoProcessarAsChaves()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse());
        client.Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = new List<Dictionary<string, AttributeValue>> { Carteira(1) } });
        client.Setup(c => c.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BatchGetItemRequest request, CancellationToken _) =>
                new BatchGetItemResponse { UnprocessedKeys = request.RequestItems });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FiiRepository(client.Object).ObterCarteiraComMetadadosAsync("123"));
        client.Verify(c => c.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    private static Dictionary<string, AttributeValue> Carteira(int index) => new()
    {
        ["Ticker"] = new() { S = $"FII{index:000}" },
        ["Quantidade"] = new() { N = "1" },
        ["PrecoMedio"] = new() { S = "10" }
    };
}
