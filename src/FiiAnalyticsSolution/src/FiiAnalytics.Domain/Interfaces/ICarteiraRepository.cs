using FiiAnalytics.Domain.Entities;

namespace FiiAnalytics.Domain.Interfaces;

public interface ICarteiraRepository
{
    Task<Carteira?> ObterPorUsuarioIdAsync(string usuarioId);
    Task SalvarCarteiraAsync(Carteira carteira);
}