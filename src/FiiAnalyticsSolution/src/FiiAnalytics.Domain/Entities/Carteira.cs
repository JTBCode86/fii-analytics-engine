using FiiAnalytics.Domain.ValueObjects;

namespace FiiAnalytics.Domain.Entities;

public class Carteira
{
    // Construtor vazio para desserialização
    public Carteira() { }
    public string Ticker { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoMedio { get; set; }

}