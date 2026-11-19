using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Domain.Entities;
using FiiAnalytics.Domain.Interfaces;
using System.Globalization;

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
        var (pk, prefixo) = await ObterChaveCarteiraAsync(usuarioId);
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            ConsistentRead = true,
            KeyConditionExpression = "PK = :v_pk AND begins_with(SK, :prefixo)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":v_pk"] = new() { S = pk },
                [":prefixo"] = new() { S = prefixo }
            }
        };

        var listaCarteira = new List<Carteira>();
        do
        {
            var response = await _dynamoDb.QueryAsync(queryRequest);
            foreach (var item in response.Items ?? new List<Dictionary<string, AttributeValue>>())
            {
                string precoStr = item.TryGetValue("PrecoMedio", out var p)
                    ? (p.S ?? p.N ?? "0")
                    : "0";

                string provStr = item.TryGetValue("TotalProventos", out var pr)
                    ? (pr.S ?? pr.N ?? "0")
                    : "0";

                listaCarteira.Add(new Carteira
                {
                    Ticker = item.TryGetValue("Ticker", out var t) ? (t.S ?? "") : "",
                    Quantidade = item.TryGetValue("Quantidade", out var q) ? int.Parse(q.N ?? "0") : 0,
                    PrecoMedio = decimal.TryParse(precoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPreco)
                        ? parsedPreco : 0,
                    TotalProventos = decimal.TryParse(provStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedProv)
                        ? parsedProv : 0
                });
            }
            queryRequest.ExclusiveStartKey = response.LastEvaluatedKey;
        } while (queryRequest.ExclusiveStartKey != null && queryRequest.ExclusiveStartKey.Count > 0);

        var listaMetadados = new List<Fii>();
        var tickers = listaCarteira.Select(c => c.Ticker.ToUpperInvariant()).Distinct().ToList();
        foreach (var lote in tickers.Chunk(100))
        {
            var pendentes = new Dictionary<string, KeysAndAttributes>
            {
                [TableName] = new KeysAndAttributes
                {
                    Keys = lote.Select(ticker => new Dictionary<string, AttributeValue>
                    {
                        { "PK", new AttributeValue { S = $"ATIVO#{ticker}" } },
                        { "SK", new AttributeValue { S = "METADATA" } }
                    }).ToList()
                }
            };

            for (var tentativa = 0; pendentes.Count > 0; tentativa++)
            {
                if (tentativa == 6)
                    throw new InvalidOperationException("Não foi possível obter todos os metadados do DynamoDB.");

                var batchResponse = await _dynamoDb.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = pendentes });
                if (batchResponse.Responses != null && batchResponse.Responses.TryGetValue(TableName, out var items))
                    listaMetadados.AddRange(items.Select(MapearFii));

                pendentes = batchResponse.UnprocessedKeys ?? new Dictionary<string, KeysAndAttributes>();
                if (pendentes.Count > 0 && tentativa < 5)
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << tentativa)));
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

        DateTimeOffset? atualizadoEm = null;
        if (item.TryGetValue("AtualizadoEm", out var data))
        {
            if (!DateTimeOffset.TryParse(data.S, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidOperationException("Data de atualização do ativo inválida.");
            atualizadoEm = parsed;
        }

        return new Fii
        {
            // Se a PK existir, remove o prefixo, senão retorna vazio
            Ticker = item.TryGetValue("PK", out var pk) ? pk.S.Replace("ATIVO#", "") : string.Empty,
            Cotacao = decimal.TryParse(GetValue("Cotacao"), NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : 0,
            DividendYield = item.TryGetValue("DividendYield", out var dividendYield) &&
                decimal.TryParse(dividendYield.S ?? dividendYield.N, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy) && dy >= 0
                    ? dy : null,
            PVP = decimal.TryParse(GetValue("PVP"), NumberStyles.Any, CultureInfo.InvariantCulture, out var pvp) ? pvp : 0,
            AtualizadoEm = atualizadoEm
        };
    }

    public async Task SalvarFiiAsync(Fii fii)
    {
        if (!fii.DividendYield.HasValue || fii.DividendYield < 0)
            throw new InvalidOperationException("Dividend yield indisponível ou inválido.");
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"FII#{fii.Ticker.ToUpper()}" } },
                { "SK", new AttributeValue { S = "METRICAS" } },
                { "Ticker", new AttributeValue { S = fii.Ticker } },
                { "Cotacao", new AttributeValue { N = fii.Cotacao.ToString() } },
                { "DividendYield", new AttributeValue { N = fii.DividendYield.Value.ToString(CultureInfo.InvariantCulture) } },
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
        return response.Item;
    }

    public async Task<dynamic> ObterAtivoCarteiraAsync(string usuarioId, string ticker)
    {
        var (pk, prefixo) = await ObterChaveCarteiraAsync(usuarioId);
        return await GetAsync(pk, $"{prefixo}{ticker}");
    }

    private async Task<(string Pk, string Prefixo)> ObterChaveCarteiraAsync(string usuarioId)
    {
        var id = usuarioId.StartsWith("USER#", StringComparison.Ordinal) ? usuarioId[5..] : usuarioId;
        var controle = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            ConsistentRead = true,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"USER#{id}" },
                ["SK"] = new() { S = "IMPORTACAO#CONTROLE" }
            }
        });
        if (controle.Item != null && controle.Item.TryGetValue("CarteiraAtiva", out var versao))
        {
            if (string.IsNullOrWhiteSpace(versao.S))
                throw new InvalidOperationException("A versão ativa da carteira é inválida.");
            return ($"SNAPSHOT#{id}#{versao.S}", "ATIVO#");
        }
        return ($"USER#{id}", "CARTEIRA#");
    }

    public async Task<bool> UsuarioExisteAsync(string usuarioId)
    {
        var queryRequest = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "PK = :v_pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
        {
            { ":v_pk", new AttributeValue { S = usuarioId } }
        },
            Limit = 1
        };

        var response = await _dynamoDb.QueryAsync(queryRequest);

        // LOG DE DEBUG: Imprima o número de itens encontrados
        Console.WriteLine($"DEBUG: Busca por {usuarioId} retornou {response.Items.Count} itens.");

        return response.Items != null && response.Items.Count > 0;
    }

}