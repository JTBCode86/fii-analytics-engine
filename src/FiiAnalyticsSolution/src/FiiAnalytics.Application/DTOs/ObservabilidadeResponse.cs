namespace FiiAnalytics.Application.DTOs;

/// <summary>Dados operacionais coletados para o usuário e para as filas compartilhadas.</summary>
public sealed record ObservabilidadeResponse(
    DateTimeOffset ConsultadoEm,
    IReadOnlyList<ImportacaoStatusResponse> Importacoes,
    IReadOnlyList<FilaObservabilidadeResponse> Filas);

/// <summary>Contagens aproximadas de mensagens de uma fila SQS.</summary>
public sealed record FilaObservabilidadeResponse(
    string Nome, int Disponiveis, int EmProcessamento, int Atrasadas);
