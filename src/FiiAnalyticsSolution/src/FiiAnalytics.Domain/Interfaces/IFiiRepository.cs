using FiiAnalytics.Domain.Entities;

namespace FiiAnalytics.Domain.Interfaces;

public interface IFiiRepository
{
    Task<Fii?> ObterPorTickerAsync(string ticker);
    Task<Dictionary<string, Fii>> ObterTodosFiisAsync();
    Task SalvarFiiAsync(Fii fii);
}