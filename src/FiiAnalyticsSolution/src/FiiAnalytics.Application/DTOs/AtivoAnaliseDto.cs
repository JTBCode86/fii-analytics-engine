public record AtivoAnaliseDto(
        string Ticker,
        int Quantidade,
        decimal PrecoMedio,
        decimal CotacaoAtual,
        decimal PVP,
        decimal DividendYield,
        decimal Performance // (CotacaoAtual / PrecoMedio) - 1
    );