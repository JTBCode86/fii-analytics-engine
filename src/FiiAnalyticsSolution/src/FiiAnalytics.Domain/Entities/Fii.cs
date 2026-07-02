namespace FiiAnalytics.Domain.Entities;

public class Fii
{
    public Fii() { }

    public string Ticker { get; set; }
    public decimal Cotacao { get; set; }
    public decimal DividendYield { get; set; }
    public decimal PVP { get; set; }
}