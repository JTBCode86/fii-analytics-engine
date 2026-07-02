using FiiAnalytics.Domain.Interfaces;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Application.Queries
{
    public class GetRentabilidadeCarteiraHandler
    {
        private readonly IFiiRepository _repository;

        public GetRentabilidadeCarteiraHandler(IFiiRepository repository)
        {
            _repository = repository;
        }

        public async Task<CarteiraAnaliseResponse> Handle(GetRentabilidadeCarteiraQuery query)
        {
            // 1. Busca os dados consolidados do repositório
            var (carteira, metadados) = await _repository.ObterCarteiraComMetadadosAsync(query.UsuarioId);

            decimal totalInvestido = 0; // Soma do PrecoMedio * Quantidade (C_a)
            decimal valorAtual = 0;     // Soma da CotacaoAtual * Quantidade (V_a)
            var listaAtivos = new List<FiiAnalytics.Application.DTOs.AtivoAnaliseDto>();

            // 2. Processa cada ativo e calcula agregados
            foreach (var item in carteira)
            {
                var meta = metadados.FirstOrDefault(m => m.Ticker == item.Ticker);

                decimal custoAtivo = item.PrecoMedio * item.Quantidade;
                decimal valorAtivo = (meta?.Cotacao ?? item.PrecoMedio) * item.Quantidade;

                totalInvestido += custoAtivo;
                valorAtual += valorAtivo;

                listaAtivos.Add(new FiiAnalytics.Application.DTOs.AtivoAnaliseDto(
                    item.Ticker,
                    item.Quantidade,
                    item.PrecoMedio,
                    meta?.Cotacao ?? 0,
                    meta?.PVP ?? 0,
                    meta?.DividendYield ?? 0,
                    item.PrecoMedio > 0 ? (valorAtivo / custoAtivo) - 1 : 0
                ));
            }

            // 3. Cálculo da Fórmula 3: Rg = [(Sum(Va) + Sum(Dc)) / Sum(Ca)] - 1
            // *Assumindo Dc (Dividendos) como 0 por enquanto conforme escopo atual*
            decimal rentabilidadeGlobal = totalInvestido > 0 ? (valorAtual / totalInvestido) - 1 : 0;

            return new CarteiraAnaliseResponse(
                rentabilidadeGlobal,
                totalInvestido,
                valorAtual,
                0, // Total Dividendos
                listaAtivos
            );
        }
    }
}