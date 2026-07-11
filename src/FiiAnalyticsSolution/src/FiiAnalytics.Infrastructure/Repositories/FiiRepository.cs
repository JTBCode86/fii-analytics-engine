using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Domain.Entities;
using FiiAnalytics.Domain.Interfaces;

namespace FiiAnalytics.Infrastructure.Repositories;

public class FiiRepository : IFiiRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "FiiAnalyticsDb";

    public FiiRepository(IAmazonDynamoDB dynamoDb) => _dynamoDb = dynamoDb;

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

        return MapearFii(response.Item);
    }

    public async Task<(List<Carteira> Carteira, List<Fii> Metadados)> ObterCarteiraComMetadadosAsync(string usuarioId)
    {
        // 1. Busca Carteira com acesso seguro aos dados
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "PK = :v_pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { { ":v_pk", new AttributeValue { S = $"USER#{usuarioId}" } } }
        };

        var response = await _dynamoDb.QueryAsync(queryRequest);

        var listaCarteira = new List<Carteira>();
        foreach (var item in response.Items)
        {
            listaCarteira.Add(new Carteira
            {
                Ticker = item.TryGetValue("Ticker", out var t) ? (t.S ?? "") : "",
                Quantidade = item.TryGetValue("Quantidade", out var q) ? int.Parse(q.N ?? "0") : 0,
                PrecoMedio = item.TryGetValue("PrecoMedio", out var p) ? decimal.Parse(p.N ?? "0") : 0
            });
        }

        // 2. Busca Metadados
        var listaMetadados = new List<Fii>();
        if (listaCarteira.Any())
        {
            var batchRequest = new BatchGetItemRequest
            {
                RequestItems = new Dictionary<string, KeysAndAttributes> {
                { TableName, new KeysAndAttributes {
                    Keys = listaCarteira.Select(c => new Dictionary<string, AttributeValue> {
                        { "PK", new AttributeValue { S = $"ATIVO#{c.Ticker.ToUpper()}" } },
                        { "SK", new AttributeValue { S = "METADATA" } }
                    }).ToList()
                }}
            }
            };

            var batchResponse = await _dynamoDb.BatchGetItemAsync(batchRequest);

            // Verifica se a tabela existe na resposta e se contém itens
            if (batchResponse.Responses != null && batchResponse.Responses.TryGetValue(TableName, out var items))
            {
                listaMetadados = items.Select(MapearFii).ToList();
            }
        }

        return (listaCarteira, listaMetadados);
    }

    private static Fii MapearFii(Dictionary<string, AttributeValue> item)
    {
        string GetValue(string key) 
        {
            if (item.TryGetValue(key, out var val) && val.S != null) return val.S;
            if (item.TryGetValue(key, out var valN) && valN.N != null) return valN.N;
            return "0";
        }

        return new Fii
        {
            // Se a PK existir, remove o prefixo, senão retorna vazio
            Ticker = item.TryGetValue("PK", out var pk) ? pk.S.Replace("ATIVO#", "") : string.Empty,

            // Usamos decimal.Parse sobre a string obtida com segurança
            Cotacao = decimal.TryParse(GetValue("Cotacao"), out var c) ? c : 0,
            DividendYield = decimal.TryParse(GetValue("DividendYield"), out var dy) ? dy : 0,
            PVP = decimal.TryParse(GetValue("PVP"), out var pvp) ? pvp : 0
        };
    }

    public async Task SalvarFiiAsync(Fii fii)
    {
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"FII#{fii.Ticker.ToUpper()}" } },
                { "SK", new AttributeValue { S = "METRICAS" } },
                { "Ticker", new AttributeValue { S = fii.Ticker } },
                { "Cotacao", new AttributeValue { N = fii.Cotacao.ToString() } },
                { "DividendYield", new AttributeValue { N = fii.DividendYield.ToString() } },
                { "PVP", new AttributeValue { N = fii.PVP.ToString() } }
            }
        };

        await _dynamoDb.PutItemAsync(request);
    }

    public async Task<dynamic> GetAsync(string pk, string sk)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = pk } },
                { "SK", new AttributeValue { S = sk } }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);

        // Retorna o dicionário bruto. 
        // O seu Handler fará o cast para Dictionary<string, AttributeValue>
        return response.Item;
    }

}