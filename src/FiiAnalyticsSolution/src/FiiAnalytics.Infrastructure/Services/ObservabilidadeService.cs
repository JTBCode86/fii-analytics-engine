using Amazon.SQS;
using Amazon.SQS.Model;
using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;
using System.Globalization;

namespace FiiAnalytics.Infrastructure.Services;

public sealed class ObservabilidadeService(IImportacaoRepository importacoes, IAmazonSQS sqs) : IObservabilidadeService
{
    private static readonly string[] Filas =
        ["fii-importacao-queue", "fii-importacao-dlq", "scraper-queue", "scraper-dlq"];

    public async Task<ObservabilidadeResponse> ConsultarAsync(string usuarioId, CancellationToken cancellationToken)
    {
        var historico = await importacoes.ListarRecentesAsync(usuarioId, cancellationToken);
        var filas = await Task.WhenAll(Filas.Select(nome => ConsultarFilaAsync(nome, cancellationToken)));
        return new ObservabilidadeResponse(DateTimeOffset.UtcNow, historico, filas);
    }

    private async Task<FilaObservabilidadeResponse> ConsultarFilaAsync(string nome, CancellationToken cancellationToken)
    {
        var url = await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = nome }, cancellationToken);
        var resposta = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = url.QueueUrl,
            AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible", "ApproximateNumberOfMessagesDelayed"]
        }, cancellationToken);

        int Contagem(string chave)
        {
            if (resposta.Attributes == null || !resposta.Attributes.TryGetValue(chave, out var valor) ||
                !int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero < 0)
                throw new InvalidOperationException($"Contagem inválida para a fila {nome}.");
            return numero;
        }

        return new FilaObservabilidadeResponse(nome, Contagem("ApproximateNumberOfMessages"),
            Contagem("ApproximateNumberOfMessagesNotVisible"), Contagem("ApproximateNumberOfMessagesDelayed"));
    }
}
