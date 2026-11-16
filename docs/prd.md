# PRD — Plataforma de Gestão de Estacionamento e Valet

**Status:** Base documental consolidada de requisitos do MVP  
**Data:** 05/11/2026  
**Revisão documental:** 16/11/2026  
**Origem:** solicitação de PRD baseada em `docs/entrevista.md`, contexto de `README.md` e esclarecimentos do solicitante nesta conversa.  
**Escopo:** MVP de uma unidade de valet/estacionamento.

**Contexto documental:** este PRD compõe a base documental do projeto, elaborada fora do fluxo de desenvolvimento. Reúne escopo, requisitos e critérios de aceite para implementação futura; não registra execução de código ou testes. A seção 12 separa decisões fechadas de verificações futuras.

**Natureza e responsabilidade:** base acadêmica, com Tiago Santini responsável por produto, decisões técnicas e verificação da demonstração. A publicação usará **exclusivamente dados sintéticos**, inclusive documentos, placas e fotos sem pessoas/veículos reais identificáveis. Metas de teste não representam observação de operação real. Uso real está fora deste MVP e exigirá revisão própria de negócio, privacidade e capacidade.

**Planejamento da Fase 3:** desconto, estorno, acerto de saldo e exceções seguem a seção 5.4. Para este exercício, Tiago responde pelas decisões comerciais; não se presume validação por negócio/financeiro reais.

**Planejamento da Fase 4 (16/11/2026):** o recorte da entrega, da vistoria de saída, do consumo de exceções e da liberação de vaga está detalhado na seção 5.5. Ele deriva dos requisitos RF-05/09/10/11 e RNF-01–04/07 e da fronteira deixada explícita na Fase 3, sem nova regra comercial. O detalhamento documental não executa testes, entrega, liberação ou publicação.

**Desenvolvimento local:** `compose.yml` na raiz deverá iniciar **somente a infraestrutura local, inicialmente SQL Server Developer**; API e Angular rodarão no host, com depuração/recarga e User Secrets. Exclusivamente em `Development`, o backend deve aplicar **todas as migrations versionadas pendentes automaticamente**, depois provisionar idempotentemente os três perfis/contas sintéticas, **antes de aceitar requisições**. Credenciais externas obrigatórias, preferencialmente em User Secrets; parâmetros não sensíveis em `appsettings*.json`. Falhas de banco/configuração/migração impedem a inicialização. Em Testing, a preparação do banco pertence aos testes; publicação usa migração controlada separada, sem bootstrap automático. Aceite: SQL iniciado pelo Compose e catálogo vazio recebem esquema/contas ao iniciar API; reinício sem migrations pendentes não recria esquema/contas; migration nova é aplicada na próxima inicialização; falha impede tráfego; fora de Development não há migração automática pelo backend.

## 1. Resumo executivo

Centralizar o atendimento do valet, desde a entrada até a entrega do veículo, com identificação do atendimento, localização da vaga, fila de retirada, cobrança padronizada e histórico auditável. O objetivo é reduzir espera, divergências financeiras e incerteza sobre a ocupação e as condições dos veículos, dando à administração informações confiáveis sobre a operação.

## 2. Contexto e problema

**Fonte de necessidades:** entrevista de Tiago Santini com Alexandre Rech (`docs/entrevista.md`). Na narrativa, Alexandre relata tickets de papel, busca manual de carros e vagas, cobrança e fechamento de caixa em planilhas, reclamações por demora e danos alegados. Ele pede busca por placa/identificador, visibilidade de vagas, acompanhamento de retiradas, cálculo automático, controle financeiro e acesso conforme a função. **A entrevista é fictícia**, conforme declaração no próprio documento: relatos e conclusões são insumos para o exercício, não evidência de uma operação real validada. O `README.md` descreve o mesmo problema e apresenta tecnologias do repositório, sem estabelecer regras comerciais.

**Decisões adicionais do solicitante:** o MVP cobre a operação completa de **uma unidade**; as vistorias de entrada e saída terão **observações textuais obrigatórias** e serão exclusivamente textuais até a Fase 4. A Fase 5, anterior à publicação, acrescentará fotos opcionais dos veículos às duas vistorias (§5.6), sem substituir o texto. A cobrança será por períodos iniciados de 24 horas; a identificação para retirada será conferida com nome e número de documento registrados na entrada e documento com foto apresentado presencialmente. Essas escolhas são decisões de escopo do PRD, não fatos atribuídos ao entrevistado.

**Hipóteses do exercício:** o fluxo pretende reduzir demora e divergências, e texto/fotos pretendem apoiar análise de danos. Esses benefícios deverão ser explorados em cenários simulados, não apresentados como resultados reais medidos.

## 3. Objetivos e métricas de sucesso

| Objetivo | Indicador e medição | Baseline / meta |
| --- | --- | --- |
| Agilizar a devolução | Tempo entre pedido e entrega, mediana e percentis por período | Medir baseline sintética na primeira demonstração; sem meta de redução comercial inventada |
| Reduzir divergências | Cobranças ajustadas e diferença entre cobrado e pago | Exemplos de fronteira e totais devem coincidir com os lançamentos; ajustes não contam como erro automaticamente |
| Melhorar controle de vagas | Ocupação e tentativas conflitantes de atribuição/liberação | Zero dupla ocupação/liberação nos cenários concorrentes de aceite |
| Dar visibilidade à operação | Atendimentos concluídos, entradas, recebimentos e saldos por período | Totais reconciliáveis com os eventos sintéticos e recortes de §10 |

Tiago avaliará os resultados a cada demonstração e revisará diariamente cotas, falhas e backups enquanto o MVP estiver publicado. Perfil de teste: **20 vagas, 5 funcionários, até 3 usuários simultâneos e 1.000 atendimentos históricos**. Consultas aquecidas deverão atingir **p95 ≤ 2 s** nesse perfil; cold start e suspensão por cota serão medidos separadamente. Não há baseline comercial real, SLA ou promessa de capacidade contínua.

## 4. Público-alvo e stakeholders

- **Cliente do valet:** deseja retirada segura, ágil e explicação clara da cobrança; não é usuário autenticado do sistema no MVP.
- **Manobrista:** consulta atendimentos, carros e vagas, registra vistoria de saída e atualiza a busca; texto e fotos de entrada são registrados por caixa/administrador.
- **Caixa/atendente:** registra atendimento e pedido, consulta operação, calcula cobrança, registra pagamento e confirma a entrega conforme permissões.
- **Administrador da unidade:** configura tarifa, gerencia acessos, acompanha relatórios, autoriza exceções e investiga histórico.
- **Responsável pelo projeto acadêmico:** Tiago Santini. Alexandre Rech é entrevistado fictício, não stakeholder real; eventual operação real deverá designar responsáveis próprios.

## 5. Escopo e prioridades

**MVP — P0 (necessário para operar):** check-in com identificador único, dados do veículo e titular, vistoria textual de entrada; cadastro e atribuição de vaga livre; consulta por placa/identificador; solicitação e acompanhamento de retirada; vistoria textual de saída; cálculo de diárias, registro de pagamento e entrega com liberação da vaga; três perfis de acesso; histórico de ações e exceções. **MVP — P1:** fotos opcionais das vistorias na Fase 5 e painel/relatórios por dia, semana e mês de receita, valores pendentes, atendimentos, períodos movimentados e ocupação, com comparação entre cobrado e pago, na Fase 6. A implantação requer cadastro de vagas, usuários/perfis e tarifa antes do primeiro atendimento. As fotos integram a entrega pré-publicação, mas sua ausência em um atendimento não bloqueia a operação.

**Evoluções posteriores:** múltiplas unidades, integrações de pagamento, comunicação automática com cliente e permissões customizadas; dependem de validação de valor e viabilidade.

**Fora do MVP:** processamento de pagamentos por provedor, autoatendimento do cliente, reconhecimento automático de placa, importação automática de planilhas e emissão fiscal integrada. O documento não presume obrigação fiscal específica.

### 5.1. Recorte da Fase 2 — preparação e entrada

A Fase 2 detalha RF-01–03, configuração/snapshot de RF-07, acessos de RF-10 e histórico de RF-11, com RNF-01–05/07. O escopo compreende:

- Administração de funcionários sem autorregistro: criação com credencial inicial e um ou mais dos três perfis fixos, alteração de perfis e ativação/desativação auditadas, sem apagar autoria nem permitir remover o último administrador ativo. Mudanças concorrentes não sobrescrevem acessos silenciosamente. Após mudança confirmada, a sessão anterior é recusada na próxima requisição protegida, inclusive na repetição de uma entrada; exige-se novo login.
- Cadastro administrativo de vagas de código único normalizado, consulta paginada por código/situação pelos três perfis e ocupação exclusiva vinculada à entrada. A disponibilidade exibida não reserva vaga; a decisão final é do servidor. Polling padrão de **30 s**, configurável, após comandos e ao recuperar foco, sem sobreposição e com pausa em aba inativa.
- Publicação administrativa de versões imutáveis de tarifa positiva, com histórico e controle de concorrência. Não há tarifa comercial inicial predefinida. Entrada sem tarifa é bloqueada; a versão e o valor capturados não mudam após publicação posterior.
- Entrada completa somente por caixa/administrador, com horário e operador do servidor, identificador único, documento e vistoria textual protegidos. Atendimento, snapshot, vistoria, ocupação, auditoria e resultado de repetição segura são confirmados juntos ou integralmente desfeitos. Mesma ação repetida com mesmo conteúdo retorna a entrada original; chave reutilizada com conteúdo diferente gera conflito.
- Busca exata normalizada por placa ou identificador, paginada e distinguindo visitas, sem impor unicidade de placa. Listagens não incluem documento completo nem vistoria. Detalhe e histórico são projetados pelo servidor: manobrista não recebe snapshot de tarifa nem outros dados financeiros. Documento completo exige leitura explícita de caixa/administrador; vistoria admite os três perfis. A auditoria da leitura deve confirmar antes de retornar conteúdo sensível; falha impede sua exposição.
- Navegação autenticada por perfil e feedback acessível. Sem sessão válida, exigir autenticação; acesso sem permissão é negado sem substituir a autorização da API. Sessão e tentativas incertas ficam apenas em memória. Exibir identificador na confirmação e no detalhe, com ação de copiar pelo funcionário, sem impressão ou envio digital obrigatório. Permitir replay explícito com mesma chave/conteúdo e limpar dados sensíveis após confirmação.

**Limites desta fatia:** não inclui pedido/conferência de retirada, cálculo de diárias, fila de retirada, pagamento, desconto/estorno, exceção de entrega, vistoria de saída, liberação de vaga ou relatórios. Essas funcionalidades continuam no MVP, mas em fases posteriores. Botão de liberação demonstrativa, ticket físico/canal de envio, CI, Azure, IaC e CD ficam fora desta fase.

### 5.2. Capacidades previstas e limites da Fase 2

| Capacidade prevista | RFs relacionados | Comportamento esperado e limites |
| --- | --- | --- |
| Equipe e acesso | RF-10, parte de RF-11 | Administração de e-mail, senha inicial, perfis e estado; concorrência, último admin ativo e revogação na próxima requisição são controles do servidor. Não inclui convite, recuperação de senha ou perfis customizados. |
| Preparação e disponibilidade | RF-02 | Cadastro, filtros, paginação e polling de vagas na consulta. Seletor do check-in paginado e com atualização explícita; não reserva nem libera vaga. |
| Tarifa versionada | Parte de RF-07 | Publicação, snapshot imutável e histórico com paginação/atualização. Cálculo de permanência, desconto, estorno e pagamento ficam para a Fase 3. |
| Entrada e recuperação de resposta perdida | RF-01/02, parte de RF-07/11 | Entrada atômica e replay no servidor; UI preserva tentativa/chave apenas em memória, bloqueia saída por rota/logout durante POST, avisa antes de saída do browser e trata rejeições definitivas sem gerar nova chave automaticamente. Não há recuperação após o usuário confirmar saída do browser/recarregar; decisão de não persistir dados no navegador permanece. |
| Busca, detalhe e leitura sensível | RF-03/10, parte de RF-11 | Busca exata, visitas distintas, paginação e projeções por perfil no servidor. Leitura explícita/auditada. Revisões de seleção invalidam resposta tardia de busca/detalhe/histórico/documento/vistoria, inclusive após fechar/sair. |
| Segurança e usabilidade local | RNF-01–04/07 | Sessão revogável com token apenas em memória, criptografia de campo, auditoria e não-cache; erros administrativos textuais com resumo/foco e semântica aria; erros operacionais em Problem Details com código/correlação e limitação de tentativas de login. Prever testes automatizados e avaliação manual de acessibilidade. |

Os objetivos de desempenho, recuperação, privacidade e operação reais continuam sujeitos às seções 8 e 12; a existência de polling, criptografia ou testes locais não os encerra.

### 5.3. Critérios complementares de aceite da Fase 2

- **Concorrência de leitura:** respostas atrasadas não devem substituir a seleção atual nem expor documento/vistoria de outro atendimento, inclusive após fechar ou sair da tela.
- **Saída e repetição segura:** comandos pendentes devem bloquear navegação interna e logout e avisar antes de sair do navegador. Resultado incerto deve permitir repetição explícita com a mesma chave/conteúdo; rejeição definitiva deve permitir correção sem reenvio automático. A saída confirmada do navegador pode perder dados mantidos somente em memória.
- **Validação acessível:** formulários devem apresentar mensagens textuais, identificar campos inválidos e focar o resumo de erros; dados inválidos não devem gerar chamada à API. A avaliação manual deve registrar navegador, dispositivo e tecnologia assistiva utilizados.
- **Consulta completa:** seletor de vagas e histórico de tarifas devem permitir navegar por todas as páginas e recuperar falhas de carga sem perder o formulário.
- **Contratos e isolamento:** erros devem ter formato consistente sem dados pessoais; excesso de tentativas de login deve retornar `429`. Testes com banco devem usar destino e credenciais isolados, nunca bancos operacionais.

### 5.4. Recorte da Fase 3 — retirada e caixa

Detalhar RF-04–08/10/11 e RNF-01–05/07, preservando o snapshot de RF-07 e a ocupação de RF-02. As decisões abaixo foram confirmadas pelo solicitante para planejar a fase:

- Caixa/administrador registra pedido após conferir presencialmente nome/número com documento com foto, declarando resultado **aprovado** ou **divergente**, horário e ator do servidor. Divergência não apaga nem impede o pedido/cálculo; bloqueia entrega normal futura. Não fotografar o documento pessoal nem coletar novo número de documento. Segundo pedido do mesmo ciclo é recusado, salvo replay da mesma ação.
- No pedido, fixar tempo decorrido, diárias iniciadas de 24 h (mínimo uma) e total pela tarifa da entrada. Fila paginada e com polling permite consultar pedidos e registrar início de busca pelos três perfis, sem expor finanças/documento completo ao manobrista.
- **F3-R01 — desconto:** somente administrador, valor monetário positivo e justificativa; permitido enquanto não houver pagamentos líquidos. Soma dos descontos limitada ao total original, podendo zerar a cobrança. Cada concessão é lançamento preservado, não edição da base. Sem desconto percentual nesta fase.
- **F3-R02 — estorno:** somente administrador, integral do pagamento ainda não estornado, com justificativa e vínculo ao lançamento original; reabre o saldo e não reduz o cobrado. Sem estorno parcial, exclusão do original ou execução bancária.
- **F3-R03 — acerto:** caixa/administrador registra exatamente o saldo exigível positivo, em dinheiro, cartão ou Pix. Após estorno ou entrega excepcional com saldo, admite novo pagamento integral do saldo atual, nunca parcial, excedente, crédito ou troco. Cobrança zerada por desconto não exige pagamento fictício.
- **F3-R04 — exceções:** administrador concede autorizações separadas para divergência documental e saldo pendente, com justificativa, vinculadas ao pedido e à versão relevante da conferência/cobrança. São revogáveis antes do consumo; alteração relevante invalida sua vigência. A autorização não muda conferência, saldo ou pagamento. Consumo e validação final ocorrerão na entrega da Fase 4.
- **Limite de identidade:** retirada por terceiro ou com titular ausente não será permitida no MVP, nem mediante exceção administrativa. A exceção documental destina-se a divergências do titular presente, não a transferir o direito de retirada.
- Pedido/cálculo, pagamento, desconto, estorno e autorizações devem ser atômicos com auditoria e resultado idempotente; concorrência não pode duplicar pedido, quitação ou estorno, nem conceder desconto sobre saldo já pago. Interface preserva tentativas incertas somente em memória e repete explicitamente a mesma chave/conteúdo.

**Limites:** esta fase não entrega veículo, não coleta vistoria de saída, não libera vaga, não consome autorização de entrega e não marca pedido como entregue. O estado entregue pertence ao contrato completo da fila, mas será produzido apenas pela Fase 4. Não inclui relatórios, fechamento de caixa, integração financeira, CI, Azure, IaC ou CD. Acerto após entrega é contrato financeiro a testar com fixture sintética, sem criar endpoint de entrega antecipado.

**Aceite adicional:** testar 0/24 h/24 h + menor fração persistida/48 h; snapshot após mudança de tarifa; pedido divergente preservado; desconto até zero e bloqueado com pagamento líquido; estorno integral único; saldo reaberto e integralmente quitado; invalidação/revogação de exceções; concorrência, rollback de auditoria e replay após resposta perdida. Nenhuma dessas verificações está executada pela geração da proposta.

### 5.5. Recorte da Fase 4 — entrega e liberação

Detalhar RF-05/09/10/11 e RNF-01–04/07 sobre as capacidades previstas nas fases anteriores, sem alterar as regras comerciais definidas. O recorte abaixo deriva do PRD/TDD e não cria nova política de cobrança, retenção ou atendimento ao cliente. A execução dos gates SQL/E2E deve ser registrada nas evidências da fase quando houver implementação.

- **F4-R01 — vistoria de saída:** os três perfis podem registrar, uma única vez por atendimento, vistoria textual das condições de saída, com ator e UTC do servidor, vinculada ao pedido de retirada. O texto é obrigatório, cifrado no armazenamento, imutável e lido por ação explícita e auditada; nesta fase não há fotos nem novo documento do titular. Atendimento já entregue, sem pedido ou com vistoria existente não admite novo registro. Fotos complementares serão tratadas na Fase 5, sem editar o texto.
- **F4-R02 — entrega normal:** caixa/administrador confirma a entrega somente com pedido existente, vistoria de saída registrada, conferência documental **aprovada** e saldo exigível **zero**. A passagem direta de **aguardando** para **entregue** é permitida, sem inventar etapa obrigatória de busca. Ator e horário são do servidor.
- **F4-R03 — entrega com exceção:** conferência divergente exige autorização documental vigente; saldo positivo exige autorização financeira vigente e vinculada à revisão corrente da razão. As duas autorizações são independentes e só podem ser usadas se não estiverem revogadas, substituídas ou obsoletas. A entrega registra o consumo de cada autorização utilizada, uma única vez, sem apagar concessão, revogação ou justificativa. Após a entrega, não se concedem nem se consomem novas autorizações para aquele pedido.
- **F4-R04 — efeitos atômicos e únicos:** a confirmação grava a entrega, o consumo das exceções aplicáveis, o encerramento do pedido e do atendimento, o fim da ocupação e a liberação da vaga na mesma transação, com histórico e resultado idempotente. Repetição da mesma ação retorna o resultado original; ação nova após a entrega é recusada. Concorrência não pode produzir duas entregas, dois consumos ou duas liberações, nem liberar vaga de outro atendimento.
- **F4-R05 — saldo posterior:** a entrega excepcional com saldo não inventa quitação nem altera os lançamentos originais. O saldo permanece visível e pode ser integralmente quitado após a entrega, sem reabrir atendimento, pedido, ocupação ou vaga; cobrança zerada por desconto não exige pagamento fictício.
- **F4-R06 — consulta, histórico e perfis:** a fila e o detalhe passam a exibir o estado **entregue** com horário/responsável e a vaga liberada, sem expor ao manobrista finanças, conferência, autorizações ou justificativas. O histórico registra vistoria de saída, consumo, entrega e liberação com ator/UTC; o manobrista recebe apenas eventos operacionais permitidos, e referências financeiras exigem caixa/administrador. Justificativas continuam acessíveis somente ao administrador por leitura explícita e auditada.
- **F4-R07 — interface e diagnóstico:** a interface de entrega apresenta bloqueios textuais antes da confirmação, exige ação explícita, preserva tentativa incerta somente em memória e não repete comandos automaticamente. Falhas de entrega, conflitos e liberações são registrados em log estruturado com correlação, ação, resultado e latência, sem placa, nome, documento, vistoria, valores ou justificativas; configuração de alertas, SLAs e monitoramento externo fica para a Fase 6.

**Aceite adicional:** cobrir entrega normal aguardando→entregue e em busca→entregue; ausência de vistoria; divergência sem autorização; saldo sem autorização; autorização revogada, substituída ou obsoleta após pagamento/desconto/estorno; duas entregas concorrentes; replay após commit; vaga liberada uma única vez e reutilizável por nova entrada; pagamento integral posterior ao saldo excepcional; vazamento bruto de finanças/documento/vistoria/justificativa por perfil; token ausente/inválido e sessão revogada; e liberação de vaga pertencente a outro atendimento. Prever cenários automatizados, E2E, revisão manual de acessibilidade e validação operacional/comercial; este documento não registra sua execução.

**Limites:** esta fase não inclui relatórios P1, fechamento de caixa, integração financeira, emissão fiscal, ticket, fotos, comunicação ao cliente, cancelamento/reabertura de pedido, nova vistoria após o registro, múltiplas unidades, CI, Azure, IaC ou CD. A conclusão local do P0 dependerá do gate desta fase; ela não substituirá homologação Azure, RF-12 ou os critérios de publicação.

### 5.6. Recorte da Fase 5 — fotos de vistoria antes da publicação

Acrescentar RF-13 às vistorias de check-in e checkout, após o P0 textual e antes da publicação prevista na Fase 6:

- Fotos de veículos **opcionais**, sem substituir as observações textuais obrigatórias. A ausência de foto, indisponibilidade do armazenamento ou recusa por limite não deve impedir entrada ou entrega que satisfaça os requisitos P0.
- Até **5 fotos por vistoria**, de até **5 MiB por arquivo**, em **JPEG, PNG ou WebP estáticos**. Validar o conteúdo real e remover metadados, inclusive EXIF/GPS, antes do armazenamento.
- Anexar fotos somente após salvar a vistoria textual. Permitir anexos de entrada até o pedido de retirada e de saída até a confirmação da entrega; esses marcos devem bloquear também uploads concorrentes ainda não vinculados. Não alterar o texto, substituir fotos confirmadas ou acrescentar fotos após o encerramento da janela.
- Fotos de entrada: envio por caixa/administrador. Fotos de saída e consulta de fotos das duas vistorias: os três perfis, com autorização no servidor e leitura explícita auditada. Não fotografar documentos pessoais; orientar a equipe a evitar pessoas e dados alheios ao veículo.
- Acesso privado via API, com vínculo à vistoria, autor, horário e histórico. Não disponibilizar galeria pública nem credenciais de armazenamento no navegador.
- Preservar atendimentos anteriores como registros textuais, sem exigir fotos retroativas. Descarte seguirá os prazos de §10; não apagar evidências antecipadamente para liberar cota. Imagens terão resolução normalizada e metadados removidos conforme TDD, sem promessa de preservação pericial do original.

**Aceite adicional:** anexar/consultar fotos nas duas vistorias; aceitar zero fotos; rejeitar formato inválido, arquivo excessivo e sexto anexo sob concorrência; comprovar normalização, remoção de EXIF/GPS, isolamento, idempotência e bloqueio nos marcos. Falha isolada do R2 não deve bloquear P0 textual. Avisar aos **70%** e suspender operações que excederiam **80%** da franquia correspondente de bytes/Classes A/B, somando ambientes e cópias em nuvem. Ao limitar leitura de fotos, informar indisponibilidade; conferir consumo da conta sem presumir trava de cobrança.

## 6. Jornadas e casos de uso

1. **Preparação:** administrador cadastra vagas e tarifa e atribui perfis aos funcionários; sem tarifa ou vagas disponíveis não se conclui entrada que exige vaga.
2. **Entrada:** atendente registra placa, modelo, nome e número de documento do titular, horário e descrição textual das condições; seleciona vaga livre e recebe identificador único. Sistema impede atribuição simultânea da mesma vaga a dois atendimentos ativos; se dados obrigatórios faltarem, não conclui check-in. A partir da Fase 5, permite anexar fotos opcionais à vistoria salva antes do pedido de retirada.
3. **Localização:** equipe pesquisa por placa ou identificador, vê vaga e situação do veículo; resultados homônimos/placas repetidas não devem ser confundidos com um único atendimento.
4. **Pedido de retirada:** caixa localiza atendimento, confere presencialmente documento com foto com nome/número registrados e registra pedido; horário do pedido fixa o cálculo da permanência. Mesmo sem ticket, a busca por placa é possível; divergência documental bloqueia entrega normal e exige autorização excepcional do administrador, registrada com justificativa.
5. **Busca e cobrança:** fila mostra pedidos aguardando, em busca e entregues; equipe registra início da busca, caixa mostra tempo, diárias, valor e recebe pagamento integral. Sem pagamento integral, entrega só com autorização excepcional do administrador e saldo pendente identificado.
6. **Entrega:** registrar condições de saída em texto e responsável; a partir da Fase 5, permitir fotos opcionais da vistoria salva antes da confirmação. Confirmar entrega e horário; sistema libera vaga e encerra atendimento. Pedido duplicado, vaga já ocupada e tentativa de entrega sem conferência/quitação ou autorização geram bloqueio e mensagem explicativa; ausência de fotos não bloqueia a entrega.
7. **Acompanhamento:** administrador consulta períodos e compara total cobrado, total recebido e saldos pendentes; manobrista não visualiza relatórios financeiros.

## 7. Requisitos funcionais

| ID | Prioridade | Requisito verificável |
| --- | --- | --- |
| RF-01 | P0 | Registrar atendimento com identificador único por entrada, placa, modelo, nome e número do documento do titular, data/hora de entrada, operador e descrição textual obrigatória das condições de entrada; não aceitar entrada incompleta. |
| RF-02 | P0 | Cadastrar vagas da unidade, exibir situação livre/ocupada e atribuir uma vaga livre ao concluir entrada; impedir atribuição simultânea ou vaga inexistente. |
| RF-03 | P0 | Consultar atendimentos por placa ou identificador e mostrar estado, vaga e dados permitidos ao perfil; distinguir múltiplas entradas do mesmo veículo. |
| RF-04 | P0 | Registrar pedido de retirada apenas para atendimento ativo, com horário, operador e verificação presencial de documento com foto contra nome/número registrados; impedir pedido duplicado ativo e bloquear entrega se houver divergência não autorizada. |
| RF-05 | P0 | Exibir fila com estados **aguardando**, **em busca** e **entregue**, registrando transições, responsáveis e horários; entrega requer pedido prévio. |
| RF-06 | P0 | Calcular e apresentar tempo decorrido até o **pedido de retirada**, quantidade de diárias e valor total: uma diária por período de até 24 horas iniciado desde a entrada, mínima de uma diária; exatamente 24 horas = uma diária, mais de 24 até 48 = duas. Fixar quantidade/total ao registrar pedido. |
| RF-07 | P0 | Permitir ao administrador configurar valor da diária e preservar no atendimento a tarifa vigente **na entrada**; alterações posteriores não mudam o valor desse atendimento. Desconto/estorno requer administrador, justificativa e registro da alteração. |
| RF-08 | P0 | Registrar manualmente pagamento **integral**, com valor, forma (dinheiro, cartão ou Pix), data/hora e operador, sem integração de cobrança; manter estado de valor devido, pago e saldo pendente; impedir registro duplicado como quitação sem ajuste auditado. |
| RF-09 | P0 | Na entrega, exigir vistoria textual de saída, verificação documental aprovada e quitação; administrador pode autorizar excepcionalmente divergência documental ou entrega com saldo pendente, com justificativa e rastreabilidade. Confirmar horário e responsável e liberar a vaga automaticamente uma única vez. |
| RF-10 | P0 | Aplicar três perfis: manobrista (consulta de carros/vagas, movimentação/retirada e vistoria), caixa (operação, cobrança, pagamento e entrega) e administrador (todas as ações, configuração, autorizações e relatórios); negar acesso a dados financeiros e configuração ao manobrista. |
| RF-11 | P0 | Guardar histórico por atendimento de entrada, atribuição/liberação de vaga, pedidos/estados, conferência, condições, cálculo, pagamentos, ajustes, exceções e entrega, com data/hora e usuário responsável. |
| RF-12 | P1 | Mostrar indicadores por dia, semana e mês para receita recebida, total cobrado, pendências/diferença, número de veículos atendidos, distribuição de entradas por faixa horária e ocupação derivada dos eventos de vagas; restringir ao administrador. |
| RF-13 | P1 | Anexar e consultar fotos opcionais das vistorias de entrada e saída, preservando texto obrigatório, limites, janelas de inclusão, perfis, privacidade e rastreabilidade definidos em §5.6; falha ou ausência de fotos não impede check-in/checkout textual. |

**Regra transversal:** não alterar silenciosamente atendimentos encerrados, pagamentos ou históricos; correções financeiras são eventos auditados. O valor numérico da tarifa é configurado pelo administrador, não estabelecido na entrevista.

## 8. Requisitos não funcionais

| ID | Requisito / validação |
| --- | --- |
| RNF-01 | Autenticar funcionários com sessão revogável e aplicar autorizações nas leituras/escritas do serviço, não apenas na interface. Logout, desativação e alteração de acessos devem invalidar a sessão no servidor para a próxima requisição. JWT com assinatura HS256 e chave aleatória de 32 bytes por versão, externa e exclusiva dessa finalidade, sem exigir RSA; token somente em memória do navegador. Validar acesso indevido, revogação, expiração e rotação conforme TDD. |
| RNF-02 | Usar apenas dados sintéticos; proteger documentos, textos e fotos em trânsito/repouso, restringir leitura por perfil e registrar acessos. Manter cifragem autenticada na aplicação para documento, vistorias e justificativas, com chaves simétricas versionadas externas e recuperáveis, sem exigir certificado de cifragem de campos. Remover metadados de imagens, impedir bucket público e cumprir retenção de §10. Configuração criptográfica, segredos e recuperação conforme TDD; uso de dados reais não está autorizado por este escopo. |
| RNF-03 | Garantir consistência de atribuição/liberação de vagas, quitação e entrega sob ações simultâneas ou tentativas repetidas; teste de concorrência não deve produzir duas ocupações ativas na mesma vaga. Repetição segura compara SHA-256 do conteúdo canônico versionado, sem segredo HMAC, mantendo autorização, transação e conflito por conteúdo diferente; hash não é anonimização nem autenticação. |
| RNF-04 | Alvo WCAG **2.2 AA**, teclado, foco visível e identificação textual de estados/erros/ações. Testes automatizados e revisão manual com registro de ambiente; não declarar conformidade apenas pela escolha do alvo. |
| RNF-05 | No perfil acadêmico de §3, consultas aquecidas com p95 ≤ 2 s; fila/vagas com polling padrão 30 s, pausa em aba oculta e atualização após comando/foco. Medir cold start e suspensão separadamente, sem SLA gratuito. |
| RNF-06 | Publicação gratuita sem SLA ou upgrade/excedente pago automático. Backup lógico diário cifrado de SQL/fotos/chaves, com 7 cópias locais em equipamento controlado por Tiago; metas de exercício RPO 24 h/RTO 4 h. Persistir transações e identificar incerteza; sem rede/cota, suspender operação e retomar por consulta/replay, sem modo offline ou importação de controles manuais. |
| RNF-07 | Registrar falhas e eventos sem PII, tokens ou conteúdo de vistoria/foto em logs. Logs técnicos com retenção máxima de 30 dias e limite de espaço; auditoria durável no SQL. Tiago revisa cotas, falhas e backups diariamente; avisos na interface, sem telemetria/alertas pagos obrigatórios. |

## 9. Critérios de aceite

| Cenário | IDs | Critério testável |
| --- | --- | --- |
| Entrada válida/inválida | RF-01, RF-02, RNF-03 | Com todos os dados e texto de vistoria, atribui identificador e vaga livre; sem documento, texto ou vaga livre, entrada não é concluída; duas entradas concorrentes não recebem a mesma vaga. |
| Busca | RF-03, RF-10 | Busca por placa ou identificador encontra atendimentos pertinentes e distingue históricos; manobrista não acessa dados financeiros. |
| Pedido e extravio | RF-04, RF-05, RF-09 | Sem ticket, caixa localiza por placa e confere documento; coincidência permite pedido, divergência impede entrega normal; somente administrador autoriza exceção justificada; segundo pedido ativo é recusado. |
| Diárias e tarifa | RF-06, RF-07 | Pedido exatamente 24 h após entrada gera uma diária; após 24 h gera duas; total = diárias × tarifa da entrada, antes de eventual desconto autorizado; mudança posterior de tarifa não altera total do atendimento. |
| Fila | RF-05, RF-11 | Pedido novo aparece aguardando, passa a em busca e depois entregue, com horários/responsáveis; não se pode marcar entregue sem pedido ativo. |
| Quitação e exceção | RF-07, RF-08, RF-09 | Registrar pagamento integral mostra forma, valor e operador; parcial não quita nem libera entrega normal; administrador pode autorizar entrega com saldo pendente e justificativa; ajuste/estorno autorizado fica visível no histórico sem apagar o lançamento original. |
| Entrega e vaga | RF-02, RF-09, RF-11, RNF-03 | Sem texto de condições de saída não conclui; com requisitos satisfeitos, registra entrega e libera a vaga; repetição do comando não libera outra vaga nem duplica entrega. |
| Acesso e relatórios | RF-10, RF-12 | Administrador consulta totais por dia/semana/mês, pendências, entradas por faixa horária e ocupação calculados dos registros; manobrista não abre nem requisita o relatório. |
| Rastreabilidade | RF-11, RNF-01 | Cada operação crítica exibe usuário e horário; tentativa de operação sem permissão é negada e passível de investigação. |
| Cifragem e recuperação | RNF-02/06 | Documento, vistorias e justificativas persistem cifrados; reinício e rotação preservam leitura autorizada com chaves históricas. Alteração do conteúdo cifrado ou troca entre campos/registros impede leitura; chave ausente não resulta em conteúdo vazio nem texto puro. Exercício de recuperação inclui as versões exigidas pelos dados e backups. |
| Fotos das vistorias | RF-13, RF-10/11, RNF-02/03 | Zero a cinco fotos válidas por vistoria, nas janelas e perfis permitidos; conteúdo privado sem EXIF/GPS; concorrência/replay não duplica nem excede limites; falha de upload não impede fluxo textual; histórico não é reescrito. |

## 10. Dados e integrações

**Entidades principais:** funcionário/usuário e perfil; vaga e eventos de ocupação; atendimento (identificador, placa, modelo, titular, documento, tarifa aplicada, horários, estado); vistorias textuais de entrada/saída e suas fotos opcionais a partir da Fase 5; pedido de retirada e estados; cobrança (tempo, diárias, valores e ajustes); pagamento (valor, forma, horário, operador); autorização excepcional (motivo, responsável); trilha de auditoria.

**Ciclo de vida:** atendimento criado na entrada → vaga ocupada → retirada solicitada (valor fixado) → busca → quitação ou autorização excepcional → vistoria de saída e entrega → vaga livre. Histórico de atendimentos concluídos e valores originais permanece consultável por perfis autorizados. Pendências financeiras excepcionais devem permanecer visíveis no relatório mesmo após entrega.

**Moeda e tempo:** BRL, apresentação pt-BR e fuso **America/Sao_Paulo**; persistência UTC e dinheiro com duas casas decimais, rejeitando precisão excedente. Nenhuma tarifa comercial automática; administrador deve publicar valor positivo antes da primeira entrada. **R$ 20,00** será apenas tarifa de exemplo/teste, sem seed comercial na publicação.

**Recortes dos relatórios:** dia civil local; semana de segunda a domingo; mês civil, com intervalos de início inclusivo/fim exclusivo. Recebimentos e estornos pela data do próprio lançamento; cobrado e descontos pela data do pedido/concessão. Exibir bases e componentes separados, inclusive total líquido negativo em período com estornos/descontos de cobrança anterior. Saldo por atendimento na data de corte, inclusive entregas excepcionais; ocupação derivada de intervalos de ocupação e entradas por horário local. Não confundir entradas com entregas concluídas nem líquido do período com saldo acumulado. Conciliação manual diária; sem módulo de fechamento ou emissão fiscal integrada.

**Retenção acadêmica:** contar a partir do encerramento e quitação, usando o marco mais recente; atendimento ativo ou com saldo não entra no descarte automático. Após **90 dias**, descartar fotos, textos de vistoria e identificadores do titular/veículo; manter por **365 dias** histórico financeiro/auditoria/idempotência sem esse conteúdo. Contas funcionais ativas permanecem necessárias ao acesso e não são cadastro do titular. Preservar apenas referências opacas e metadados mínimos nos eventos, sem cópias pessoais em payloads. Após 365 dias, descarte controlado do histórico conforme dependências; imutabilidade vale para operações normais, não impede esse procedimento explícito e auditado. Não reconstruir dados descartados se houver ajuste posterior.

Descarte/reconciliação será retomável após iniciar/reativar o serviço; item vencido deverá deixar de ser exposto mesmo se a remoção física estiver adiada por pausa. Backups locais cifrados terão rotação de **7 cópias diárias**; restauração deverá reaplicar expiração antes da consulta. Esses prazos são escolhas da demonstração, não afirmações de retenção legal para dados reais.

**Interfaces externas:** a Fase 5 adicionará armazenamento privado de fotos em serviço de objetos, com Cloudflare R2 via API compatível com S3 detalhado no TDD. O P0 textual não dependerá dessa integração. Dinheiro, cartão e Pix são **formas informadas manualmente**, não confirmação automática de liquidação. Hardware de tickets, gateway de pagamento, sistema fiscal e importação de dados ficam fora de escopo. O `README.md` lista tecnologias do projeto, mas este PRD não as transforma em requisitos de arquitetura.

**Publicação definida pelo solicitante:** frontend Angular no **Azure Static Web Apps Free**, backend no **Azure App Service F1** e banco na **oferta gratuita do Azure SQL Database**, com publicação na Fase 6 e elegibilidade a verificar antes de provisionar. A SPA chamará a API diretamente por HTTPS com CORS restrito e JWTs revogáveis, sem depender do proxy pago do SWA ou de cookies entre sites. Azure SQL deverá pausar ao esgotar a cota, sem excedentes pagos. Cloudflare R2 Standard deverá permanecer dentro da franquia. Usar domínios gratuitos dos provedores; aceitar pausas/limites, sem SLA ou upgrade pago automático. A restrição de custo é decisão de projeto, não prova de gratuidade já verificada em uma conta; se não for viável nos serviços escolhidos, interromper a publicação e rever o plano.

**Configuração da demonstração:** cadastrar 20 vagas e até 5 contas funcionais sintéticas, sem credenciais no repositório. Os três perfis devem estar representados; senhas iniciais serão fornecidas por configuração externa. Publicar a tarifa explicitamente, sem valor padrão. Nomes de recursos/URLs, valores de segredos e versões exatas serão materializados na implementação/implantação, não inventados neste documento.

**Configuração e segredos:** preferir os mecanismos padrão do .NET — `appsettings.json`/arquivos por ambiente para parâmetros não sensíveis e User Secrets em desenvolvimento — em vez de depender de serviço adicional de configuração/segredos. Na publicação, valores sensíveis serão fornecidos por variáveis de ambiente/App Settings do App Service, fora dos arquivos versionados. Não exigir Key Vault ou serviço equivalente no MVP gratuito; detalhes e limites de segurança no TDD §6.4.

**Autenticação:** manter sessão revogável no SQL, JWT de 15 minutos, sessão absoluta de 8 horas, refresh rotativo e tolerância de 30 segundos. Assinar/validar JWT com HS256, usando chaves simétricas aleatórias de 32 bytes representadas em Base64, com versão ativa e rotação de 90 dias. Chaves anteriores ficam disponíveis somente pelo tempo necessário à validação dos tokens emitidos, sem distribuição de segredo ao navegador e sem reutilização das chaves AES ou dos backups. A retirada do segredo HMAC de idempotência não retira o segredo necessário à assinatura JWT.

**Escopo de cifragem e idempotência:** manter documento, textos de vistoria e justificativas cifrados na aplicação com AES-256-GCM e chaves versionadas; não exigir Data Protection, key ring SQL ou certificado PFX para esses campos. Chaves AES continuam sendo segredos, separados de JWT e backups, preservados enquanto houver dados ou cópias dependentes. Idempotência usa SHA-256 sem segredo, com risco de descoberta por tentativa de conteúdo previsível aceito apenas para dados sintéticos; não equivale à anonimização e exige reavaliação antes de qualquer uso real. Fotos permanecem sob os controles próprios de armazenamento privado do TDD, sem nova cifragem de aplicação neste recorte.

## 11. Riscos, premissas e dependências

| Ponto | Impacto | Mitigação / validação |
| --- | --- | --- |
| Fonte fictícia, sem observação real da operação | Regras não representam negócio real validado | Tiago valida cenários acadêmicos; uso real exigirá outra revisão antes de introduzir dados reais, sem bloquear o exercício por ausência de stakeholders reais. |
| Fotos opcionais e vistoria textual | Evidência visual pode faltar em disputas sobre danos | Manter texto obrigatório, orientar captura útil e validar qualidade dos registros com a equipe; não prometer cobertura fotográfica de todos os atendimentos. |
| Armazenamento externo de fotos | Exposição, indisponibilidade ou cobrança | Bucket privado, imagens sintéticas normalizadas, prazos de §10, margens de 70%/80%, backup local e contingência textual. Cotas devem ser conferidas na conta antes e durante o uso. |
| Publicação gratuita e cotas compartilhadas | Pausas, cold starts, recusa de uploads e indisponibilidade sem SLA | Verificar elegibilidade/cotas; Azure SQL com pausa por limite, sem excedentes pagos; não introduzir serviços cobrados. Preferir suspender funcionalidades a mudar silenciosamente para plano pago. |
| Documento do titular e conferência presencial | Tentativa de retirada por terceiro ou uso indevido de dado real | Somente dados sintéticos e titular presente na simulação; bloquear terceiro mesmo por exceção e cumprir minimização/retencão definidas. |
| Chaves de cifragem e hashes idempotentes | Perda de leitura por descarte de chave ou descoberta por tentativa de conteúdo previsível no hash | Preservar e recuperar chaves versionadas com backups, testar rotação e adulteração; não tratar SHA-256 como anonimização, não armazenar corpos em idempotência e manter uso exclusivamente sintético. |
| Tarifa por 24 h iniciadas e valor fixado no pedido | Erro de fronteira ou uso como política real validada | Testar diárias e ajustes com tarifa sintética; política real futura exigirá avaliação própria. |
| Pagamento manual e exceções de saldo | Divergência entre registro e dinheiro efetivamente recebido | Administrador faz conciliação com fechamento de caixa e audita autorizações/ajustes. |
| Rede, cold start e cotas gratuitas | Fila atrasada, requisição incerta ou aplicação suspensa | Avisar e suspender mutações sem conexão/serviço; após retomada, consultar estado e repetir mesma chave quando necessário. Testar concorrência e não prometer operação offline. |

## 12. Decisões encerradas e próximos passos

As escolhas de produto foram fechadas com Tiago nesta revisão: contexto acadêmico/sintético; BRL/pt-BR/São Paulo; tarifa configurada sem padrão comercial; recortes financeiros por evento e conciliação diária; identificador em tela/cópia; retirada por terceiro excluída; fotos opcionais; retenção, cotas, perfil de carga, acessibilidade e contingência definidos acima.

Não restam decisões de escopo em aberto para esta base. As atividades abaixo são verificações futuras, **não resultados concluídos**:

| Responsável | Atividade | Momento e evidência |
| --- | --- | --- |
| Tiago / implementação | Criar/testar funcionalidades, segurança, acessibilidade e exemplos de fronteira | Fases 1–5; resultados rastreáveis a RF/RNF, sem reaproveitar relatos de execução inexistentes |
| Tiago / publicação | Verificar elegibilidade/região/cotas, configurar recursos gratuitos e contas/dados sintéticos | Fase 6; plano de custo zero verificável, sem upgrade pago automático |
| Tiago / qualidade | Medir perfil de teste e baseline de retirada simulada; revisar teclado/tecnologia assistiva | Antes da publicação; registrar ambiente, métricas e limitações, sem alegar conformidade já obtida |
| Tiago / operação acadêmica | Exercitar backup/restore/expiração e revisar diariamente cotas/falhas/cópias | Antes e durante publicação; registro de exercício RPO/RTO e checklist diário |
| Futuro responsável real | Reavaliar negócio, base legal, localização/transferência de dados, retenção, requisitos fiscais e capacidade | Somente se houver decisão posterior de uso real; fora do MVP sintético |
