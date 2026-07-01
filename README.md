# FII Analytics Engine

## Descrição
O **FII Analytics Engine** é um sistema de engenharia de dados *serverless* projetado para automação, extração e análise de indicadores financeiros de Fundos de Imobiliários (FIIs). O projeto centraliza a coleta de dados de mercado, processamento e persistência, utilizando uma arquitetura moderna baseada em eventos e escalabilidade em nuvem.

## 🏗️ Arquitetura do Projeto
O sistema foi desenhado para ser desacoplado e resiliente:

* **Ingestão:** Scripts de web scraping (Python/BeautifulSoup) via AWS Lambda.
* **Orquestração:** Infraestrutura provisionada como código com **LocalStack**, simulando serviços AWS localmente.
* **Armazenamento:** Persistência estruturada em **Amazon DynamoDB**, otimizado para consultas rápidas.
* **Pipeline:** Processamento assíncrono capaz de integrar com filas (SQS).

```mermaid
graph LR
    A[Trigger/Evento] --> B[AWS Lambda]
    B --> C[StatusInvest]
    B --> D[(Amazon DynamoDB)]
    B --> E[Amazon SQS]
```

## 🛠️ Tecnologias Utilizadas
* 🐍 **Linguagens:** Python 3.10+ (Data Engineering) e C#/.NET (API Service).
* ☁️ **Cloud/Infra:** AWS Lambda, Amazon DynamoDB, LocalStack.
* 🕸️ **Data Scraping:** BeautifulSoup, Requests.
* 🐳 **Containerização:** Docker & Docker Compose.
* 📜 **Infraestrutura:** Bash scripts (IaC) via `init-aws.sh`.

## Fluxo de Dados
1. 🎯 **Trigger:** O evento de coleta é disparado manualmente ou via regra agendada.
2. 🤖 **Scraping:** O worker (Lambda) extrai dados brutos do portal StatusInvest.
3. ⚙️ **Processamento:** O dado é limpo, tratado e estruturado pelo motor.
4. 💾 **Persistência:** Registro salvo no DynamoDB com o modelo de dados `PK: ATIVO#{ticker}`.

## 📝 Documentação da API (Swagger/OpenAPI)

O FII Analytics Engine expõe a estrutura de dados utilizada pelo sistema para integração com ferramentas externas ou consumo via client. Abaixo, a representação da entidade principal de dados (FII Metadata):

### Estrutura do Recurso: `Ativo`

| Campo | Tipo | Descrição |
| :--- | :--- | :--- |
| `PK` | String | Chave de Partição (ex: `ATIVO#HGLG11`) |
| `SK` | String | Chave de Ordenação (ex: `METADATA`) |
| `Ticker` | String | Código do fundo imobiliário |
| `Cotacao` | Number | Último valor de cotação extraído |
| `DividendYield` | Number | Percentual anualizado de dividendos |

> **Nota:** Para visualizar o esquema completo em formato OpenAPI, você pode importar o arquivo `docs/openapi.yaml` em ferramentas como [Swagger Editor](https://editor.swagger.io/) ou [Postman](https://www.postman.com/).

## 🚀 Passo a Passo para Execução

### 1. Pré-requisitos
* ✅ **Ambiente de Container:** Docker e Docker Desktop instalados e em execução.
* ✅ **Ferramentas de Cloud:** AWS CLI instalado e configurado.
* ✅ **Utilitários:** `awslocal` instalado (opcional, para facilitar comandos contra o LocalStack).
* ✅ **Desenvolvimento .NET:**
    * **SDK:** .NET 8.0 (ou superior) instalado.
    * **IDE:** Visual Studio 2022 ou Visual Studio Code (com a extensão *C# Dev Kit* recomendada).

### 2. Subir a Infraestrutura Local
No terminal, na raiz do projeto, execute o comando para iniciar os containers:
> 💻 `docker-compose up -d`

### 3. Monitorar o Provisionamento 
Para verificar se os serviços foram criados com sucesso, acompanhe os logs:
```bash
docker logs -f fiianalytics_localstack
```

Obs: Você pode reduzir a quantidade de logs, vendo apenas os últimos gerados:
```bash
docker logs fiianalytics_localstack --tail 20
```

Pode validar também toda a infraestrutura AWS criada:
```bash
docker exec -it fiianalytics_localstack bash -c "echo '--- S3 Buckets ---'; awslocal s3 ls; echo -e '\n--- SQS Queues ---'; awslocal sqs list-queues; echo -e '\n--- DynamoDB Tables ---'; awslocal dynamodb list-tables; echo -e '\n--- Lambda Functions ---'; awslocal lambda list-functions"
```

### 4. Executar um Teste de Scraping
Após o ambiente estar pronto, dispare uma invocação manual na Lambda para um ativo específico:
Obs: Se não tiver o awslocal, use: aws --endpoint-url=http://localhost:4566 lambda invoke ...

> 🧪 `awslocal lambda invoke --function-name scraper-ativos-lambda --payload '{"ticker": "HGLG11"}' response.json`

### 5. Validar a Persistência no DynamoDB
```bash
aws --endpoint-url=http://localhost:4566 dynamodb scan --table-name FiiAnalyticsDb
```

**⚠️ Nota de Configuração:** Certifique-se de que os arquivos de script e configuração (`init-aws.sh`, `docker-compose.yml` e `Dockerfile`) estejam salvos com codificação **UTF-8** e final de linha **LF (Unix)**. Arquivos salvos com codificação Windows (CRLF) podem causar erros de sintaxe ou falhas de execução dentro dos contêineres Linux.

💡 **Dica de CLI:** Ao executar comandos `aws dynamodb` via terminal Windows (CMD/PowerShell) contra o container, atente-se às aspas. O uso de `docker exec -it <container_nome> awslocal ...` requer tratamento específico de aspas no JSON. Em caso de erro de sintaxe, prefira listar os itens com `scan` ou utilize o PowerShell para garantir a correta interpretação do JSON.

---

## 🤝 Contribuições
Contribuições são bem-vindas! Sinta-se à vontade para abrir uma Issue ou enviar um Pull Request.
---
*Desenvolvido com foco em arquitetura de sistemas distribuídos, processamento de dados em Python e boas práticas de desenvolvimento .NET.*
