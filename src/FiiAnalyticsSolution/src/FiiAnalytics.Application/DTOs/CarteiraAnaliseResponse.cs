namespace FiiAnalytics.Application.DTOs
{
    public record CarteiraAnaliseResponse(
        [property: JsonDecimalFormat("F4")] decimal RentabilidadeGlobal,
        [property: JsonDecimalFormat("F2")] decimal TotalInvestido,
        [property: JsonDecimalFormat("F2")] decimal ValorAtual,
        [property: JsonDecimalFormat("F2")] decimal TotalProventos,
        List<AtivoAnaliseDto> Ativos,
        EstimativaProventosDto? EstimativaProventos
    );

    public record AtivoAnaliseDto(
        string Ticker,
        int Quantidade,

        [property: JsonDecimalFormat("F2")] decimal PrecoMedio,
        [property: JsonDecimalFormat("F2")] decimal CotacaoAtual,
        [property: JsonDecimalFormat("F2")] decimal PVP,
        [property: JsonDecimalFormat("F2")] decimal? DividendYield,
        [property: JsonDecimalFormat("F4")] decimal Performance,
        DateTimeOffset? CotacaoAtualizadaEm
    );

    /// <summary>Estimativa bruta a partir do dividend yield histórico, sem garantia de pagamentos futuros.</summary>
    public sealed record EstimativaProventosDto(
        [property: JsonDecimalFormat("F2")] decimal Anual,
        [property: JsonDecimalFormat("F2")] decimal MediaMensal,
        List<EstimativaProventoAtivoDto> Ativos
    );

    /// <summary>Estimativa anual e média mensal de proventos por ativo.</summary>
    public sealed record EstimativaProventoAtivoDto(
        string Ticker,
        [property: JsonDecimalFormat("F2")] decimal Anual,
        [property: JsonDecimalFormat("F2")] decimal MediaMensal
    );
}