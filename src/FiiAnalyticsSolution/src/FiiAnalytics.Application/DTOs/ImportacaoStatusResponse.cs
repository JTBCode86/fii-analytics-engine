namespace FiiAnalytics.Application.DTOs;

/// <summary>Estado da gravação de uma carteira importada.</summary>
public sealed record ImportacaoStatusResponse(
    Guid ImportacaoId,
    string Status,
    DateTimeOffset AtualizadoEm,
    string? Mensagem);
