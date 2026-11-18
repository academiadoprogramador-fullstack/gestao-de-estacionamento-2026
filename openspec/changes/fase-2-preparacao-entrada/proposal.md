# Proposal

## Why

A Fase 1 entregou a fundação local executável, mas ainda não existe nenhum fluxo operacional: sem administração de acessos, vagas e tarifa não é possível iniciar um atendimento, e sem entrada atômica e consulta autorizada o valet não tem como registrar e localizar veículos com segurança. A Fase 2 fecha esse intervalo definido no PRD §5.1–5.3 e no TDD §4.1/§5.1–5.2, entregando a preparação da unidade e o check-in completo sobre a base de sessão, configuração e bootstrap já existente.

## What Changes

- **Administração de funcionários sem autorregistro:** criação por administrador com e-mail, senha inicial e conjunto não vazio de perfis fixos; listagem paginada; alteração de perfis e estado com token de concorrência próprio, preservando autoria, impedindo remover o último administrador ativo e revogando as sessões do funcionário na mesma transação.
- **Cadastro e consulta de vagas:** código único normalizado, cadastro restrito a administrador, consulta paginada por código/situação disponível aos três perfis, situação derivada da ocupação aberta, sem reserva de vaga pela tela e com polling padrão de 30 s, pausa em aba oculta, atualização após comando e ao recuperar foco, sem sobreposição.
- **Tarifa versionada:** publicação administrativa de versões imutáveis e positivas em `decimal(18,2)`, controle de concorrência por versão-base, configuração corrente podendo estar ausente, histórico paginado e bloqueio de entrada sem tarifa publicada; nenhum valor comercial inicial.
- **Entrada de atendimento (check-in):** comando exclusivo de caixa/administrador, com placa, modelo, titular, documento e vistoria textual obrigatórios, vaga livre e `Idempotency-Key`; horário, operador, identificador público e snapshot de tarifa são do servidor. Atendimento, vistoria cifrada, ocupação exclusiva, eventos e resultado idempotente são confirmados juntos ou integralmente desfeitos, com conflito explícito em disputa de vaga ou de versão.
- **Consulta de atendimentos:** busca exata normalizada por placa **ou** identificador, paginada e com visitas distintas, detalhe e histórico projetados por perfil, exclusão de documento e vistoria das listagens e do DTO do manobrista, leitura explícita e auditada de documento (caixa/administrador) e vistoria (três perfis) antes de retornar o conteúdo.
- **Proteção de campos sensíveis:** documento, vistoria de entrada e justificativas persistidos com AES-256-GCM em envelope versionado, nonce novo por cifragem, tag de 16 bytes e vínculo autenticado por propósito e registro; falha de chave, envelope ou tag impede leitura e escrita sem fallback em texto puro, e a rotação mantém leitura das versões anteriores.
- **Idempotência de comandos:** chave UUID por ator/operação, SHA-256 do conteúdo canônico versionado sem segredo nem corpo persistido; replay com a mesma chave e conteúdo reautoriza e devolve o resultado mínimo original, conteúdo diferente resulta em conflito, e a retenção acompanha o histórico.
- **Interface operacional acessível:** novas telas de funcionários, vagas, tarifas, check-in e atendimentos com Signal Forms, resumo de erros focado, `aria-invalid`, mensagens textuais, identificador copiável, descarte de respostas tardias, tentativa incerta somente em memória com bloqueio de rota/logout e aviso antes de sair do navegador.
- **Verificação da fase:** MSTest unitário e de integração com SQL Server real (concorrência de vaga, transações, criptografia, idempotência), Vitest para polling/validação/descarte de respostas e Playwright entre origens HTTPS distintas, incluindo perfis, revogação e replay.

**Fora do escopo desta fase:** pedido de retirada, fila, cálculo de diárias, pagamento, desconto, estorno, autorizações de exceção, vistoria de saída, entrega, liberação manual de vaga, relatórios, fotos, ticket, CI, Azure, IaC e CD. Nenhuma migração remove ou reescreve dados da Fase 1.

## Capabilities

### New Capabilities

- `administracao-acessos`: administração de funcionários e perfis fixos, credencial inicial, concorrência de alteração, proteção do último administrador ativo e revogação de sessões com auditoria.
- `vagas`: cadastro e consulta paginada de vagas com código único normalizado, situação derivada da ocupação e comportamento de atualização periódica sem reserva.
- `tarifas`: publicação e histórico de versões imutáveis de diária, configuração corrente sem valor padrão e concorrência de publicação.
- `atendimentos`: check-in atômico com identificador público, snapshot de tarifa, vistoria textual de entrada, ocupação exclusiva de vaga e tentativa incerta controlada no cliente.
- `consulta-atendimentos`: busca exata paginada, detalhe e histórico projetados por perfil, descarte de respostas tardias e leitura explícita e auditada de documento e vistoria.
- `protecao-campos-sensiveis`: cifragem autenticada de campos sensíveis com chaves versionadas, vínculo por propósito/registro, rotação e falha fechada.
- `idempotencia-comandos`: repetição segura de comandos críticos por SHA-256 de conteúdo canônico versionado, replay autorizado e conflito por conteúdo diferente.

### Modified Capabilities

Nenhuma. Os requisitos existentes de `autenticacao-sessoes`, `infraestrutura-local` e `shell-acesso-frontend` continuam válidos e são reutilizados: a revogação por alteração de acesso, a sessão em memória e a configuração por ambiente já estão especificadas e apenas ganham consumidores nesta fase.

## Impact

- **Backend:** novos módulos em `Estacionamento.Domain` (vagas, tarifa, atendimento, valores e regras de normalização), `Estacionamento.Application` (casos de uso, portas, autorização por ação, projeções) e `Estacionamento.Infrastructure` (mapeamentos EF, criptografia de campos, consultas, auditoria); `Estacionamento.Api` recebe controllers finos em `/api/v1` para funcionários, vagas, tarifas, atendimentos e leituras sensíveis.
- **Banco:** migração aditiva sobre a fundação da Fase 1 criando vagas, versões/configuração de tarifa, atendimentos, vistorias de entrada, ocupações, eventos de auditoria de negócio e resultados idempotentes, com índice único filtrado de ocupação aberta por vaga, unicidade por atendimento e `rowversion` onde há mutação concorrente.
- **Frontend:** novas funcionalidades em `app/frontend/src/app/features` (funcionários, vagas, tarifas, check-in e atendimentos), serviços de transporte por funcionalidade e componentes compartilhados acessíveis, reutilizando o shell, os guards, o interceptor e a sessão em memória da Fase 1.
- **Configuração:** nenhum novo segredo versionado; as chaves AES da Fase 1 passam a cifrar conteúdo real e a versão ativa é obrigatória antes de aceitar tráfego.
- **Dependências:** nenhuma dependência de runtime nova além das versões já fixadas; a verificação de compatibilidade continua conforme TDD §2.1.
- **Rastreabilidade:** RF-01–03, parte de RF-07/10/11 e RNF-01–05/07; PRD §5.1–5.3; TDD §4.1, §5.1–5.2, §6.2–6.4, §7.1 e F2-01–F2-06 do roadmap.
