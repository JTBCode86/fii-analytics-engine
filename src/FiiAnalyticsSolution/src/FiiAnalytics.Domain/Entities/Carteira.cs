using FiiAnalytics.Domain.ValueObjects;

namespace FiiAnalytics.Domain.Entities;

public class Carteira
{
    public string UsuarioId { get; private set; }
    public List<AtivoCarteira> Ativos { get; private set; }

    public Carteira(string usuarioId)
    {
        UsuarioId = usuarioId;
        Ativos = new List<AtivoCarteira>();
    }

    public void AdicionarAtivo(AtivoCarteira ativo)
    {
        // Regra para evitar duplicidade ou somar posições
        Ativos.Add(ativo);
    }

    public decimal CalcularCustoTotalDaCarteira() => Ativos.Sum(a => a.CalcularCustoTotal());

    // Calcula a Rentabilidade com base nas cotações de mercado atuais trazidas do DynamoDB
    public decimal CalcularRentabilidadeGlobal(Dictionary<string, Fii> fiisMercado)
    {
        decimal custoTotal = CalcularCustoTotalDaCarteira();
        if (custoTotal == 0) return 0;

        decimal valorAtualTotal = 0;

        foreach (var ativo in Ativos)
        {
            if (fiisMercado.TryGetValue(ativo.Ticker, out var fii))
            {
                valorAtualTotal += ativo.Quantidade * fii.PrecoAtual;
            }
            else
            {
                // Se não achar a cotação no momento, assume o preço médio de custo
                valorAtualTotal += ativo.CalcularCustoTotal();
            }
        }

        // Fórmula: ((Valor Atual / Custo Total) - 1) * 100 para percentual
        return ((valorAtualTotal / custoTotal) - 1) * 100;
    }
}