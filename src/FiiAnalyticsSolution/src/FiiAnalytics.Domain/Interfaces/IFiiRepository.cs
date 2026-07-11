using FiiAnalytics.Domain.Entities;

namespace FiiAnalytics.Domain.Interfaces;

public interface IFiiRepository
{
    Task<Fii?> ObterPorTickerAsync(string ticker);
   
    Task SalvarFiiAsync(Fii fii);

    Task<(List<Carteira> Carteira, List<Fii> Metadados)> ObterCarteiraComMetadadosAsync(string usuarioId);

    Task<dynamic> GetAsync(string pk, string sk);
}