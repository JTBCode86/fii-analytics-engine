namespace FiiAnalytics.Domain.ValueObjects;

public class AtivoCarteira
{
    public string Ticker { get; private set; }
    public int Quantidade { get; private set; }
    public decimal PrecoMedio { get; private set; }

    public AtivoCarteira(string ticker, int quantidade, decimal precoMedio)
    {
        if (quantidade <= 0) throw new ArgumentException("A quantidade deve ser maior que zero.");
        if (precoMedio <= 0) throw new ArgumentException("O preço médio deve ser maior que zero.");

        Ticker = ticker.ToUpper().Trim();
        Quantidade = quantidade;
        PrecoMedio = precoMedio;
    }

    // Custo Total de Aquisição (Capital Aplicado)
    public decimal CalcularCustoTotal() => Quantidade * PrecoMedio;
}