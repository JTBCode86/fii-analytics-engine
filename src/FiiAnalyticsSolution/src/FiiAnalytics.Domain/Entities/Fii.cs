namespace FiiAnalytics.Domain.Entities;

public class Fii
{
    public string Ticker { get; private set; }
    public string Nome { get; private set; }
    public decimal PrecoAtual { get; private set; }
    public decimal ValorPatrimonialPorCota { get; private set; }
    public decimal DividendoAcumulado12M { get; private set; }

    public Fii(string ticker, string nome, decimal precoAtual, decimal valorPatrimonialPorCota, decimal dividendoAcumulado12M)
    {
        if (string.IsNullOrWhiteSpace(ticker)) throw new ArgumentException("Ticker inválido.");
        Ticker = ticker.ToUpper().Trim();
        Nome = nome;
        PrecoAtual = precoAtual;
        ValorPatrimonialPorCota = valorPatrimonialPorCota;
        DividendoAcumulado12M = dividendoAcumulado12M;
    }

    // Fórmula: DY = (Dividendo 12M / Preço Atual) * 100
    public decimal CalcularDividendYield()
    {
        if (PrecoAtual <= 0) return 0;
        return (DividendoAcumulado12M / PrecoAtual) * 100;
    }

    // Fórmula: P/VP
    public decimal CalcularPvp()
    {
        if (ValorPatrimonialPorCota <= 0) return 0;
        return PrecoAtual / ValorPatrimonialPorCota;
    }

    // Regra de Negócio para o Report: Caro ou Barato
    public string ObterStatusValuation()
    {
        decimal pvp = CalcularPvp();
        if (pvp == 0) return "Sem Dados";
        if (pvp < 0.95m) return "Barato (Desconto)";
        if (pvp > 1.05m) return "Caro (Ágio)";
        return "Preço Justo";
    }
}