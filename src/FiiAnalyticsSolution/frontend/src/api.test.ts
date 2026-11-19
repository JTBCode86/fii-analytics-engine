import { afterEach, expect, it, vi } from 'vitest'
import { obterCarteira } from './api'

afterEach(() => {
  vi.unstubAllGlobals()
})

it('rejeita valores inválidos retornados pela API em vez de mostrar zero', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    rentabilidadeGlobal: '0',
    totalInvestido: '100',
    valorAtual: 'não disponível',
    totalProventos: '0',
    ativos: [],
    estimativaProventos: null,
  }), { status: 200 })))

  await expect(obterCarteira('123')).rejects.toThrow('valor financeiro inválido')
})
