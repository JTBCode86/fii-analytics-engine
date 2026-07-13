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
            var usuarioId = request.UsuarioId.StartsWith("USER#") ? request.UsuarioId : $"USER#{request.UsuarioId}";

            if (!await _repo.UsuarioExisteAsync(usuarioId))
            {
                throw new KeyNotFoundException($"Usuário {request.UsuarioId} não encontrado na base de dados.");
            }

            var taskMercado = _repo.GetAsync($"ATIVO#{ticker}", "METADATA");
            var taskCarteira = _repo.GetAsync(usuarioId, $"CARTEIRA#{ticker}");

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
            // 1. Verifica existência da chave
            if (item == null || !item.TryGetValue(key, out var val))
                return 0;

            // 2. Tenta processar o tipo 'N' (Number) - Preferencial no DynamoDB
            if (!string.IsNullOrEmpty(val.N))
            {
                if (decimal.TryParse(val.N, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var resultN))
                {
                    return resultN;
                }
            }

            // 3. Tenta processar o tipo 'S' (String), caso o número esteja guardado como texto
            if (!string.IsNullOrEmpty(val.S))
            {
                if (decimal.TryParse(val.S, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var resultS))
                {
                    return resultS;
                }
            }

            // Retorna 0 caso não seja um formato numérico válido
            return 0;
        }
    }
}