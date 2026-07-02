using Microsoft.AspNetCore.Mvc;
using FiiAnalytics.Application.UseCases;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Application.DTOs;

namespace FiiAnalytics.API.Controllers;

[ApiController]
[Route("api/v1/carteira")]
public class CarteiraController : ControllerBase
{
    private readonly ImportarCarteiraUseCase _importarCarteiraUseCase;
    private readonly GetRentabilidadeCarteiraHandler _rentabilidadeHandler;

    public CarteiraController(
        ImportarCarteiraUseCase importarCarteiraUseCase,
        GetRentabilidadeCarteiraHandler rentabilidadeHandler)
    {
        _importarCarteiraUseCase = importarCarteiraUseCase;
        _rentabilidadeHandler = rentabilidadeHandler;
    }

    [HttpPost("importar")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(ImportarCarteiraOutput))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportarCarteira([FromHeader(Name = "X-Usuario-Id")] string usuarioId, IFormFile file)
    {
        if (string.IsNullOrEmpty(usuarioId))
            return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");

        if (file == null || file.Length == 0)
            return BadRequest("Nenhum arquivo válido foi enviado.");

        using var stream = file.OpenReadStream();
        var input = new ImportarCarteiraInput(usuarioId, file.FileName, stream);

        var resultado = await _importarCarteiraUseCase.ExecutarAsync(input);

        if (!resultado.Sucesso)
            return BadRequest(resultado);

        return Accepted(resultado);
    }

    [HttpGet("rentabilidade/{usuarioId}")]
    [ProducesResponseType(typeof(CarteiraAnaliseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRentabilidade(string usuarioId)
    {
        try
        {
            var query = new GetRentabilidadeCarteiraQuery(usuarioId);
            var resultado = await _rentabilidadeHandler.Handle(query);

            if (resultado == null)
                return NotFound(new { message = "Carteira não encontrada para este usuário." });

            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Erro ao processar análise da carteira.", detail = ex.Message });
        }
    }
}