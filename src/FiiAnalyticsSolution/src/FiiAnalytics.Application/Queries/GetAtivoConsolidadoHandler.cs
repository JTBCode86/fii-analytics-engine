using MediatR;
using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Domain.Interfaces;
using Amazon.DynamoDBv2.Model;

namespace FiiAnalytics.Application.Queries
{
    public class GetAtivoConsolidadoHandler : IRequestHandler<GetAtivoConsolidadoQuery, FundoResponse>
    {
        private readonly IFiiRepository _repo; 

        public GetAtivoConsolidadoHandler(IFiiRepository repo)
        {
            _repo = repo;
        }

        public async Task<FundoResponse> Handle(GetAtivoConsolidadoQuery request, CancellationToken cancellationToken)
        {
            var ticker = request.Ticker.ToUpper();
            var taskMercado = _repo.GetAsync($"ATIVO#{ticker}", "METADATA");
            var taskCarteira = _repo.GetAsync("USER#123456", $"CARTEIRA#{ticker}");

            await Task.WhenAll(taskMercado, taskCarteira);

            // Converte os resultados para o formato que o DynamoDB devolve
            var mercado = taskMercado.Result as Dictionary<string, AttributeValue>;
            var carteira = taskCarteira.Result as Dictionary<string, AttributeValue>;
            var cotacao = ExtrairDecimal(mercado, "Cotacao");
            var precoMedio = ExtrairDecimal(carteira, "PrecoMedio");

            // Consolida e retorna o DTO
            return new FundoResponse
            {
                Ticker = ticker,
                CotacaoAtual = ExtrairDecimal(mercado, "Cotacao"),
                PrecoMedio = ExtrairDecimal(carteira, "PrecoMedio"),
                Performance = precoMedio > 0 ? (cotacao / precoMedio) - 1 : 0
            };
        }

        private decimal ExtrairDecimal(Dictionary<string, AttributeValue>? item, string key)
        {
            //if (item == null) return -999; // Dicionário nulo
            //if (!item.TryGetValue(key, out var val)) return -888; // Chave não existe
            //if (decimal.TryParse(val.N, out var result)) return result;
            //return -777; // Existe, mas não é número (ou está em .S)

            if (item == null || !item.TryGetValue(key, out var val))
                return 0; // Chave não existe

            // Tenta ler como Number (.N)
            if (val.N != null && decimal.TryParse(val.N, out var resultN))
                return resultN;

            // Tenta ler como String (.S)
            if (val.S != null && decimal.TryParse(val.S, out var resultS))
                return resultS;

            return 0; // Não conseguiu converter nem como N nem como S

        }   

    }
}