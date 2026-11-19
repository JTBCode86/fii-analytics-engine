using Microsoft.AspNetCore.Mvc;
using FiiAnalytics.Application.UseCases;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Application.DTOs;
using FiiAnalytics.Application.Interfaces;

namespace FiiAnalytics.API.Controllers;

[ApiController]
[Route("api/v1/carteira")]
public class CarteiraController : ControllerBase
{
    private readonly ImportarCarteiraUseCase _importarCarteiraUseCase;
    private readonly GetRentabilidadeCarteiraHandler _rentabilidadeHandler;
    private readonly GetAtivoConsolidadoHandler _ativoConsolidadoHandler;
    private readonly IImportacaoRepository _importacoes;

    public CarteiraController(
        ImportarCarteiraUseCase importarCarteiraUseCase,
        GetRentabilidadeCarteiraHandler rentabilidadeHandler,
        GetAtivoConsolidadoHandler ativoConsolidadoHandler,
        IImportacaoRepository importacoes)
    {
        _importarCarteiraUseCase = importarCarteiraUseCase;
        _rentabilidadeHandler = rentabilidadeHandler;
        _ativoConsolidadoHandler = ativoConsolidadoHandler;
        _importacoes = importacoes;
    }

    [HttpPost("importar")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(ImportarCarteiraOutput))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ImportarCarteira([FromHeader(Name = "X-Usuario-Id")] string usuarioId, IFormFile file, CancellationToken cancellationToken)
    {
        usuarioId = NormalizarUsuarioId(usuarioId);
        if (string.IsNullOrEmpty(usuarioId))
            return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");

        if (file == null || file.Length == 0)
            return BadRequest("Nenhum arquivo válido foi enviado.");

        using var stream = file.OpenReadStream();
        var input = new ImportarCarteiraInput(usuarioId, file.FileName, stream);

        var resultado = await _importarCarteiraUseCase.ExecutarAsync(input, cancellationToken);

        if (!resultado.Sucesso)
            return BadRequest(resultado);

        return AcceptedAtAction(nameof(ObterStatusImportacao), new { importacaoId = resultado.ImportacaoId }, resultado);
    }

    [HttpGet("importacoes/{importacaoId:guid}")]
    [ProducesResponseType(typeof(ImportacaoStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterStatusImportacao(
        [FromHeader(Name = "X-Usuario-Id")] string usuarioId, Guid importacaoId, CancellationToken cancellationToken)
    {
        usuarioId = NormalizarUsuarioId(usuarioId);
        if (string.IsNullOrEmpty(usuarioId))
            return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");

        var resultado = await _importacoes.ObterAsync(usuarioId, importacaoId, cancellationToken);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    [HttpGet("rentabilidade/{usuarioId}")]
    [ProducesResponseType(typeof(CarteiraAnaliseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetRentabilidade(string usuarioId)
    {
        var query = new GetRentabilidadeCarteiraQuery(usuarioId);
        var resultado = await _rentabilidadeHandler.Handle(query);

        if (resultado == null)
            return NotFound(new { message = "Carteira não encontrada para este usuário." });

        return Ok(resultado);
    }

    [HttpGet("ativo/{ticker}")]
    [ProducesResponseType(typeof(FundoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAtivoConsolidado(
        [FromHeader(Name = "X-Usuario-Id")] string usuarioId,
        string ticker)
    {

        if (string.IsNullOrEmpty(usuarioId))
            return BadRequest("O cabeçalho 'X-Usuario-Id' é obrigatório.");

        var query = new GetAtivoConsolidadoQuery(ticker, usuarioId);
        var resultado = await _ativoConsolidadoHandler.Handle(query, CancellationToken.None);

        if (resultado == null)
            return NotFound(new { message = $"Ativo {ticker} não encontrado." });

        return Ok(resultado);
    }

    private static string NormalizarUsuarioId(string? usuarioId)
    {
        var id = usuarioId?.Trim() ?? string.Empty;
        return id.StartsWith("USER#", StringComparison.Ordinal) ? id[5..] : id;
    }
}