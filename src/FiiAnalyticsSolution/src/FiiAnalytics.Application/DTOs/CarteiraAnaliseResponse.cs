namespace FiiAnalytics.Application.DTOs
{
    public record CarteiraAnaliseResponse(
        decimal RentabilidadeGlobal,
        decimal TotalInvestido,
        decimal ValorAtual,
        decimal TotalProventos,
        List<AtivoAnaliseDto> Ativos
    );

    public record AtivoAnaliseDto(
        string Ticker,
        int Quantidade,
        decimal PrecoMedio,
        decimal CotacaoAtual,
        decimal PVP,
        decimal DividendYield,
        decimal Performance
    );
}