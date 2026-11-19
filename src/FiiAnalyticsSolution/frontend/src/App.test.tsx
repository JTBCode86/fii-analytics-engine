import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => {
  vi.unstubAllGlobals()
  window.location.hash = ''
})

describe('carteira', () => {
  it('consulta a API e apresenta os valores e a distribuição reais', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      rentabilidadeGlobal: '0.1500',
      totalInvestido: '100.00',
      valorAtual: '110.00',
      totalProventos: '5.00',
      estimativaProventos: {
        anual: '9.35', mediaMensal: '0.78',
        ativos: [{ ticker: 'HGLG11', anual: '9.35', mediaMensal: '0.78' }],
      },
      ativos: [{
        ticker: 'HGLG11', quantidade: 10, precoMedio: '10.00',
        cotacaoAtual: '11.00', pvp: '1.00', dividendYield: '8.50', performance: '0.0667',
        cotacaoAtualizadaEm: '2026-01-10T12:00:00Z',
      }],
    }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByLabelText('ID do usuário'), '123')
    await user.click(screen.getByRole('button', { name: 'Consultar' }))

    expect(await screen.findAllByText('HGLG11')).toHaveLength(3)
    expect(screen.getByText('R$ 110,00')).toBeInTheDocument()
    expect(screen.getByText('15,00%')).toBeInTheDocument()
    expect(screen.getByText('8,50%')).toBeInTheDocument()
    expect(screen.getByText('6,67%')).toBeInTheDocument()
    expect(screen.getAllByText('R$ 9,35')).toHaveLength(2)
    expect(screen.getAllByText('R$ 0,78')).toHaveLength(2)
    expect(screen.getByText(/não um pagamento previsto para cada mês/)).toBeInTheDocument()
    expect(screen.queryByText('Não informada')).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/carteira/rentabilidade/123', undefined)
  })

  it('informa que a estimativa está indisponível quando falta o yield', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      rentabilidadeGlobal: '0', totalInvestido: '100', valorAtual: '110', totalProventos: '0',
      estimativaProventos: null,
      ativos: [{ ticker: 'HGLG11', quantidade: 1, precoMedio: '100', cotacaoAtual: '110',
        pvp: '1', dividendYield: null, performance: '0.1', cotacaoAtualizadaEm: null }],
    }), { status: 200 })))
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('ID do usuário'), '123')
    await user.click(screen.getByRole('button', { name: 'Consultar' }))
    expect(await screen.findByText(/dividend yield ausente ou inválido para HGLG11/)).toBeInTheDocument()
    expect(screen.getByText('Não informado')).toBeInTheDocument()
  })

  it('informa erro de mercado sem exibir valores fictícios', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ error: 'Cotação indisponível para o ativo HGLG11.' }),
      { status: 503 },
    )))
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByLabelText('ID do usuário'), '123')
    await user.click(screen.getByRole('button', { name: 'Consultar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Cotação indisponível para o ativo HGLG11.')
    expect(screen.queryByText('R$ 0,00')).not.toBeInTheDocument()
  })

  it('envia CSV com o ID e informa que a importação é assíncrona', async () => {
    const importacaoId = 'bcecb77e-3c45-4fd8-8580-1e5d8a6e31b8'
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(
        JSON.stringify({ sucesso: true, mensagem: 'Recebido', s3Key: 'carteira.csv', importacaoId }),
        { status: 202 },
      ))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        importacaoId, status: 'CONCLUIDO', atualizadoEm: '2026-01-10T12:00:00Z',
        mensagem: 'Carteira salva. As cotações podem estar em atualização.',
      }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByLabelText('ID do usuário'), '123')
    const arquivo = new File(['Ticker,Quantidade\nHGLG11,1'], 'carteira.csv', { type: 'text/csv' })
    await user.upload(screen.getByLabelText('Selecionar arquivo CSV'), arquivo)
    await user.click(screen.getByRole('button', { name: 'Enviar arquivo' }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Concluída'))
    expect(screen.getByRole('status')).toHaveTextContent('cotações podem estar em atualização')
    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/carteira/importar')
    expect(options.headers['X-Usuario-Id']).toBe('123')
    expect(options.body.get('file')).toBe(arquivo)
    expect(fetchMock.mock.calls[1][0]).toBe(`/api/v1/carteira/importacoes/${importacaoId}`)
  })

  it('não envia um CSV vazio', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<App />)

    fireEvent.change(screen.getByLabelText('ID do usuário'), { target: { value: '123' } })
    fireEvent.change(screen.getByLabelText('Selecionar arquivo CSV'), {
      target: { files: [new File([], 'carteira.csv', { type: 'text/csv' })] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Enviar arquivo' }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('CSV não vazio'))
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('explica o formato CSV e disponibiliza o exemplo', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByText('Como preparar o CSV'))
    expect(screen.getByText(/Quantidade inteira positiva/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Baixar CSV fictício' })).toHaveAttribute('href', '/carteira-ficticia.csv')
  })

  it('mostra importações e filas consultadas para o usuário', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      consultadoEm: '2026-01-10T12:00:00Z',
      importacoes: [{ importacaoId: 'bcecb77e-3c45-4fd8-8580-1e5d8a6e31b8', status: 'FALHOU', atualizadoEm: '2026-01-10T12:00:00Z', mensagem: 'Erro no processamento' }],
      filas: [{ nome: 'fii-importacao-dlq', disponiveis: 2, emProcessamento: 1, atrasadas: 0 }],
    }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('link', { name: 'Observabilidade' }))
    const consulta = await screen.findByRole('region', { name: 'Consultar observabilidade' })
    await user.type(within(consulta).getByLabelText('ID do usuário'), '123')
    await user.click(screen.getByRole('button', { name: 'Atualizar' }))

    expect(await screen.findByText('fii-importacao-dlq')).toBeInTheDocument()
    expect(screen.getByText('Falhou')).toBeInTheDocument()
    expect(screen.getByText('Erro no processamento')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/observabilidade', expect.objectContaining({ headers: { 'X-Usuario-Id': '123' } }))
  })

  it('exibe erro em vez de contagens inventadas quando a consulta falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })))
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByRole('link', { name: 'Observabilidade' }))
    const consulta = await screen.findByRole('region', { name: 'Consultar observabilidade' })
    await user.type(within(consulta).getByLabelText('ID do usuário'), '123')
    await user.click(screen.getByRole('button', { name: 'Atualizar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 503')
    expect(screen.queryByText('fii-importacao-dlq')).not.toBeInTheDocument()
  })
})
