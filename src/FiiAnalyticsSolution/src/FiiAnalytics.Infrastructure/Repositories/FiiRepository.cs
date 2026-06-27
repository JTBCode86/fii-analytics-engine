using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Domain.Entities;
using FiiAnalytics.Domain.Interfaces;

namespace FiiAnalytics.Infrastructure.Repositories;

public class FiiRepository : IFiiRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "FiiAnalyticsDb";

    public FiiRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<Fii?> ObterPorTickerAsync(string ticker)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"FII#{ticker.ToUpper()}" } },
                { "SK", new AttributeValue { S = "METRICAS" } }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        if (response.Item == null || response.Item.Count == 0) return null;

        return new Fii(
            ticker: response.Item["PK"].S.Replace("FII#", ""),
            nome: response.Item["Nome"].S,
            precoAtual: decimal.Parse(response.Item["PrecoAtual"].N),
            valorPatrimonialPorCota: decimal.Parse(response.Item["ValorPatrimonialPorCota"].N),
            dividendoAcumulado12M: decimal.Parse(response.Item["DividendoAcumulado12M"].N)
        );
    }

    public async Task<Dictionary<string, Fii>> ObterTodosFiisAsync()
    {
        // Nota: Scan deve ser usado com cautela em produção, mas serve perfeitamente para buscar a carga de FIIs locais
        var request = new ScanRequest
        {
            TableName = TableName,
            FilterExpression = "begins_with(PK, :fiiPrefix) AND SK = :skValue",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":fiiPrefix", new AttributeValue { S = "FII#" } },
                { ":skValue", new AttributeValue { S = "METRICAS" } }
            }
        };

        var response = await _dynamoDb.ScanAsync(request);
        var dicionario = new Dictionary<string, Fii>();

        foreach (var item in response.Items)
        {
            var ticker = item["PK"].S.Replace("FII#", "");
            var fii = new Fii(
                ticker: ticker,
                nome: item["Nome"].S,
                precoAtual: decimal.Parse(item["PrecoAtual"].N),
                valorPatrimonialPorCota: decimal.Parse(item["ValorPatrimonialPorCota"].N),
                dividendoAcumulado12M: decimal.Parse(item["DividendoAcumulado12M"].N)
            );
            dicionario.Add(ticker, fii);
        }

        return dicionario;
    }

    public async Task SalvarFiiAsync(Fii fii)
    {
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"FII#{fii.Ticker}" } },
                { "SK", new AttributeValue { S = "METRICAS" } },
                { "Nome", new AttributeValue { S = fii.Nome } },
                { "PrecoAtual", new AttributeValue { N = fii.PrecoAtual.ToString() } },
                { "ValorPatrimonialPorCota", new AttributeValue { N = fii.ValorPatrimonialPorCota.ToString() } },
                { "DividendoAcumulado12M", new AttributeValue { N = fii.DividendoAcumulado12M.ToString() } }
            }
        };

        await _dynamoDb.PutItemAsync(request);
    }
}