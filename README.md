# Gestão de Estacionamento

Uma plataforma para organizar o atendimento de estacionamentos e serviços de valet: da entrada do veículo à entrega, com controle de vagas, cobrança e histórico das condições do carro.

> **Uso acadêmico:** utilize exclusivamente dados fictícios, sem documentos, placas ou fotos reais identificáveis.

## Por que este projeto?

Tickets perdidos, dificuldade para localizar veículos, cobrança manual e falta de registros das vistorias podem causar demora e divergências. O sistema reúne essas informações em um só lugar, com responsabilidades claras para a equipe e acompanhamento de cada atendimento.

O sistema atende **uma unidade** de estacionamento. A publicação acadêmica utiliza serviços gratuitos, sujeitos a cotas e possíveis interrupções, sem garantia de disponibilidade contínua.

## Funcionalidades

| Funcionalidade | O que você pode fazer |
| --- | --- |
| **Entrada do veículo** | Registrar placa, modelo, titular e observações obrigatórias da vistoria; atribuir uma vaga livre e gerar um identificador para exibir e copiar. |
| **Vagas e localização** | Consultar vagas livres/ocupadas e encontrar atendimentos por placa ou identificador, distinguindo visitas anteriores do mesmo veículo. |
| **Retirada organizada** | Acompanhar pedidos aguardando, em busca e entregues, com horários e responsáveis; conferir presencialmente a identidade do titular. |
| **Cobrança padronizada** | Calcular diárias de 24 horas iniciadas, com mínimo de uma diária, usando a tarifa da entrada. O valor é fixado no pedido de retirada. |
| **Pagamentos e ajustes** | Registrar pagamentos integrais em dinheiro, cartão ou Pix; acompanhar saldos e permitir descontos/estornos justificados por administrador. |
| **Vistoria e entrega** | Registrar observações de saída, verificar as condições para entrega e liberar a vaga uma única vez, preservando o histórico. |
| **Fotos das vistorias** | Acrescentar fotos opcionais de entrada e saída, mantendo o texto obrigatório e o acesso privado. |
| **Gestão e relatórios** | Consultar recebimentos, cobranças, pendências, atendimentos e ocupação por dia, semana e mês. |
| **Equipe e histórico** | Administrar acessos por função e acompanhar quem registrou cada ação relevante. |

### Perfis de acesso

- **Manobrista:** consulta veículos, vagas e fila, registra início da busca e vistoria de saída; não acessa dados financeiros ou documento completo.
- **Caixa/atendente:** registra entradas, pedidos, pagamentos e entregas, conforme as regras do atendimento.
- **Administrador:** gerencia equipe, vagas e tarifas, acompanha relatórios e registra ajustes e exceções justificadas.

### Regras de uso

- As observações textuais das vistorias de entrada e saída são obrigatórias; as fotos são opcionais.
- São permitidas até **5 fotos por vistoria**, com até **5 MiB por arquivo**, em JPEG, PNG ou WebP estáticos. As imagens são normalizadas e seus metadados removidos antes do armazenamento privado.
- Anexe fotos de entrada após salvar a vistoria e antes do pedido de retirada; fotos de saída, após salvar a vistoria e antes de confirmar a entrega.
- A ausência de fotos ou uma falha isolada do armazenamento não impede um atendimento textual válido.
- Exatas 24 horas de permanência correspondem a uma diária; qualquer fração além disso inicia a segunda. Alterações posteriores de tarifa não mudam a tarifa daquele atendimento.
- Pagamentos são **registros manuais**, sem processamento bancário ou confirmação automática de Pix/cartão.
- Não é permitida retirada por terceiro ou com titular ausente, inclusive por exceção administrativa.
- O sistema não inclui autoatendimento do cliente, múltiplas unidades, operação offline ou emissão fiscal integrada. Impressão e envio de ticket não são obrigatórios.

## Desenvolvimento local

### Dependências

| Dependência | Finalidade |
| --- | --- |
| **SDK .NET 10** | Executar, desenvolver e testar o backend no computador do desenvolvedor. |
| **Node.js LTS e npm compatíveis com Angular 22** | Instalar dependências e executar o frontend. Consulte a [matriz oficial de compatibilidade](https://angular.dev/reference/versions). |
| **Docker Engine e plugin Docker Compose** (`docker compose`) | Executar o SQL Server local por meio de `compose.yml`. |
| **Host compatível com SQL Server Linux em container** | As imagens oficiais têm suporte documentado em x86-64 Intel/AMD; não presumir compatibilidade com ARM/emulação. Reservar recursos para SQL Server, API e frontend. |
| **Navegador e terminal** | Utilizar a aplicação, testar o fluxo e executar as ferramentas de desenvolvimento. |
| **Certificados HTTPS locais confiáveis** | Executar frontend e API em origens HTTPS distintas e validar o acesso entre elas. |

Use versões compatíveis do Angular CLI, ferramentas EF e demais dependências de projeto. O SQL Server utiliza a edição **Developer, apenas para desenvolvimento/testes**, respeitando sua licença e requisitos.

**Não é necessário instalar o SQL Server diretamente no host.** O Compose cuida somente da infraestrutura; Angular e backend são executados fora dos containers para facilitar depuração, recarga automática e uso de User Secrets.

Contas Azure não são necessárias para o fluxo local. A integração com armazenamento de fotos exige conta e bucket Cloudflare R2; a publicação exige acesso à assinatura Azure. Não crie serviços pagos automaticamente.

### Configurações e Secrets necessários

Use **User Secrets do .NET em Development** para os segredos do backend e **`appsettings*.json` apenas para parâmetros não sensíveis**. User Secrets mantém valores fora do repositório, mas não os cifra.

| Segredo ou material protegido | Onde é necessário | Para que serve |
| --- | --- | --- |
| **Senha administrativa do SQL local** (`MSSQL_SA_PASSWORD`) | Preparação da infraestrutura | Inicializar a imagem SQL Server. Fornecer externamente ao Compose, nunca como valor literal no YAML. Não usar a conta administrativa como usuário normal da API. |
| **Conexão e credencial do banco local da API** | Backend em Development | Acessar somente o catálogo de desenvolvimento e aplicar suas migrations pendentes. Em Development, usar a conta administrativa local `sa` em User Secrets para que a migration crie o catálogo; nunca reutilizar essa credencial em Testing/Production. |
| **E-mails e senhas das contas sintéticas** | Inicialização local em Development | Configurar todas as contas dos três perfis em User Secrets. Não use credenciais padrão publicadas ou compartilhadas; não coloque `ContasSinteticas` em `appsettings.Development.json`. |
| **Chaves simétricas JWT HS256** | Autenticação | Assinar e validar tokens com chave aleatória de 32 bytes por versão. Manter uma ativa para emissão e anteriores apenas durante a validade dos tokens correspondentes, sem RSA ou certificado. Nunca entregar essas chaves ao frontend. |
| **Chaves AES-256 versionadas** | Cifragem de campos no backend e recuperação | Cifrar documento, textos de vistoria e justificativas com AES-GCM. Manter uma chave ativa para escrita e as anteriores para leitura enquanto dados ou backups dependerem delas, sem certificado PFX ou key ring de Data Protection. |
| **Access Key ID e Secret Access Key do R2** | Armazenamento de fotos | Acessar somente o bucket privado autorizado para o ambiente. Separe as credenciais de testes e publicação. |
| **Credenciais próprias de testes** | Integração/E2E | Preparar bancos e dados sintéticos isolados, sem reutilizar o banco Development nem carregar User Secrets pessoais em Testing. |
| **Chave ou senha de cifragem dos backups** | Rotina de cópias e exercícios de recuperação | Proteger as cópias locais de SQL, imagens e chaves; guardar o segredo de recuperação separadamente. |

Forneça valores próprios para cada ambiente. A tabela descreve os segredos necessários, sem divulgar credenciais ou valores de exemplo reutilizáveis.

**Exemplo de estrutura dos User Secrets locais do backend** — substitua os placeholders e corresponda os nomes às opções de configuração da API:

```json
{
  "ConnectionStrings": {
    "Estacionamento": "<CONEXAO_SQL_LOCAL_COM_USUARIO_E_SENHA_DA_API>"
  },
  "ContasSinteticas": {
    "Administrador": {
      "Email": "<EMAIL_SINTETICO_ADMIN>",
      "Senha": "<SENHA_EXCLUSIVA_DO_ADMINISTRADOR>"
    },
    "Caixa": {
      "Email": "<EMAIL_SINTETICO_CAIXA>",
      "Senha": "<SENHA_EXCLUSIVA_DO_CAIXA>"
    },
    "Manobrista": {
      "Email": "<EMAIL_SINTETICO_MANOBRISTA>",
      "Senha": "<SENHA_EXCLUSIVA_DO_MANOBRISTA>"
    }
  },
  "Autenticacao": {
    "Jwt": {
      "Chaves": {
        "v1": "<CHAVE_JWT_ALEATORIA_DE_32_BYTES_EM_BASE64>"
      }
    }
  },
  "ProtecaoDados": {
    "Chaves": {
      "v1": "<CHAVE_AES_ALEATORIA_DE_32_BYTES_EM_BASE64>"
    }
  },
  "ArmazenamentoFotos": {
    "R2": {
      "AccessKeyId": "<ACCESS_KEY_ID_DO_AMBIENTE>",
      "SecretAccessKey": "<SECRET_ACCESS_KEY_DO_AMBIENTE>"
    }
  }
}
```

O JSON é ilustrativo e contém **somente placeholders**. Não o salve com valores reais em `appsettings*.json` versionados. Cada chave AES ou JWT deve ter **32 bytes gerados com aleatoriedade criptográfica**, representados em Base64; Base64 não cifra nem protege a chave. São 32 bytes aleatórios antes da codificação, não uma senha ou string de 32 caracteres.

Toda a seção `ContasSinteticas`, incluindo e-mails e senhas, pertence a User Secrets em Development. Não crie essa seção em `appsettings.Development.json`; o exemplo acima é somente a estrutura externa de User Secrets, não um `appsettings*.json`. Os três identificadores de perfil são fixos. A API valida previamente as três entradas, e-mails aceitos/únicos após normalização e senhas presentes, sem registrar seus valores.

**Exemplo de `appsettings.Development.json` — parâmetros não sensíveis complementares aos User Secrets:**

```json
{
  "AllowedHosts": "localhost;127.0.0.1",
  "ConfiguracaoRegional": {
    "Cultura": "pt-BR",
    "FusoHorario": "America/Sao_Paulo",
    "Moeda": "BRL"
  },
  "Autenticacao": {
    "Jwt": {
      "Emissor": "gestao-estacionamento-development",
      "Audiencia": "gestao-estacionamento-api-development",
      "VersaoAtiva": "v1",
      "DuracaoAcessoMinutos": 15,
      "DuracaoSessaoHoras": 8,
      "ToleranciaRelogioSegundos": 30,
      "RotacaoChaveDias": 90
    }
  },
  "ProtecaoDados": {
    "VersaoAtiva": "v1"
  },
  "Cors": {
    "OrigensPermitidas": [
      "https://localhost:4200",
      "https://localhost:4300"
    ]
  },
  "Operacao": {
    "IntervaloPollingSegundos": 30
  },
  "ArmazenamentoFotos": {
    "Habilitado": true,
    "R2": {
      "Endpoint": "https://<ACCOUNT_ID>.r2.cloudflarestorage.com",
      "RegiaoAssinatura": "auto",
      "Bucket": "<BUCKET_PRIVADO_DE_DESENVOLVIMENTO>"
    },
    "Limites": {
      "MaximoFotosPorVistoria": 5,
      "TamanhoMaximoArquivoMiB": 5,
      "MaximoMegapixels": 20,
      "MaximoPixelsPorLado": 10000,
      "LadoMaiorSaidaPixels": 2560,
      "QualidadeWebp": 85,
      "TimeoutProcessamentoSegundos": 30
    },
    "Orcamento": {
      "FranquiaBytesMensal": 10737418240,
      "FranquiaOperacoesClasseAMensal": 1000000,
      "FranquiaOperacoesClasseBMensal": 10000000,
      "AvisoPercentual": 70,
      "BloqueioPercentual": 80
    },
    "RetencaoDias": 90,
    "DiasParaReconciliarOrfao": 1
  },
  "Retencao": {
    "DadosOperacionaisDias": 90,
    "HistoricoDias": 365,
    "LogsTecnicosDias": 30
  }
}
```

Este é um exemplo de **configuração não secreta**, com nomes destinados a corresponder às opções tipadas do backend; URLs/origens, bucket e nomes de propriedades devem ser ajustados ao ambiente e implementados de forma consistente com o TDD. Os dois endereços CORS são exemplos locais HTTPS, não garantem que o servidor de desenvolvimento use essas portas. As versões ativas `v1` devem corresponder às chaves com o mesmo identificador nos User Secrets. Não inclua `ContasSinteticas`, connection strings com senha, chaves JWT/AES ou Access Key/Secret Access Key do R2 neste arquivo.

Configure `Autenticacao:Jwt:VersaoAtiva` com o identificador da chave de emissão, por exemplo `v1`, em `appsettings*.json`; esse identificador não é segredo. Os valores de `Autenticacao:Jwt:Chaves:<versao>` ficam somente na configuração externa sensível. A assinatura é HS256; a mesma chave secreta permite emitir e validar, por isso não existe chave pública para distribuição. Na rotação de 90 dias, preserve as anteriores para validação até expirar o último token correspondente mais a tolerância de 30 segundos.

Configure `ProtecaoDados:VersaoAtiva` com o identificador da chave de escrita, por exemplo `v1`, em `appsettings*.json`; esse identificador não é segredo. Os valores de `ProtecaoDados:Chaves:<versao>` ficam somente na configuração externa sensível. Na rotação, acrescente uma nova chave/versão, preserve as anteriores e não reutilize identificadores com outro material. As chaves AES são exclusivas por ambiente e finalidade, sem reutilização da chave JWT ou dos backups.

A repetição segura dos comandos usa **SHA-256 do conteúdo canônico versionado**, sem segredo HMAC de idempotência e sem guardar o corpo da requisição. Isso não remove o HMAC-SHA-256 usado internamente pelo JWT HS256, cuja chave é independente. Autorização, transações e regras de replay continuam obrigatórias. Hash não anonimiza conteúdo previsível nem o protege contra descoberta por tentativa; esse risco é aceito apenas no escopo sintético.

Forneça `MSSQL_SA_PASSWORD` ao processo Compose e, em `Development`, use a mesma senha na conexão administrativa da API mantida em User Secrets; não reutilize essa credencial em `Testing` ou `Production`. Credenciais de Testing pertencem ao processo de testes; a chave/senha dos backups fica protegida separadamente do backend.

URL da API, origens permitidas, moeda/fuso, limites de upload, emissor/audiência JWT e identificação do bucket/endpoint R2 são configurações, não senhas. Nunca incluir chaves AES/JWT ou credenciais SQL/R2 no frontend.

**Atenção ao Compose:** ele não lê User Secrets automaticamente. Forneça ao processo Compose somente os valores de que ele precisa, sem copiar todo o perfil de segredos para o container. Não versione `.env` com credenciais, certificados privados ou `secrets.json`; não publique dumps de configuração ou segredos em logs.

Na publicação, forneça segredos por **variáveis de ambiente/App Settings do App Service**, sem dependência obrigatória de Key Vault. O acesso ao Azure SQL utiliza identidade gerenciada, sem senha do banco no cliente.

### Comandos de desenvolvimento

Os comandos abaixo foram verificados com .NET SDK **10.0.114**, Node **26.13.1**, npm **12.2.2**, Docker **29.9.3**, Docker Compose **5.7.1** e host **x86-64**. As versões exatas de pacotes estão fixadas em `global.json`, `app/backend/Directory.Packages.props`, `app/frontend/package.json` e `app/frontend/package-lock.json`; a imagem do SQL está fixada em `mcr.microsoft.com/mssql/server:2022-CU28-ubuntu-22.04`.

**1. Preparar a infraestrutura SQL**

Forneça as senhas ao processo, nunca a arquivos versionados. O exemplo usa uma variável de ambiente de curta duração no terminal:

```bash
export MSSQL_SA_PASSWORD='<SENHA_ADMINISTRATIVA_SQL_FORTE_E_EXCLUSIVA>'
docker compose up -d
docker compose ps                       # aguarde o estado healthy
```

Em `Development`, a conexão da API usa a conta `sa` local, fornecida por User Secrets, para que `Database.MigrateAsync()` crie o catálogo e aplique todo o esquema na primeira inicialização. **Não há etapa manual de preparação do banco**: um volume novo é configurado pela própria API. A conta `sa` é uma exceção restrita ao SQL local de desenvolvimento; a API não a utiliza em `Testing` nem em `Production`, e a publicação mantém identidade de migração controlada. Nunca use `sa` como credencial de runtime em ambiente publicado.

Para uma segunda instância isolada de testes, use projeto Compose, porta e volume próprios:

```bash
COMPOSE_PROJECT_NAME=gestao-estacionamento-test SQL_PORT=14330 \
  MSSQL_SA_PASSWORD='<SENHA_DA_INSTANCIA_DE_TESTE>' docker compose up -d
```

**2. Preencher os User Secrets do backend**

Crie `~/.microsoft/usersecrets/gestao-estacionamento-api-development/secrets.json` com permissão `600` e a estrutura abaixo. Este arquivo fica fora do repositório; User Secrets **não é cofre cifrado**. Gere cada chave com `openssl rand -base64 32` (32 bytes aleatórios antes do Base64) e não reutilize material entre JWT, AES ou backups.

```json
{
  "ConnectionStrings": {
    "Estacionamento": "Server=127.0.0.1,1433;Database=Estacionamento;User Id=sa;Password=<MSSQL_SA_PASSWORD>;Encrypt=True;TrustServerCertificate=True"
  },
  "ContasSinteticas": {
    "Administrador": { "Email": "<EMAIL_SINTETICO_ADMIN>", "Senha": "<SENHA_EXCLUSIVA_ADMIN>" },
    "Caixa": { "Email": "<EMAIL_SINTETICO_CAIXA>", "Senha": "<SENHA_EXCLUSIVA_CAIXA>" },
    "Manobrista": { "Email": "<EMAIL_SINTETICO_MANOBRISTA>", "Senha": "<SENHA_EXCLUSIVA_MANOBRISTA>" }
  },
  "Autenticacao": { "Jwt": { "Chaves": { "v1": "<CHAVE_JWT_ALEATORIA_DE_32_BYTES_EM_BASE64>" } } },
  "ProtecaoDados": { "Chaves": { "v1": "<CHAVE_AES_ALEATORIA_DE_32_BYTES_EM_BASE64>" } },
  "ArmazenamentoFotos": {
    "R2": {
      "AccessKeyId": "<ACCESS_KEY_ID_DO_R2>",
      "SecretAccessKey": "<SECRET_ACCESS_KEY_DO_R2>"
    }
  }
}
```

`chmod 600 ~/.microsoft/usersecrets/gestao-estacionamento-api-development/secrets.json`. A senha em `<MSSQL_SA_PASSWORD>` deve ser exatamente a mesma fornecida ao Compose; alterar a variável depois que o volume foi inicializado não altera a senha do SQL. Não use `appsettings*.json` para esses valores e não copie o arquivo para build/deploy. Essa conexão administrativa é exclusiva de `Development`; não a reutilize em `Testing` ou `Production`. As credenciais `ArmazenamentoFotos:R2:AccessKeyId` e `SecretAccessKey` também ficam em User Secrets; endpoint, região, bucket, limites e orçamento permanecem em `appsettings.Development.json`.

**3. Iniciar a API e a SPA no host, em origens HTTPS distintas**

```bash
# terminal 1 — API em https://localhost:4300
dotnet run --project app/backend/src/Estacionamento.Api --launch-profile https

# terminal 2 — SPA em https://localhost:4200
cd app/frontend
npm ci
npm start -- --ssl
```

A API expõe `GET /health` sem autenticação. Em `Development`, após validar configuração, conexão e as três contas sintéticas, a API executa `Database.MigrateAsync()` — criando o catálogo quando ele ainda não existe —, provisiona imediatamente as contas ausentes e só então aceita requisições. Falha em qualquer etapa interrompe a inicialização. `Testing` e `Production` nunca migram nem provisionam no startup.

Se o navegador não confiar no certificado local, execute `dotnet dev-certs https --trust` uma vez. O Playwright ignora o certificado local por configuração explícita; isso não altera a validação TLS entre SPA e API.

**4. Parar e diagnosticar sem perder dados**

```bash
docker compose stop          # para o container; volume e dados permanecem
docker compose up -d         # retoma com os dados existentes
docker compose ps            # verifica saúde
docker inspect --format '{{.State.Health.Status}}' gestao-estacionamento-sqlserver-1
```

`docker compose down` também preserva o volume nomeado. **Nunca use `docker compose down -v` para contornar erro de senha ou migration**; a remoção do volume é uma ação destrutiva explícita. Erros de senha em volume já inicializado não são corrigidos alterando `MSSQL_SA_PASSWORD`; trate a rotação separadamente.

**5. Testes**

```bash
# Unitários .NET
dotnet test app/backend/tests/Estacionamento.UnitTests

# Integração com SQL Server real em instância isolada
export ESTACIONAMENTO_TESTE_CONEXAO='Server=127.0.0.1,14330;Database=master;User Id=estacionamento_teste;Password=<SENHA_DE_TESTE>;Encrypt=True;TrustServerCertificate=True'
dotnet test app/backend/tests/Estacionamento.IntegrationTests

# SPA
cd app/frontend
npm test -- --run

# E2E entre https://localhost:4200 e https://localhost:4300
# Defina E2E_CONEXAO, E2E_JWT_CHAVE, E2E_AES_CHAVE e E2E_*_EMAIL/SENHA
# apontando para catálogo e contas sintéticas isolados.
npx playwright test

# Contrato real opcional com o R2 de teste (bucket privado e imagens sintéticas)
# Defina ESTACIONAMENTO_TESTE_R2=1, R2_API_ENDPOINT, R2_TEST_ACCESS_KEY_ID,
# R2_TEST_SECRET_ACCESS_KEY e ESTACIONAMENTO_R2_TESTE_BUCKET.
```

Os testes de integração criam catálogos `EstacionamentoTeste_*` e os removem ao final; eles validam o host local e não carregam User Secrets pessoais (`ESTACIONAMENTO_DESABILITAR_USER_SECRETS=1`). O E2E inicia a API em `Development` com credenciais explícitas e catálogo isolado, sem tocar no banco de desenvolvimento.

**6. Erros comuns**

| Sintoma | Verificação |
| --- | --- |
| API encerra antes de escutar | O log informa a etapa (`validacao`, `conexao`, `migracoes`, `provisionamento`) e o tipo da falha. Confira User Secrets, conexão, chaves Base64 de 32 bytes e as três contas. |
| SQL não fica saudável | `docker compose ps`, `docker compose logs sqlserver`; confirme `MSSQL_SA_PASSWORD` e recursos do host x86-64. |
| `Login failed for user 'sa'` | A senha da conexão de `Development` não corresponde à senha usada para inicializar o volume. Atualize o User Secret com o mesmo `MSSQL_SA_PASSWORD`; não apague o volume para contornar. |
| `CREATE DATABASE permission denied` | A conexão de `Development` não está usando a conta administrativa local. Confira `ConnectionStrings:Estacionamento` nos User Secrets. |
| CORS bloqueado no navegador | A origem exata da SPA deve estar em `Cors:OrigensPermitidas` (`https://localhost:4200`). Não use proxy de desenvolvimento nem `AllowAnyOrigin`. |
| Login retorna 429 | Limite de 5 requisições por minuto por IP. Aguarde a janela; não contorne o limite. |
| Recarga da SPA pede login novamente | Comportamento esperado: a sessão existe apenas em memória. |



### Fluxo de execução

1. Prepare as dependências, parâmetros não sensíveis, User Secrets e credenciais locais.
2. Inicie somente o SQL Server pelo Compose, aguarde a verificação de saúde e prepare o catálogo/usuário dedicado.
3. Inicie o backend no host em **Development**. Após validar a configuração de User Secrets, a inicialização aplica automaticamente as **migrations pendentes** e, **imediatamente após o sucesso delas**, provisiona somente as contas sintéticas ausentes. A API começa a aceitar requisições apenas após o provisionamento bem-sucedido.
4. Inicie o Angular no host e acesse a interface pelo navegador.
5. Execute integração/E2E em instância e catálogos de teste separados, sem tocar nos dados de desenvolvimento.

Falhas de banco, configuração, migration ou provisionamento interrompem a inicialização da API. Reinícios preservam o esquema e não duplicam nem atualizam e-mail, senha, perfis ou estado das contas existentes. Fora de Development, execute migrations pelas fixtures de teste ou pelo procedimento controlado de publicação; o startup não provisiona contas.

Pare a infraestrutura preservando seus volumes. Não remova dados para resolver erros de senha ou migration; a limpeza do ambiente de desenvolvimento exige ação explícita.

### Fase 3 — retirada e caixa

A Fase 3 acrescenta `/withdrawals` (fila, três perfis), `/withdrawals/new` (Caixa/Administrador, com lista de atendimentos em aberto) e `/cashier/:attendanceId` (Caixa/Administrador). O polling da fila usa **30 s** por padrão, configurável em `intervaloPollingRetiradasSegundos` de `app/frontend/src/environments/environment.ts`; a API mantém a paginação padrão de 20 e máxima de 100.

A tela de atendimentos e a tela de pedido listam os atendimentos ainda em aberto com situação da retirada, sem exigir identificador; `GET /api/v1/attendances?status=aberto&page=&pageSize=` entrega essa projeção aos três perfis, e a busca exata por placa/identificador continua como filtro opcional.

- O pedido exige titular presente, conferência aprovada/divergente e `Idempotency-Key`; não há rota de entrega, vistoria de saída, consumo de autorização ou liberação de vaga nesta fase.
- O caixa aceita acerto integral em dinheiro, cartão ou Pix; desconto e estorno são exclusivos do Administrador e exigem justificativa cifrada, lida somente por endpoint explícito e auditado.
- O detalhe e o histórico do atendimento incluem a retirada, mas o Manobrista continua sem finanças, conferência, autorizações, justificativas, documento completo ou vistoria.
- A migração `20261119160017_Fase3RetiradaCaixa` é aditiva e não altera dados das fases anteriores. Limitações e evidências estão em [Fase 3 — retirada e caixa](docs/fases/fase-3.md).

## Documentação

- [Requisitos do produto — PRD](docs/prd.md): escopo, jornadas, regras de negócio e critérios de aceite.
- [Desenho técnico — TDD](docs/tdd.md): arquitetura, configuração, segurança e infraestrutura local.
- [Sessão, autenticação e CORS](docs/sessao-autenticacao.md): contratos HTTP, claims, erros, rotação de chaves e bootstrap.
- [Fase 1 — fundação e infraestrutura local](docs/fases/fase-1.md): implementação, bootstrap, autenticação, sessão, evidências e limitações.
- [Fase 2 — preparação e entrada](docs/fases/fase-2.md): escopo, endpoints, verificações, acessibilidade e limitações.
- [Fase 3 — retirada e caixa](docs/fases/fase-3.md): pedido, fila, cobrança, autorizações, verificações e limitações.
- [Entrevista de levantamento](docs/entrevista.md): contexto inicial **fictício** que motivou o projeto.

## Tecnologias

[![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![SQL Server](https://img.shields.io/badge/Microsoft_SQL_Server-CC2927?style=for-the-badge&logo=mssqlserver&logoColor=white)](https://learn.microsoft.com/sql/sql-server/tutorials-for-sql-server/)
[![OpenAPI / Swagger](https://img.shields.io/badge/OpenAPI%20%2F%20Swagger-85EA2D?style=for-the-badge&logo=swagger&logoColor=black)](https://swagger.io/)
[![Entity Framework Core](https://img.shields.io/badge/Entity%20Framework%20Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![ASP.NET Core Identity](https://img.shields.io/badge/ASP.NET%20Core%20Identity-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/security/authentication/identity)
[![Node.js](https://img.shields.io/badge/Node.js-339933?style=for-the-badge&logo=nodedotjs&logoColor=white)](https://nodejs.org/)
[![Angular](https://img.shields.io/badge/Angular-DD0031?style=for-the-badge&logo=angular&logoColor=white)](https://angular.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind%20CSS-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![Lucide Icons](https://img.shields.io/badge/Lucide%20Icons-F56565?style=for-the-badge&logo=lucide&logoColor=white)](https://lucide.dev/)
