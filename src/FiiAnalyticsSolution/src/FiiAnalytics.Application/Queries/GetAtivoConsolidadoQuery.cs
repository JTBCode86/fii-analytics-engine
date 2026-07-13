using MediatR;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Application.Queries
{
    public class GetAtivoConsolidadoQuery : IRequest<FundoResponse>
    {
        public string Ticker { get; set; }

        public string UsuarioId { get; set; }

        public GetAtivoConsolidadoQuery(string ticker)
        {
            Ticker = ticker;
        }

        public GetAtivoConsolidadoQuery(string ticker, string usuarioId)
        {
            Ticker = ticker;
            UsuarioId = usuarioId;
        }
    }
}