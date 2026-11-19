import { useEffect, useRef, useState, type FormEvent } from 'react'
import { CircleAlert, RefreshCw } from 'lucide-react'
import { obterObservabilidade, type ImportacaoStatus, type Observabilidade as Dados } from './api'

const nomesStatus: Record<ImportacaoStatus['status'], string> = {
  PENDENTE: 'Na fila',
  PROCESSANDO: 'Processando',
  RETENTANDO: 'Tentando novamente',
  CONCLUIDO: 'Concluída',
  FALHOU: 'Falhou',
  SUPERADA: 'Substituída',
}
const dataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })

export default function Observabilidade({ usuarioInicial }: { usuarioInicial: string }) {
  const [usuario, setUsuario] = useState(usuarioInicial)
  const [usuarioConsultado, setUsuarioConsultado] = useState('')
  const [dados, setDados] = useState<Dados | null>(null)
  const [erro, setErro] = useState('')
  const [carregando, setCarregando] = useState(false)
  const requisicao = useRef<AbortController | null>(null)

  useEffect(() => () => requisicao.current?.abort(), [])

  async function consultar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = usuario.trim()
    if (!id) {
      setErro('Informe o ID do usuário para consultar as importações.')
      return
    }
    requisicao.current?.abort()
    const controller = new AbortController()
    requisicao.current = controller
    setCarregando(true)
    setDados(null)
    setErro('')
    try {
      const resultado = await obterObservabilidade(id, controller.signal)
      if (!controller.signal.aborted) {
        setDados(resultado)
        setUsuarioConsultado(id)
      }
    } catch (error) {
      if (!controller.signal.aborted) setErro(error instanceof Error ? error.message : 'Erro ao consultar os dados operacionais.')
    } finally {
      if (!controller.signal.aborted) setCarregando(false)
    }
  }

  return (
    <div className="content observabilidade">
      <section className="intro">
        <div>
          <p className="eyebrow"><span /> OPERAÇÃO LOCAL</p>
          <h1>Observabilidade<span className="heading-dot">.</span></h1>
          <p>Acompanhe importações e filas para identificar pendências e falhas.</p>
        </div>
      </section>
      <section className="lookup card" aria-label="Consultar observabilidade">
        <div className="lookup-copy">
          <span className="section-icon"><RefreshCw size={19} /></span>
          <div><h2>Consultar dados operacionais</h2><p>As importações pertencem ao ID informado; as filas são compartilhadas.</p></div>
        </div>
        <form className="lookup-form" onSubmit={consultar}>
          <label className="sr-only" htmlFor="usuario-observabilidade">ID do usuário</label>
          <input id="usuario-observabilidade" value={usuario} onChange={(event) => setUsuario(event.target.value)} placeholder="ID do usuário" autoComplete="off" />
          <button className="button button-dark" disabled={carregando} type="submit">{carregando ? 'Consultando...' : 'Atualizar'}</button>
        </form>
      </section>
      {erro && <div className="notice notice-error" role="alert"><CircleAlert size={18} /><span>{erro}</span></div>}
      {carregando && <div className="operational-placeholder" role="status">Consultando dados operacionais...</div>}
      {!dados && !carregando && !erro && <div className="operational-placeholder">Informe um ID e atualize para consultar os dados.</div>}
      {dados && (
        <>
          <div className="section-heading">
            <div><p className="eyebrow">FILAS COMPARTILHADAS</p><h2>Mensagens por fila</h2></div>
            <span className="section-context">CONSULTADO EM {dataHora.format(new Date(dados.consultadoEm))}</span>
          </div>
          <p className="operational-note">Contagens aproximadas do SQS, não atualizadas automaticamente. Filas DLQ guardam mensagens que excederam as tentativas.</p>
          <div className="queue-grid">
            {dados.filas.map((fila) => (
              <article className="queue-card card" key={fila.nome}>
                <h3>{fila.nome}</h3>
                <dl>
                  <div><dt>Disponíveis</dt><dd>{fila.disponiveis}</dd></div>
                  <div><dt>Em processamento</dt><dd>{fila.emProcessamento}</dd></div>
                  <div><dt>Atrasadas</dt><dd>{fila.atrasadas}</dd></div>
                </dl>
              </article>
            ))}
          </div>
          <section className="assets card operational-history">
            <div className="panel-heading">
              <div><p className="eyebrow">IMPORTAÇÕES DO USUÁRIO</p><h2>Histórico recente</h2><p>Até 20 importações, ordenadas pela última atualização. Usuário: {usuarioConsultado}.</p></div>
            </div>
            {dados.importacoes.length === 0 ? (
              <div className="operational-placeholder">Nenhuma importação encontrada para este usuário.</div>
            ) : (
              <div className="table-scroll">
                <table>
                  <thead><tr><th>IMPORTAÇÃO</th><th>STATUS</th><th>ATUALIZADA EM</th><th>MENSAGEM</th></tr></thead>
                  <tbody>{dados.importacoes.map((item) => (
                    <tr key={item.importacaoId}>
                      <td>{item.importacaoId}</td>
                      <td><span className={`performance ${item.status === 'FALHOU' ? 'performance-negative' : ''}`}>{nomesStatus[item.status]}</span></td>
                      <td>{dataHora.format(new Date(item.atualizadoEm))}</td>
                      <td className="operational-message">{item.mensagem ?? '—'}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
            )}
          </section>
          <p className="operational-note">Concluída indica carteira salva; cotações podem estar em atualização. Interrupções abruptas podem deixar um status sem atualização.</p>
        </>
      )}
    </div>
  )
}
