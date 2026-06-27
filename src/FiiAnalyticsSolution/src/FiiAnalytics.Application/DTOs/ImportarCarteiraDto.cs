namespace FiiAnalytics.Application.DTOs;

public record ImportarCarteiraInput(string UsuarioId, string NomeArquivo, Stream ArquivoStream);
public record ImportarCarteiraOutput(bool Sucesso, string Mensagem, string S3Key);