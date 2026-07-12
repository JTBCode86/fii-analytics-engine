namespace FiiAnalytics.Application.DTOs
{
    public class FundoResponse
    {
        public string Ticker { get; set; }
        public decimal CotacaoAtual { get; set; }
        public decimal PrecoMedio { get; set; }
        public decimal Performance { get; set; }
    }
}
