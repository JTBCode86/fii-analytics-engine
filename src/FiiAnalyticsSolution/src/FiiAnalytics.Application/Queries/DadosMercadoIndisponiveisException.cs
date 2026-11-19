namespace FiiAnalytics.Application.Queries;

public class DadosMercadoIndisponiveisException : Exception
{
    public DadosMercadoIndisponiveisException(string ticker)
        : base($"Cotação indisponível para o ativo {ticker}.")
    {
    }
}
