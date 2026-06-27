using Microsoft.AspNetCore.Mvc;
using FiiAnalytics.Application.UseCases;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.API.Controllers;

[ApiController]
[Route("api/v1/carteira")]
public class CarteiraController : ControllerBase
{
    private readonly ImportarCarteiraUseCase _importarCarteiraUseCase;

    public CarteiraController(ImportarCarteiraUseCase importarCarteiraUseCase)
    {
        _importarCarteiraUseCase = importarCarteiraUseCase;
    }

    [HttpPost("importar")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(ImportarCarteiraOutput))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportarCarteira([FromHeader(Name = "X-Usuario-Id")] string usuarioId, IFormFile file)
    {
        if (string.IsNullOrEmpty(usuarioId))
        {
            return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("Nenhum arquivo válido foi enviado.");
        }

        // Abre o stream do arquivo recebido via HTTP
        using var stream = file.OpenReadStream();

        var input = new ImportarCarteiraInput(usuarioId, file.FileName, stream);

        var resultado = await _importarCarteiraUseCase.ExecutarAsync(input);

        if (!resultado.Sucesso)
        {
            return BadRequest(resultado);
        }

        // Retorna HTTP 202 (Accepted), pois o processamento real ocorrerá de forma assíncrona pela Lambda Python
        return Accepted(resultado);
    }
}