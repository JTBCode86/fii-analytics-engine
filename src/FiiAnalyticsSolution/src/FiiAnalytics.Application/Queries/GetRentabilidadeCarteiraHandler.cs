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
            // 1. Defesa de entrada: Normaliza o ID e garante que é válido
            var usuarioIdNormalizado = query.UsuarioId?.Trim();

            if (string.IsNullOrWhiteSpace(usuarioIdNormalizado))
                throw new ArgumentException("O ID do usuário é obrigatório.");

            // 2. Busca os dados consolidados usando o ID normalizado
            var (carteira, metadados) = await _repository.ObterCarteiraComMetadadosAsync(usuarioIdNormalizado);

            // 3. Cláusula de guarda: trata cenários onde a carteira ou metadados são nulos
            if (carteira == null || metadados == null)
            {
                return new CarteiraAnaliseResponse(0, 0, 0, 0, new List<FiiAnalytics.Application.DTOs.AtivoAnaliseDto>());
            }

            decimal totalInvestido = 0;
            decimal valorAtual = 0;
            var listaAtivos = new List<FiiAnalytics.Application.DTOs.AtivoAnaliseDto>();

            // 4. Processa cada ativo e calcula agregados
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
                    custoAtivo > 0 ? (valorAtivo / custoAtivo) - 1 : 0
                ));
            }

            // Rentabilidade global com proteção contra divisão por zero
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