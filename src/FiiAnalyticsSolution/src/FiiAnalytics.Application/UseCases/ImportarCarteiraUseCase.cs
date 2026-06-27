using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;

namespace FiiAnalytics.Application.UseCases;

public class ImportarCarteiraUseCase
{
    private readonly IStorageService _storageService;
    private readonly IQueueService _queueService;
    private const string BucketName = "fii-carteiras-bucket";
    private const string QueueUrl = "http://localstack:4566/000000000000/fii-importacao-queue"; // URL padrão do LocalStack

    public ImportarCarteiraUseCase(IStorageService storageService, IQueueService queueService)
    {
        _storageService = storageService;
        _queueService = queueService;
    }

    public async Task<ImportarCarteiraOutput> ExecutarAsync(ImportarCarteiraInput input)
    {
        if (input.ArquivoStream == null || input.ArquivoStream.Length == 0)
        {
            return new ImportarCarteiraOutput(false, "O arquivo enviado está vazio.", string.Empty);
        }

        // 1. Gerar uma chave única para o S3 para evitar sobrescrever dados de outros uploads
        string s3Key = $"carteiras/{input.UsuarioId}/{Guid.NewGuid()}_{input.NomeArquivo}";

        try
        {
            // 2. Persistir o arquivo bruto no AWS S3 local via LocalStack
            await _storageService.UploadFileAsync(BucketName, s3Key, input.ArquivoStream);

            // 3. Montar o payload do evento assíncrono para a Lambda Python
            var eventoFila = new CarteiraImportadaEvent(
                UsuarioId: input.UsuarioId,
                S3Key: s3Key,
                BucketName: BucketName,
                DataCriacao: DateTime.UtcNow
            );

            // 4. Publicar o evento no AWS SQS
            await _queueService.PublishEventAsync(QueueUrl, eventoFila);

            return new ImportarCarteiraOutput(true, "Arquivo recebido com sucesso. Processamento iniciado de forma assíncrona.", s3Key);
        }
        catch (Exception ex)
        {
            // Log do erro omitido para brevidade (Poderia usar um ILogger aqui)
            return new ImportarCarteiraOutput(false, $"Erro interno ao processar importação: {ex.Message}", string.Empty);
        }
    }
}