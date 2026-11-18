# Tasks

## 1. Fundação de dados da fase

- [x] 1.1 Criar a migration aditiva `Fase2PreparacaoEntrada` com `Vagas`, `TarifasDiarias`, `ConfiguracaoTarifa`, `Atendimentos`, `VistoriasEntrada`, `Ocupacoes`, `EventosAtendimento`, `ResultadosIdempotentes`, `SEQUENCE` do identificador e `RowVersion` da conta; verificar com `dotnet ef migrations script` que não há `DROP`/`ALTER` destrutivo sobre tabelas da Fase 1.
- [x] 1.2 Mapear as entidades e configurações EF com FKs `NO ACTION`, checks de valor positivo, `decimal(18,2)` e projeções sem rastreamento; verificar com teste de integração que o esquema é criado sobre um catálogo vazio e que as contas/sessões da Fase 1 permanecem intactas.
- [x] 1.3 Criar os índices únicos filtrados de ocupação aberta por vaga e por atendimento e o índice único de identificador público; verificar com teste de integração concorrente que duas inserções não produzem duas ocupações abertas para a mesma vaga.
- [x] 1.4 Implementar a geração do identificador `AT-{anoUTC}-{sequência:D6}` e o índice único; verificar com teste de integração concorrente que valores são únicos, legíveis e distintos de chave primária.
- [x] 1.5 Adicionar `RowVersion` e token opaco de concorrência à conta sem alterar security stamp; verificar com teste de integração que o token muda após alteração de acesso e não é reutilizado como credencial.

## 2. Proteção de campos sensíveis

- [x] 2.1 Implementar `ICriptografiaCampos` e o envelope `[versão][kid][nonce 12][tag 16][ciphertext]` com AES-256-GCM, propósitos `documento` e `vistoria-entrada` e AAD reconstruído no servidor; verificar com vetores conhecidos, ida/volta e nonce novo por cifragem.
- [x] 2.2 Integrar a cifragem às colunas de documento e vistoria, sem serializar entidade ou texto puro; verificar com teste de unidade que nenhum DTO de lista/detalhe contém o campo cifrado ou decifrado.
- [x] 2.3 Implementar falha fechada para chave ausente, `kid` desconhecido, envelope inválido, tag adulterada e troca entre registro/campo/ambiente; verificar com testes de integração que a resposta e o log não expõem conteúdo sensível.
- [x] 2.4 Validar rotação sem recifragem e leitura de envelopes antigos após reinício; verificar com teste de integração usando duas versões de chave e reinício do provedor.

## 3. Idempotência de comandos

- [x] 3.1 Implementar a canonicalização versão 1 com ordem fixa, operação, ator, alvo, valores normalizados e versões-base, gerando SHA-256 binário; verificar com testes de unidade de determinismo, independência de formatação e hash divergente para conteúdo divergente.
- [x] 3.2 Criar a reserva de chave, o resultado mínimo e a unicidade `(Ator, Operação, Chave)` na transação do comando; verificar com teste de integração que efeitos e resultado são confirmados juntos ou revertidos.
- [x] 3.3 Implementar replay com reautorização, comparação pela versão persistida e `409` para conteúdo divergente; verificar com testes de integração de replay idêntico, divergente, ator diferente e sessão revogada.
- [x] 3.4 Tratar a disputa da mesma chave em contexto novo após rollback, sem retry cego; verificar com teste de integração com dois escritores simultâneos e um único efeito.
- [x] 3.5 Validar o cabeçalho `Idempotency-Key` como UUID obrigatório no check-in e mapear ausência/formato inválido para `400`; verificar com teste de contrato.

## 4. Administração de acessos

- [x] 4.1 Implementar criação e listagem paginada de funcionários com e-mail normalizado único, senha inicial mínima e perfis fixos, sem retornar credenciais; verificar com testes de unidade e contrato de `400/409/403`.
- [x] 4.2 Implementar alteração de perfis/estado com token de concorrência, incremento de versão de acesso, revogação de sessão/refresh e auditoria na mesma transação; verificar com teste de integração de conflito `409` e de revogação na requisição seguinte.
- [x] 4.3 Serializar a proteção do último administrador ativo com transação e leitura bloqueante; verificar com teste de integração concorrente que nunca resta zero administradores ativos.
- [x] 4.4 Criar a tela de funcionários conforme `prototypes/administracao-funcionarios.html`, com Signal Forms, resumo focado, `aria-invalid` e ações por perfil; verificar com Vitest que dados inválidos não chamam a API e que o estado negado aparece para Caixa/Manobrista.
- [x] 4.5 Cobrir em Playwright a criação de funcionário, alteração de acesso e recusa da requisição seguinte (inclusive replay de check-in) com sessão revogada; verificar paridade de estados e mensagens com o protótipo.

## 5. Vagas e disponibilidade

- [x] 5.1 Implementar cadastro e consulta paginada de vagas com código normalizado único, filtros por código/situação e situação derivada da ocupação aberta; verificar com testes de integração de duplicidade `409`, paginação e três perfis.
- [x] 5.2 Criar o serviço de polling de 30 s configurável com pausa em aba oculta, atualização no foco/após comando, cancelamento e trava contra sobreposição; verificar com Vitest que não há requisições concorrentes nem polling em aba oculta.
- [x] 5.3 Criar a tela de vagas conforme `prototypes/vagas.html`, incluindo cadastro exclusivo de administrador, filtros, paginação e recuperação de falha sem perder contexto; verificar com Vitest os estados vazio/carregando/erro e a preservação de filtros/página.
- [x] 5.4 Cobrir em Playwright a consulta pelos três perfis, o cadastro administrativo e a atualização após comando; verificar paridade de layout, estados e mensagens com o protótipo.

## 6. Tarifa versionada

- [x] 6.1 Implementar publicação imutável com valor positivo `decimal(18,2)`, rejeição de escala excedente, configuração singleton e concorrência por versão-base; verificar com testes de integração de publicação válida, valor inválido e duas publicações concorrentes.
- [x] 6.2 Implementar consulta da tarifa corrente (inclusive ausente) e histórico paginado ordenado, sem edição de versões passadas; verificar com testes de contrato e de integração que versões anteriores permanecem inalteradas.
- [x] 6.3 Criar a tela de tarifas conforme `prototypes/tarifas.html`, com estado sem tarifa, publicação, conflito de base e histórico recuperável; verificar com Vitest a validação sem chamada indevida e a atualização da versão corrente.
- [x] 6.4 Cobrir em Playwright a publicação e o bloqueio de perfis não administrativos; verificar paridade de estados e mensagens com o protótipo.

## 7. Check-in e snapshot

- [x] 7.1 Implementar `RegistrarEntrada` com reautorização, normalização, leitura da tarifa sob bloqueio compartilhado, snapshot de versão/valor, ocupação exclusiva, vistoria cifrada, eventos e resultado idempotente na mesma transação; verificar com testes de unidade de normalização/limites e de integração de rollback por falha de auditoria.
- [x] 7.2 Implementar o DTO de entrada sem campos do servidor, rejeição de propriedades desconhecidas, `201` com `Location` e resultado mínimo, e `422` sem tarifa; verificar com testes de contrato que tarifa/horário/operador/identificador do cliente resultam em `400`.
- [x] 7.3 Verificar concorrência de check-in na mesma vaga, vaga inexistente e indisponibilidade de tarifa com escritores independentes; confirmar um único `201`, `409`/`422` explícitos e nenhuma ocupação órfã.
- [x] 7.4 Criar o formulário de check-in conforme `prototypes/check-in.html`, com Signal Forms, validação acessível, seletor de vaga paginado e confirmação com identificador copiável; verificar com Vitest que dados inválidos não chamam a API e que documento/vistoria são limpos após sucesso.
- [x] 7.5 Implementar o coordenador de comando em memória com estados pendente/incerto, bloqueio de rota/logout, aviso de saída, replay com a mesma chave/conteúdo e nova chave após rejeição definitiva; verificar com Vitest os cinco estados e a ausência de persistência em storage.
- [x] 7.6 Cobrir em Playwright check-in, conflito de vaga, perda de resposta com replay e limpeza de campos, em duas origens HTTPS; verificar paridade de estados e mensagens com o protótipo.

## 8. Busca, detalhe e leituras sensíveis

- [x] 8.1 Implementar busca exata por placa ou identificador com exatamente um filtro, paginação estável e visitas distintas; verificar com testes de integração de placa normalizada, duas visitas do mesmo veículo e filtros inválidos.
- [x] 8.2 Implementar detalhe e histórico projetados por perfil, omitindo documento/vistoria das listagens e tarifa/valores do DTO do Manobrista; verificar com testes de contrato que o JSON não contém os campos proibidos e que a API responde `403` sem depender do guard.
- [x] 8.3 Implementar leitura explícita de documento (Caixa/Admin) e vistoria (três perfis), com auditoria confirmada antes da decifragem e falha fechada; verificar com testes de integração de ordem auditoria→conteúdo, falha de auditoria, decifragem inválida e negativa ao Manobrista.
- [x] 8.4 Criar a tela de atendimentos conforme `prototypes/atendimentos.html`, com abas de busca, painel de detalhe, histórico por perfil, leitura sensível e botão copiar; verificar com Vitest que respostas tardias de busca/detalhe/leitura são descartadas após troca de seleção, fechamento, logout e destruição.
- [x] 8.5 Cobrir em Playwright busca por placa/identificador, projeções por perfil, leitura auditada, negativa e descarte de resposta fora de ordem; verificar paridade de estados e mensagens com o protótipo.

## 9. Contrato, acessibilidade e documentação

- [x] 9.1 Padronizar os novos erros em Problem Details com `code`/`correlationId`, sem PII, e manter `no-store` em sucesso e erro; verificar com testes de contrato e inspeção de logs.
- [x] 9.2 Atualizar o OpenAPI com os endpoints, DTOs, exemplos e respostas de erro da fase; verificar que o documento gerado não executa bootstrap e corresponde às respostas HTTP testadas.
- [x] 9.3 Validar acessibilidade automatizada das novas telas (labels, resumo focado, `aria-invalid`, teclado, foco em diálogo) e registrar navegador/dispositivo/tecnologia assistiva da revisão manual em `docs/fases/fase-2.md`; não declarar conformidade sem evidência.
- [x] 9.4 Atualizar `README.md` e a documentação da fase com endpoints, comandos de teste, configuração e limites conhecidos; verificar que os comandos documentados executam como escritos.

## 10. Verificação de integração e encerramento

- [x] 10.1 Executar `dotnet build app/backend/Estacionamento.slnx`, a suíte MSTest completa com SQL Server real, `npm test --prefix app/frontend` e Playwright em duas origens; registrar comandos, ambiente, resultados e limitações em `docs/fases/fase-2.md`.
- [x] 10.2 Auditar respostas, logs, trilha e protótipos quanto a PII/segredos; verificar que documento, vistoria, tarifa e eventos financeiros respeitam os perfis e que nenhum segredo aparece em artefato ou repositório.
- [x] 10.3 Executar `openspec validate --strict` e atualizar os itens F2-01–F2-06 do roadmap em `docs/tdd.md` somente para critérios efetivamente comprovados, registrando as evidências correspondentes.
- [x] 10.4 Revisar a coerência entre PRD, TDD, specs arquivadas e decisões tomadas na implementação; corrigir divergências documentais antes de encerrar a fase.

## Workflow follow-up

- Abrir Pull Request da branch `feat/fase-2-preparacao-entrada` para `main` com o template de `.github/pull_request_template.md`, relacionando RF/RNF, decisões do TDD, specs e evidências.
- Arquivar a mudança após revisão, aprovações e verificações exigidas, e validar o resultado arquivado.
