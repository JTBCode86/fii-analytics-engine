using MediatR;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.Application.Queries
{
    public class GetAtivoConsolidadoQuery : IRequest<FundoResponse>
    {
        public string Ticker { get; set; }

        public GetAtivoConsolidadoQuery(string ticker)
        {
            Ticker = ticker;
        }
    }
}