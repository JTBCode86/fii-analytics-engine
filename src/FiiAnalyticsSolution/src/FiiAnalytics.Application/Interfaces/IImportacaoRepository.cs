using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Application.Interfaces;

public interface IImportacaoRepository
{
    Task<IReadOnlyList<ImportacaoStatusResponse>> ListarRecentesAsync(string usuarioId, CancellationToken cancellationToken);
    Task RegistrarAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken);
    Task<ImportacaoStatusResponse?> ObterAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken);
    Task MarcarFalhaPublicacaoAsync(string usuarioId, Guid importacaoId, CancellationToken cancellationToken);
}
