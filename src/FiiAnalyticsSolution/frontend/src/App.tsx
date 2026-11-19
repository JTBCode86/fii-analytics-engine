import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  ArrowDownRight, ArrowRight, ArrowUpRight, BarChart3, Building2,
  Activity, CheckCircle2, CircleAlert, FileUp, Layers3, LayoutDashboard,
  RefreshCw, Search, ShieldCheck, Wallet,
} from 'lucide-react'
import { importarCarteira, obterCarteira, obterStatusImportacao, type Carteira, type ImportacaoStatus } from './api'
import Observabilidade from './Observabilidade'

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const decimal = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const dataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
const cores = ['#b2d26f', '#3d7465', '#e7b976', '#849d97', '#d9997d', '#a8b7d1']
const nomesStatus: Record<ImportacaoStatus['status'], string> = {
  PENDENTE: 'Na fila',
  PROCESSANDO: 'Processando',
  RETENTANDO: 'Tentando novamente',
  CONCLUIDO: 'Concluída',
  FALHOU: 'Falhou',
  SUPERADA: 'Substituída por outra importação',
}

function porcentagem(value: number) {
  return `${decimal.format(value)}%`
}

function mensagem(error: unknown) {
  return error instanceof Error ? error.message : 'Ocorreu um erro inesperado. Tente novamente.'
}

function App() {
  const [pagina, setPagina] = useState(() => window.location.hash === '#observabilidade' ? 'observabilidade' : 'carteira')
  const [usuarioId, setUsuarioId] = useState('')
  const [usuarioConsultado, setUsuarioConsultado] = useState('')
  const [carteira, setCarteira] = useState<Carteira | null>(null)
  const [consultando, setConsultando] = useState(false)
  const [erroConsulta, setErroConsulta] = useState('')
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [erroEnvio, setErroEnvio] = useState('')
  const [importacao, setImportacao] = useState<{ usuarioId: string; id: string } | null>(null)
  const [statusImportacao, setStatusImportacao] = useState<ImportacaoStatus | null>(null)
  const [erroStatus, setErroStatus] = useState('')
  const [tentativaStatus, setTentativaStatus] = useState(0)
  const fileInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const navegar = () => setPagina(window.location.hash === '#observabilidade' ? 'observabilidade' : 'carteira')
    window.addEventListener('hashchange', navegar)
    return () => window.removeEventListener('hashchange', navegar)
  }, [])

  useEffect(() => {
    if (!importacao) return
    const atual = importacao
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    let ativo = true
    async function acompanhar() {
      try {
        const status = await obterStatusImportacao(atual.usuarioId, atual.id, controller.signal)
        if (!ativo) return
        setStatusImportacao(status)
        setErroStatus('')
        if (['PENDENTE', 'PROCESSANDO', 'RETENTANDO'].includes(status.status)) {
          timer = setTimeout(acompanhar, 5000)
        }
      } catch (error) {
        if (ativo) setErroStatus(mensagem(error))
      }
    }
    void acompanhar()
    return () => {
      ativo = false
      controller.abort()
      if (timer) clearTimeout(timer)
    }
  }, [importacao, tentativaStatus])

  async function consultar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = usuarioId.trim()
    if (!id) {
      setErroConsulta('Informe o ID do usuário para consultar a carteira.')
      return
    }

    setConsultando(true)
    setErroConsulta('')
    setCarteira(null)
    setUsuarioConsultado(id)
    try {
      setCarteira(await obterCarteira(id))
    } catch (error) {
      setErroConsulta(mensagem(error))
    } finally {
      setConsultando(false)
    }
  }

  async function enviar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setErroEnvio('')
    const id = usuarioId.trim()
    if (!id) {
      setErroEnvio('Informe o ID do usuário antes de importar.')
      return
    }
    if (!arquivo || !arquivo.name.toLowerCase().endsWith('.csv') || arquivo.size === 0) {
      setErroEnvio('Selecione um arquivo CSV não vazio.')
      return
    }

    setEnviando(true)
    try {
      const importacaoId = await importarCarteira(id, arquivo)
      setErroStatus('')
      setStatusImportacao(null)
      setImportacao({ usuarioId: id, id: importacaoId })
      setArquivo(null)
      if (fileInput.current) fileInput.current.value = ''
    } catch (error) {
      setErroEnvio(mensagem(error))
    } finally {
      setEnviando(false)
    }
  }

  const ativosOrdenados = carteira
    ? [...carteira.ativos].sort((a, b) => b.quantidade * b.cotacaoAtual - a.quantidade * a.cotacaoAtual)
    : []

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#inicio" aria-label="FII Analytics, início">
          <span className="brand-mark"><BarChart3 size={23} strokeWidth={2.2} /></span>
          <span><strong>fii<span className="brand-accent">.</span>analytics</strong><small>INTELIGÊNCIA EM FIIs</small></span>
        </a>
        <div className="sidebar-label">MENU PRINCIPAL</div>
        <nav className="navigation" aria-label="Navegação principal">
          <a className={pagina === 'carteira' ? 'nav-active' : ''} href="#resumo" aria-label="Visão geral"><LayoutDashboard size={18} /> Visão geral</a>
          <a href="#ativos"><Layers3 size={18} /> Meus ativos</a>
          <a href="#importar"><FileUp size={18} /> Importar carteira</a>
          <a className={pagina === 'observabilidade' ? 'nav-active' : ''} href="#observabilidade" aria-label="Observabilidade"><Activity size={18} /> Observabilidade</a>
        </nav>
        <div className="sidebar-footer">
          <div className="sidebar-footer-icon"><ShieldCheck size={18} /></div>
          <div><strong>Ambiente local</strong><span>Seus dados, no seu espaço.</span></div>
        </div>
      </aside>

      <main className="main" id="inicio">
        <header className="topbar">
          <div className="breadcrumb"><span>PAINEL</span><span className="breadcrumb-separator">/</span> {pagina === 'carteira' ? 'Visão geral' : 'Observabilidade'}</div>
          <div className="environment"><span className="environment-dot" /> AMBIENTE LOCAL</div>
        </header>

        {pagina === 'observabilidade' && <Observabilidade usuarioInicial={usuarioId} />}
        <div className="content" hidden={pagina === 'observabilidade'}>
          <section className="intro" id="resumo">
            <div>
              <p className="eyebrow"><span /> SUA CARTEIRA, MAIS CLARA</p>
              <h1>Seu patrimônio <em>em perspectiva.</em></h1>
              <p>Acompanhe seus fundos imobiliários e entenda os números da sua carteira em um só lugar.</p>
            </div>
            <div className="intro-stamp" aria-hidden="true"><Building2 size={26} /><span>INVESTIR<br />COM CLAREZA</span></div>
          </section>

          <section className="lookup card" aria-label="Consultar carteira">
            <div className="lookup-copy">
              <span className="section-icon"><Search size={19} /></span>
              <div><h2>Encontre sua carteira</h2><p>Informe o ID usado na importação para visualizar seus dados.</p></div>
            </div>
            <form className="lookup-form" onSubmit={consultar}>
              <label className="sr-only" htmlFor="usuario">ID do usuário</label>
              <input id="usuario" value={usuarioId} onChange={(event) => setUsuarioId(event.target.value)} placeholder="ID do usuário" autoComplete="off" />
              <button className="button button-dark" type="submit" disabled={consultando}>
                {consultando ? 'Consultando...' : 'Consultar'} <ArrowRight size={17} />
              </button>
            </form>
          </section>

          {erroConsulta && <div className="notice notice-error" role="alert"><CircleAlert size={18} /> <span>{erroConsulta}</span></div>}

          <div className="section-heading">
            <div><p className="eyebrow">VISÃO GERAL</p><h2>Os números da sua carteira<span className="heading-dot">.</span></h2></div>
            <span className="section-context">{usuarioConsultado ? `Usuário ${usuarioConsultado}` : 'CONSULTE PARA VISUALIZAR'}</span>
          </div>

          <section className="metrics" aria-label="Resumo da carteira" aria-busy={consultando}>
            <article className="metric metric-featured">
              <div className="metric-top"><span>Patrimônio atual</span><Wallet size={19} /></div>
              <strong>{carteira ? moeda.format(carteira.valorAtual) : '—'}</strong>
              <div className="metric-bottom"><span className="metric-pill"><span /> VALOR EM CARTEIRA</span><ArrowUpRight size={17} /></div>
            </article>
            <article className="metric card">
              <div className="metric-top"><span>Total investido</span><span className="metric-icon"><Layers3 size={18} /></span></div>
              <strong>{carteira ? moeda.format(carteira.totalInvestido) : '—'}</strong>
              <p>Custo de aquisição</p>
            </article>
            <article className="metric card">
              <div className="metric-top"><span>Rentabilidade</span><span className="metric-icon"><BarChart3 size={18} /></span></div>
              <strong className={carteira && carteira.rentabilidadeGlobal < 0 ? 'negative' : ''}>
                {carteira ? porcentagem(carteira.rentabilidadeGlobal * 100) : '—'}
              </strong>
              <p>Inclui proventos recebidos</p>
            </article>
            <article className="metric card">
              <div className="metric-top"><span>Proventos</span><span className="metric-icon"><ArrowDownRight size={18} /></span></div>
              <strong>{carteira ? moeda.format(carteira.totalProventos) : '—'}</strong>
              <p>Total acumulado</p>
            </article>
          </section>

          <section className="forecast card" aria-label="Estimativa de proventos">
            <div className="forecast-heading">
              <div><p className="eyebrow">PROVENTOS ESTIMADOS</p><h2>Uma projeção para sua carteira</h2></div>
              <span className="count-badge">ESTIMATIVA BRUTA</span>
            </div>
            {!carteira ? (
              <p>Consulte uma carteira para visualizar a estimativa.</p>
            ) : carteira.ativos.length === 0 ? (
              <p>A carteira não possui ativos para estimar proventos.</p>
            ) : !carteira.estimativaProventos ? (
              <p>Estimativa indisponível: dividend yield ausente ou inválido para {carteira.ativos.filter((ativo) => ativo.dividendYield === null).map((ativo) => ativo.ticker).join(', ') || 'um ou mais ativos'}. Atualize os dados e consulte novamente.</p>
            ) : (
              <>
                <div className="forecast-totals">
                  <div><span>Estimativa anual</span><strong>{moeda.format(carteira.estimativaProventos.anual)}</strong></div>
                  <div><span>Média mensal</span><strong>{moeda.format(carteira.estimativaProventos.mediaMensal)}</strong></div>
                </div>
                <div className="table-scroll">
                  <table>
                    <thead><tr><th>ATIVO</th><th>ESTIMATIVA ANUAL</th><th>MÉDIA MENSAL</th><th>DADOS ATUALIZADOS EM</th></tr></thead>
                    <tbody>{carteira.estimativaProventos.ativos.map((previsao) => {
                      const atualizado = carteira.ativos.find((ativo) => ativo.ticker === previsao.ticker)?.cotacaoAtualizadaEm
                      return <tr key={previsao.ticker}>
                        <td>{previsao.ticker}</td><td>{moeda.format(previsao.anual)}</td><td>{moeda.format(previsao.mediaMensal)}</td>
                        <td>{atualizado ? dataHora.format(new Date(atualizado)) : 'Não informada'}</td>
                      </tr>
                    })}</tbody>
                  </table>
                </div>
              </>
            )}
            <p className="forecast-note">Cálculo: quantidade × cotação × dividend yield dos últimos 12 meses. A média mensal é o total anual dividido por 12, não um pagamento previsto para cada mês. Valores brutos, sem garantia, sujeitos a alterações; não incluem impostos ou novas compras.</p>
          </section>

          <div className="dashboard-grid">
            <section className="assets card" id="ativos">
              <div className="panel-heading">
                <div><p className="eyebrow">COMPOSIÇÃO</p><h2>Seus ativos</h2><p>Uma visão dos fundos que fazem parte da carteira.</p></div>
                <span className="count-badge">{carteira ? `${carteira.ativos.length} ${carteira.ativos.length === 1 ? 'ativo' : 'ativos'}` : '—'}</span>
              </div>
              {consultando ? (
                <div className="empty-state" role="status"><RefreshCw className="spin" size={25} /><strong>Consultando sua carteira...</strong></div>
              ) : !carteira ? (
                <div className="empty-state"><span className="empty-icon"><Layers3 size={24} /></span><strong>Sua carteira começa aqui</strong><p>Consulte seu ID para visualizar os ativos e seus indicadores.</p></div>
              ) : carteira.ativos.length === 0 ? (
                <div className="empty-state"><span className="empty-icon"><Layers3 size={24} /></span><strong>Nenhum ativo encontrado</strong><p>Importe uma carteira ou confira se o ID informado está correto.</p></div>
              ) : (
                <div className="table-scroll">
                  <table>
                    <thead><tr><th>ATIVO</th><th>QUANTIDADE</th><th>PREÇO MÉDIO</th><th>COTAÇÃO</th><th>ATUALIZADA EM</th><th>DY</th><th>P/VP</th><th>PERFORMANCE</th></tr></thead>
                    <tbody>{ativosOrdenados.map((ativo, index) => (
                      <tr key={ativo.ticker}>
                        <td><div className="ticker"><span className="ticker-icon" style={{ backgroundColor: cores[index % cores.length] }}><Building2 size={16} /></span><strong>{ativo.ticker}</strong></div></td>
                        <td>{ativo.quantidade}</td>
                        <td>{moeda.format(ativo.precoMedio)}</td>
                        <td>{moeda.format(ativo.cotacaoAtual)}</td>
                        <td>{ativo.cotacaoAtualizadaEm ? dataHora.format(new Date(ativo.cotacaoAtualizadaEm)) : 'Não informada'}</td>
                        <td>{ativo.dividendYield !== null ? porcentagem(ativo.dividendYield) : 'Não informado'}</td>
                        <td>{decimal.format(ativo.pvp)}</td>
                        <td><span className={`performance ${ativo.performance < 0 ? 'performance-negative' : ''}`}>{porcentagem(ativo.performance * 100)}</span></td>
                      </tr>
                    ))}</tbody>
                  </table>
                </div>
              )}
            </section>

            <div className="side-panels">
              <section className="allocation card">
                <div className="panel-heading"><div><p className="eyebrow">DIVERSIFICAÇÃO</p><h2>Distribuição</h2><p>Participação no patrimônio atual.</p></div></div>
                {carteira && ativosOrdenados.length > 0 && carteira.valorAtual > 0 ? (
                  <>
                    <div className="allocation-bar" aria-hidden="true">{ativosOrdenados.map((ativo, index) => (
                      <span key={ativo.ticker} style={{ width: `${ativo.quantidade * ativo.cotacaoAtual / carteira.valorAtual * 100}%`, backgroundColor: cores[index % cores.length] }} />
                    ))}</div>
                    <ul className="allocation-list">{ativosOrdenados.map((ativo, index) => (
                      <li key={ativo.ticker}><span><i style={{ backgroundColor: cores[index % cores.length] }} />{ativo.ticker}</span><strong>{porcentagem(ativo.quantidade * ativo.cotacaoAtual / carteira.valorAtual * 100)}</strong></li>
                    ))}</ul>
                  </>
                ) : <div className="allocation-empty">A distribuição aparecerá depois da consulta.</div>}
              </section>

              <section className="import card" id="importar">
                <div className="import-icon"><FileUp size={20} /></div>
                <p className="eyebrow">MANTENHA ATUALIZADO</p>
                <h2>Importar carteira</h2>
                <p>Envie seu arquivo CSV para processar os ativos da sua carteira.</p>
                <details className="csv-help">
                  <summary>Como preparar o CSV</summary>
                  <p>Use UTF-8 e vírgulas. Colunas: <strong>Ticker, Quantidade, PrecoCompra</strong> (ou PrecoMedio) e <strong>TotalProventos</strong>.</p>
                  <code>Ticker,Quantidade,PrecoCompra,TotalProventos<br />HGLG11,5,160.25,8.50</code>
                  <p>Quantidade inteira positiva; decimais com ponto. A importação substitui toda a carteira.</p>
                  <a href={`${import.meta.env.BASE_URL}carteira-ficticia.csv`} download>Baixar CSV fictício</a>
                </details>
                <form onSubmit={enviar}>
                  <label className="file-label" htmlFor="arquivo"><FileUp size={18} /><span>{arquivo ? arquivo.name : 'Selecionar arquivo CSV'}</span></label>
                  <input ref={fileInput} id="arquivo" className="file-input" type="file" accept=".csv,text/csv" onChange={(event) => { setArquivo(event.target.files?.[0] ?? null); setErroEnvio('') }} />
                  <button className="button button-dark import-button" type="submit" disabled={enviando}>{enviando ? 'Enviando...' : 'Enviar arquivo'} <ArrowRight size={17} /></button>
                </form>
                {erroEnvio && <div className="notice notice-error compact" role="alert"><CircleAlert size={17} /><span>{erroEnvio}</span></div>}
                {importacao && !erroStatus && (
                  <div className={`notice compact ${statusImportacao?.status === 'FALHOU' || statusImportacao?.status === 'SUPERADA' ? 'notice-error' : 'notice-success'}`} role="status">
                    {statusImportacao?.status === 'CONCLUIDO' ? <CheckCircle2 size={17} /> :
                      statusImportacao?.status === 'FALHOU' || statusImportacao?.status === 'SUPERADA' ? <CircleAlert size={17} /> : <RefreshCw size={17} />}
                    <span>
                      <strong>Importação de {importacao.usuarioId}: {statusImportacao ? nomesStatus[statusImportacao.status] : 'Consultando status'}</strong>
                      {statusImportacao?.mensagem && <>. {statusImportacao.mensagem}</>}
                      {statusImportacao && ['PENDENTE', 'PROCESSANDO', 'RETENTANDO'].includes(statusImportacao.status) &&
                        Date.now() - Date.parse(statusImportacao.atualizadoEm) > 7 * 60 * 1000 &&
                        <> Sem atualização recente; verifique os logs e a fila de falhas.</>}
                    </span>
                  </div>
                )}
                {erroStatus && <div className="notice notice-error compact" role="alert"><CircleAlert size={17} /><span>Não foi possível acompanhar a importação: {erroStatus} <button type="button" className="retry-button" onClick={() => setTentativaStatus((valor) => valor + 1)}>Tentar novamente</button></span></div>}
                <p className="import-hint">O processamento acontece em segundo plano.</p>
              </section>
            </div>
          </div>
          <footer className="footer"><span>FII Analytics <span className="footer-dot">●</span> Seu patrimônio, com mais clareza.</span><span>FEITO PARA ACOMPANHAR SUA JORNADA</span></footer>
        </div>
      </main>
    </div>
  )
}

export default App
