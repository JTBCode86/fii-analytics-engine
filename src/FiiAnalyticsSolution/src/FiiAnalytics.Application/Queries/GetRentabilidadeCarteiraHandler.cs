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

            if (carteira == null || metadados == null)
                throw new InvalidOperationException("O repositório retornou dados incompletos da carteira.");

            decimal totalInvestido = 0;
            decimal valorAtual = 0;
            decimal totalProventos = 0;
            var listaAtivos = new List<FiiAnalytics.Application.DTOs.AtivoAnaliseDto>();
            var estimativas = new List<EstimativaProventoAtivoDto>();
            decimal estimativaAnual = 0;
            bool estimativaCompleta = true;

            // 4. Processa cada ativo e calcula agregados
            foreach (var item in carteira)
            {
                var meta = metadados.FirstOrDefault(m => m.Ticker == item.Ticker);
                if (meta == null || meta.Cotacao <= 0)
                    throw new DadosMercadoIndisponiveisException(item.Ticker);

                decimal custoAtivo = item.PrecoMedio * item.Quantidade;
                decimal valorAtivo = meta.Cotacao * item.Quantidade;
                decimal? dividendYield = meta.DividendYield is >= 0 ? meta.DividendYield : null;

                totalInvestido += custoAtivo;
                valorAtual += valorAtivo;
                totalProventos += item.TotalProventos;

                listaAtivos.Add(new FiiAnalytics.Application.DTOs.AtivoAnaliseDto(
                    item.Ticker,
                    item.Quantidade,
                    item.PrecoMedio,
                    meta.Cotacao,
                    meta.PVP,
                    dividendYield,
                    custoAtivo > 0 ? (valorAtivo / custoAtivo) - 1 : 0,
                    meta.AtualizadoEm
                ));
                if (dividendYield.HasValue)
                {
                    decimal anual = valorAtivo * dividendYield.Value / 100m;
                    estimativaAnual += anual;
                    estimativas.Add(new EstimativaProventoAtivoDto(item.Ticker, anual, anual / 12m));
                }
                else
                {
                    estimativaCompleta = false;
                }
            }

            // Rentabilidade global com Total Return (Valor Atual + Proventos)
            // Fórmula: ((Valor Atual + Proventos) / Total Investido) - 1
            decimal rentabilidadeGlobal = totalInvestido > 0
                ? ((valorAtual + totalProventos) / totalInvestido) - 1
                : 0;

            return new CarteiraAnaliseResponse(
                rentabilidadeGlobal,
                totalInvestido,
                valorAtual,
                totalProventos,
                listaAtivos,
                estimativaCompleta && listaAtivos.Count > 0
                    ? new EstimativaProventosDto(estimativaAnual, estimativaAnual / 12m, estimativas)
                    : null
            );
        }
    }
}