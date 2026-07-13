namespace FiiAnalytics.Application.DTOs
{
    public record CarteiraAnaliseResponse(
        [property: JsonDecimalFormat("F4")] decimal RentabilidadeGlobal,
        [property: JsonDecimalFormat("F2")] decimal TotalInvestido,
        [property: JsonDecimalFormat("F2")] decimal ValorAtual,
        [property: JsonDecimalFormat("F2")] decimal TotalProventos,
        List<AtivoAnaliseDto> Ativos
    );

    public record AtivoAnaliseDto(
        string Ticker,
        int Quantidade,

        [property: JsonDecimalFormat("F2")] decimal PrecoMedio,
        [property: JsonDecimalFormat("F2")] decimal CotacaoAtual,
        [property: JsonDecimalFormat("F2")] decimal PVP,
        [property: JsonDecimalFormat("F2")] decimal DividendYield,
        [property: JsonDecimalFormat("F2")] decimal Performance
    );
}