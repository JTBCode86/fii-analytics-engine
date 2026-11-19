export interface Ativo {
  ticker: string
  quantidade: number
  precoMedio: number
  cotacaoAtual: number
  pvp: number
  dividendYield: number | null
  performance: number
  cotacaoAtualizadaEm: string | null
}

export interface ImportacaoStatus {
  importacaoId: string
  status: 'PENDENTE' | 'PROCESSANDO' | 'RETENTANDO' | 'CONCLUIDO' | 'FALHOU' | 'SUPERADA'
  atualizadoEm: string
  mensagem: string | null
}

export interface Carteira {
  rentabilidadeGlobal: number
  totalInvestido: number
  valorAtual: number
  totalProventos: number
  ativos: Ativo[]
  estimativaProventos: EstimativaProventos | null
}

export interface EstimativaProventos {
  anual: number
  mediaMensal: number
  ativos: { ticker: string; anual: number; mediaMensal: number }[]
}

export interface FilaObservabilidade {
  nome: string
  disponiveis: number
  emProcessamento: number
  atrasadas: number
}

export interface Observabilidade {
  consultadoEm: string
  importacoes: ImportacaoStatus[]
  filas: FilaObservabilidade[]
}

function objeto(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('A API retornou dados em um formato inesperado.')
  }
  return value as Record<string, unknown>
}

function numero(value: unknown): number {
  if (
    (typeof value !== 'number' && typeof value !== 'string') ||
    (typeof value === 'string' && !/^-?\d+(?:\.\d+)?$/.test(value))
  ) {
    throw new Error('A API retornou um valor financeiro inválido.')
  }
  const parsed = Number(value)
  if (!Number.isFinite(parsed)) {
    throw new Error('A API retornou um valor financeiro inválido.')
  }
  return parsed
}

async function requisicao(path: string, init?: RequestInit): Promise<unknown> {
  let response: Response
  try {
    response = await fetch(path, init)
  } catch (error) {
    if (error instanceof TypeError) {
      throw new Error('Não foi possível conectar à API. Verifique se os serviços locais estão em execução.')
    }
    throw error
  }

  const text = await response.text()
  let body: unknown = null
  if (text) {
    try {
      body = JSON.parse(text)
    } catch (error) {
      if (!(error instanceof SyntaxError)) throw error
      if (response.ok) throw new Error('A API retornou uma resposta inválida.')
    }
  }

  if (!response.ok) {
    const details = body !== null && typeof body === 'object' && !Array.isArray(body)
      ? body as Record<string, unknown>
      : {}
    const message = details.error ?? details.message ?? details.mensagem
    throw new Error(typeof message === 'string' && message.trim()
      ? message
      : `Não foi possível concluir a operação (HTTP ${response.status}).`)
  }
  return body
}

export async function obterCarteira(usuarioId: string): Promise<Carteira> {
  const data = objeto(await requisicao(`/api/v1/carteira/rentabilidade/${encodeURIComponent(usuarioId)}`))
  if (!Array.isArray(data.ativos)) {
    throw new Error('A API retornou uma lista de ativos inválida.')
  }
  const ativos = data.ativos.map((value: unknown) => {
    const ativo = objeto(value)
    if (typeof ativo.ticker !== 'string' || !ativo.ticker.trim()) {
      throw new Error('A API retornou um ticker inválido.')
    }
    const quantidade = numero(ativo.quantidade)
    if (!Number.isInteger(quantidade) || quantidade < 0) {
      throw new Error('A API retornou uma quantidade inválida.')
    }
    const data = ativo.cotacaoAtualizadaEm
    if (data != null && (typeof data !== 'string' || Number.isNaN(Date.parse(data)))) {
      throw new Error('A API retornou uma data de cotação inválida.')
    }
    if (!('dividendYield' in ativo)) throw new Error('A API não informou o dividend yield.')
    return {
      ticker: ativo.ticker,
      quantidade,
      precoMedio: numero(ativo.precoMedio),
      cotacaoAtual: numero(ativo.cotacaoAtual),
      pvp: numero(ativo.pvp),
      dividendYield: ativo.dividendYield == null ? null : numero(ativo.dividendYield),
      performance: numero(ativo.performance),
      cotacaoAtualizadaEm: data ?? null,
    }
  })
  if (!('estimativaProventos' in data)) throw new Error('A API não informou a estimativa de proventos.')
  let estimativaProventos: EstimativaProventos | null = null
  if (data.estimativaProventos != null) {
    const estimativa = objeto(data.estimativaProventos)
    if (!Array.isArray(estimativa.ativos)) throw new Error('A API retornou uma estimativa de proventos inválida.')
    estimativaProventos = {
      anual: numero(estimativa.anual),
      mediaMensal: numero(estimativa.mediaMensal),
      ativos: estimativa.ativos.map((value: unknown) => {
        const ativo = objeto(value)
        if (typeof ativo.ticker !== 'string' || !ativo.ticker.trim())
          throw new Error('A API retornou um ticker de estimativa inválido.')
        return { ticker: ativo.ticker, anual: numero(ativo.anual), mediaMensal: numero(ativo.mediaMensal) }
      }),
    }
  }
  return {
    rentabilidadeGlobal: numero(data.rentabilidadeGlobal),
    totalInvestido: numero(data.totalInvestido),
    valorAtual: numero(data.valorAtual),
    totalProventos: numero(data.totalProventos),
    ativos,
    estimativaProventos,
  }
}

export async function importarCarteira(usuarioId: string, file: File): Promise<string> {
  const formData = new FormData()
  formData.append('file', file)
  const result = objeto(await requisicao('/api/v1/carteira/importar', {
    method: 'POST',
    headers: { 'X-Usuario-Id': usuarioId },
    body: formData,
  }))
  if (result.sucesso !== true) {
    throw new Error(typeof result.mensagem === 'string' ? result.mensagem : 'Não foi possível importar a carteira.')
  }
  if (typeof result.importacaoId !== 'string' || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(result.importacaoId)) {
    throw new Error('A API não retornou um identificador de importação válido.')
  }
  return result.importacaoId
}

export async function obterStatusImportacao(usuarioId: string, importacaoId: string, signal?: AbortSignal): Promise<ImportacaoStatus> {
  const data = interpretarImportacao(await requisicao(`/api/v1/carteira/importacoes/${encodeURIComponent(importacaoId)}`, {
    headers: { 'X-Usuario-Id': usuarioId },
    signal,
  }))
  if (data.importacaoId !== importacaoId) throw new Error('A API retornou um identificador de importação inválido.')
  return data
}

function interpretarImportacao(value: unknown): ImportacaoStatus {
  const data = objeto(value)
  const estados = ['PENDENTE', 'PROCESSANDO', 'RETENTANDO', 'CONCLUIDO', 'FALHOU', 'SUPERADA'] as const
  const status = estados.find((estado) => estado === data.status)
  if (typeof data.importacaoId !== 'string' || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(data.importacaoId) ||
      !status ||
      typeof data.atualizadoEm !== 'string' || Number.isNaN(Date.parse(data.atualizadoEm)) ||
      (data.mensagem != null && typeof data.mensagem !== 'string')) {
    throw new Error('A API retornou um estado de importação inválido.')
  }
  return {
    importacaoId: data.importacaoId,
    status,
    atualizadoEm: data.atualizadoEm,
    mensagem: data.mensagem ?? null,
  }
}

export async function obterObservabilidade(usuarioId: string, signal?: AbortSignal): Promise<Observabilidade> {
  const data = objeto(await requisicao('/api/v1/observabilidade', {
    headers: { 'X-Usuario-Id': usuarioId },
    signal,
  }))
  if (typeof data.consultadoEm !== 'string' || Number.isNaN(Date.parse(data.consultadoEm)) ||
      !Array.isArray(data.importacoes) || !Array.isArray(data.filas)) {
    throw new Error('A API retornou dados operacionais inválidos.')
  }
  const filas = data.filas.map((value: unknown) => {
    const fila = objeto(value)
    if (typeof fila.nome !== 'string' || !fila.nome.trim())
      throw new Error('A API retornou dados de fila inválidos.')
    function contagem(valor: unknown): number {
      if (typeof valor !== 'number' || !Number.isSafeInteger(valor) || valor < 0)
        throw new Error('A API retornou dados de fila inválidos.')
      return valor
    }
    return {
      nome: fila.nome,
      disponiveis: contagem(fila.disponiveis),
      emProcessamento: contagem(fila.emProcessamento),
      atrasadas: contagem(fila.atrasadas),
    }
  })
  return { consultadoEm: data.consultadoEm, importacoes: data.importacoes.map(interpretarImportacao), filas }
}
