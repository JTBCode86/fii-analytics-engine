namespace FiiAnalytics.Application.DTOs;

public record CarteiraImportadaEvent(
    string UsuarioId,
    string S3Key,
    string BucketName,
    DateTime DataCriacao
);