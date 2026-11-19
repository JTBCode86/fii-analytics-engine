using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Application.Interfaces;

public interface IObservabilidadeService
{
    Task<ObservabilidadeResponse> ConsultarAsync(string usuarioId, CancellationToken cancellationToken);
}
