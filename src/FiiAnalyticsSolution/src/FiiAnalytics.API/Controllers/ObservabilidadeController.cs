using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FiiAnalytics.API.Controllers;

[ApiController]
[Route("api/v1/observabilidade")]
public sealed class ObservabilidadeController(IObservabilidadeService service) : ControllerBase
{
    /// <summary>Consulta importações recentes do usuário e contagens aproximadas das filas compartilhadas.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ObservabilidadeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Consultar([FromHeader(Name = "X-Usuario-Id")] string usuarioId, CancellationToken cancellationToken)
    {
        var id = usuarioId?.Trim() ?? string.Empty;
        if (id.StartsWith("USER#", StringComparison.Ordinal)) id = id[5..];
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");
        return Ok(await service.ConsultarAsync(id, cancellationToken));
    }
}
