using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;
using System.Globalization;

namespace FiiAnalytics.Infrastructure.Repositories;

public sealed class ImportacaoRepository(IAmazonDynamoDB dynamoDb) : IImportacaoRepository
{
    private const string TableName = "FiiAnalyticsDb";

    public async Task RegistrarAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken)
    {
        var pk = $"USER#{usuarioId}";
        var id = importacaoId.ToString();
        await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = TableName,
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["PK"] = new() { S = pk },
                            ["SK"] = new() { S = $"IMPORTACAO#{id}" },
                            ["Status"] = new() { S = "PENDENTE" },
                            ["AtualizadoEm"] = new() { S = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) }
                        },
                        ConditionExpression = "attribute_not_exists(PK)"
                    }
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = TableName,
                        Key = Chave(pk, "IMPORTACAO#CONTROLE"),
                        UpdateExpression = "SET UltimaSolicitada = :id",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":id"] = new() { S = id } }
                    }
                }
            ]
        }, cancellationToken);
    }

    public async Task<ImportacaoStatusResponse?> ObterAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken)
    {
        var result = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = Chave($"USER#{usuarioId}", $"IMPORTACAO#{importacaoId}"),
            ConsistentRead = true
        }, cancellationToken);
        if (result.Item == null || result.Item.Count == 0) return null;

        return Mapear(importacaoId, result.Item);
    }

    public async Task<IReadOnlyList<ImportacaoStatusResponse>> ListarRecentesAsync(string usuarioId, CancellationToken cancellationToken)
    {
        var request = new QueryRequest
        {
            TableName = TableName,
            ConsistentRead = true,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :prefixo)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = $"USER#{usuarioId}" },
                [":prefixo"] = new() { S = "IMPORTACAO#" }
            }
        };
        var importacoes = new List<ImportacaoStatusResponse>();
        do
        {
            var pagina = await dynamoDb.QueryAsync(request, cancellationToken);
            foreach (var item in pagina.Items ?? [])
            {
                if (!item.TryGetValue("SK", out var sk) || sk.S == null)
                    throw new InvalidOperationException("Identificador de importação inválido.");
                if (sk.S == "IMPORTACAO#CONTROLE") continue;
                if (!Guid.TryParse(sk.S["IMPORTACAO#".Length..], out var id))
                    throw new InvalidOperationException("Identificador de importação inválido.");
                importacoes.Add(Mapear(id, item));
            }
            request.ExclusiveStartKey = pagina.LastEvaluatedKey;
        } while (request.ExclusiveStartKey is { Count: > 0 });

        return importacoes.OrderByDescending(item => item.AtualizadoEm).Take(20).ToList();
    }

    private static ImportacaoStatusResponse Mapear(Guid importacaoId, Dictionary<string, AttributeValue> item)
    {
        if (!item.TryGetValue("Status", out var status) || string.IsNullOrEmpty(status.S) ||
            !item.TryGetValue("AtualizadoEm", out var atualizado) ||
            !DateTimeOffset.TryParse(atualizado.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var data))
            throw new InvalidOperationException("O estado da importação no DynamoDB é inválido.");

        return new ImportacaoStatusResponse(importacaoId, status.S, data,
            item.TryGetValue("Mensagem", out var mensagem) ? mensagem.S : null);
    }

    public async Task MarcarFalhaPublicacaoAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken)
    {
        await dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = TableName,
            Key = Chave($"USER#{usuarioId}", $"IMPORTACAO#{importacaoId}"),
            ConditionExpression = "attribute_exists(PK)",
            UpdateExpression = "SET #status = :status, AtualizadoEm = :atualizado, Mensagem = :mensagem",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new() { S = "FALHOU" },
                [":atualizado"] = new() { S = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
                [":mensagem"] = new() { S = "Não foi possível enviar a importação para processamento." }
            }
        }, cancellationToken);
    }

    private static Dictionary<string, AttributeValue> Chave(string pk, string sk) => new()
    {
        ["PK"] = new() { S = pk },
        ["SK"] = new() { S = sk }
    };
}
