using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;

namespace FiiAnalytics.Application.UseCases;

public class ImportarCarteiraUseCase
{
    private readonly IStorageService _storageService;
    private readonly IQueueService _queueService;
    private readonly IImportacaoRepository _importacaoRepository;
    private const string BucketName = "fii-carteiras-bucket";
    private const string QueueUrl = "http://localstack:4566/000000000000/fii-importacao-queue"; // URL padrão do LocalStack

    public ImportarCarteiraUseCase(IStorageService storageService, IQueueService queueService, IImportacaoRepository importacaoRepository)
    {
        _storageService = storageService;
        _queueService = queueService;
        _importacaoRepository = importacaoRepository;
    }

    public async Task<ImportarCarteiraOutput> ExecutarAsync(ImportarCarteiraInput input, CancellationToken cancellationToken = default)
    {
        if (input.ArquivoStream == null || input.ArquivoStream.Length == 0)
        {
            return new ImportarCarteiraOutput(false, "O arquivo enviado está vazio.", string.Empty);
        }

        // 1. Gerar uma chave única para o S3 para evitar sobrescrever dados de outros uploads
        var importacaoId = Guid.NewGuid();
        string s3Key = $"carteiras/{input.UsuarioId}/{importacaoId}_{input.NomeArquivo}";

        // 2. Persistir o arquivo bruto no AWS S3 local via LocalStack
        await _storageService.UploadFileAsync(BucketName, s3Key, input.ArquivoStream);
        await _importacaoRepository.RegistrarAsync(input.UsuarioId, importacaoId, cancellationToken);

        // 3. Montar o payload do evento assíncrono para a Lambda Python
        var eventoFila = new CarteiraImportadaEvent(
            UsuarioId: input.UsuarioId,
            S3Key: s3Key,
            BucketName: BucketName,
            DataCriacao: DateTime.UtcNow,
            ImportacaoId: importacaoId
        );

        // 4. Publicar o evento no AWS SQS
        try
        {
            await _queueService.PublishEventAsync(QueueUrl, eventoFila);
        }
        catch
        {
            await _importacaoRepository.MarcarFalhaPublicacaoAsync(input.UsuarioId, importacaoId, CancellationToken.None);
            throw;
        }

        return new ImportarCarteiraOutput(true, "Arquivo recebido com sucesso. Processamento iniciado de forma assíncrona.", s3Key, importacaoId);
    }
}