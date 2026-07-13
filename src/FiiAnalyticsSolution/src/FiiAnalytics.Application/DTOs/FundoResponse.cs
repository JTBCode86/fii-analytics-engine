namespace FiiAnalytics.Application.DTOs
{
    public class FundoResponse
    {
        public string Ticker { get; set; }
        [property: JsonDecimalFormat("F2")] public decimal CotacaoAtual { get; set; }
        [property: JsonDecimalFormat("F2")] public decimal PrecoMedio { get; set; }
        [property: JsonDecimalFormat("F4")] public decimal Performance { get; set; }
    }
}
