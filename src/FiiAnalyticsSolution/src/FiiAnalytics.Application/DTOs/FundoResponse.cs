using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FiiAnalytics.Application.DTOs
{
    public class FundoResponse
    {
        public string Ticker { get; set; }
        public decimal CotacaoAtual { get; set; }
        public decimal PrecoMedio { get; set; }
        
        public decimal LucroPrejuizo => CotacaoAtual - PrecoMedio;

        public decimal Performance { get; set; }

    }
}
