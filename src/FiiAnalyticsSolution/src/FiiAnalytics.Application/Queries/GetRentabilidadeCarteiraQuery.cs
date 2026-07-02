using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FiiAnalytics.Application.Queries
{
    // Record para imutabilidade e concisão
    public record GetRentabilidadeCarteiraQuery(string UsuarioId);
}
