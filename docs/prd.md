# PRD — Plataforma de Gestão de Estacionamento e Valet

**Status:** Aprovado para planejamento do MVP (validações pré-implantação pendentes)  
**Data:** 05/11/2026  
**Aprovação:** solicitante, em 05/11/2026; o escopo está aprovado, sem dispensar as validações da seção 12.  
**Origem:** solicitação de PRD baseada em `docs/entrevista.md`, contexto de `README.md` e esclarecimentos do solicitante nesta conversa.  
**Escopo:** MVP de uma unidade de valet/estacionamento.

**Ajuste de desenvolvimento local (06/11/2026, confirmado pelo solicitante):** em `Development`, a API deve aplicar as migrações versionadas e provisionar idempotentemente os três perfis/contas sintéticas antes de aceitar requisições, com credenciais externas obrigatórias. Falhas de banco ou credenciais impedem a inicialização. Outros ambientes não executam esse bootstrap; migrações de homologação/produção continuam sujeitas a revisão e aprovação explícitas. Aceite: iniciar sobre banco local vazio cria esquema e contas, reiniciar não duplica contas, configuração ausente falha antes de servir e ambiente não Development não migra nem provisiona.

## 1. Resumo executivo

Centralizar o atendimento do valet, desde a entrada até a entrega do veículo, com identificação do atendimento, localização da vaga, fila de retirada, cobrança padronizada e histórico auditável. O objetivo é reduzir espera, divergências financeiras e incerteza sobre a ocupação e as condições dos veículos, dando à administração informações confiáveis sobre a operação.

## 2. Contexto e problema

**Fonte de necessidades:** entrevista de Tiago Santini com Alexandre Rech (`docs/entrevista.md`). Na narrativa, Alexandre relata tickets de papel, busca manual de carros e vagas, cobrança e fechamento de caixa em planilhas, reclamações por demora e danos alegados. Ele pede busca por placa/identificador, visibilidade de vagas, acompanhamento de retiradas, cálculo automático, controle financeiro e acesso conforme a função. **A entrevista é fictícia**, conforme declaração no próprio documento: relatos e conclusões são insumos para o exercício, não evidência de uma operação real validada. O `README.md` descreve o mesmo problema e apresenta tecnologias do repositório, sem estabelecer regras comerciais.

**Decisões adicionais do solicitante:** o MVP cobre a operação completa de **uma unidade**; a vistoria será **somente textual**, sem fotos no MVP, contrariando a sugestão de fotos na entrevista; a cobrança será por períodos iniciados de 24 horas; a identificação para retirada será conferida com nome e número de documento registrados na entrada e documento com foto apresentado presencialmente. Essas escolhas são decisões de escopo do PRD, não fatos atribuídos ao entrevistado.

**Hipóteses a validar:** o fluxo escolhido é adequado ao volume real de veículos; a conferência presencial de documento é operacionalmente viável; as informações registradas bastam para lidar com reclamações de danos sem imagens.

## 3. Objetivos e métricas de sucesso

| Objetivo | Indicador e medição | Baseline / meta |
| --- | --- | --- |
| Agilizar a devolução | Tempo entre pedido de retirada e entrega, mediana e percentis por período, a partir dos eventos registrados | Baseline e meta a definir após medição piloto |
| Reduzir divergências | Quantidade e proporção de cobranças ajustadas/contestadas e diferença entre valores cobrados e pagos por período | Baseline e meta a definir com operação/administrador |
| Melhorar controle de vagas | Ocupação atual e série de ocupação calculada pelos eventos de ocupação/liberação; incidentes de tentativa de atribuir vaga ocupada | Baseline e meta a definir no piloto |
| Dar visibilidade à operação | Atendimentos concluídos e receita registrada por dia, semana e mês; distribuição de entradas por faixa horária | Baseline e meta a definir com administrador |

O administrador deverá validar definições, periodicidade de acompanhamento e metas após observar dados reais. Não há números de desempenho confirmados pela fonte fictícia.

## 4. Público-alvo e stakeholders

- **Cliente do valet:** deseja retirada segura, ágil e explicação clara da cobrança; não é usuário autenticado do sistema no MVP.
- **Manobrista:** consulta atendimentos, carros e vagas, registra condições e atualiza a movimentação/retirada.
- **Caixa/atendente:** registra atendimento e pedido, consulta operação, calcula cobrança, registra pagamento e confirma a entrega conforme permissões.
- **Administrador da unidade:** configura tarifa, gerencia acessos, acompanha relatórios, autoriza exceções e investiga histórico.
- **Responsável de produto/negócio:** a validar com uma operação real; Alexandre Rech é entrevistado fictício, não stakeholder real confirmado.

## 5. Escopo e prioridades

**MVP — P0 (necessário para operar):** check-in com identificador único, dados do veículo e titular, vistoria textual de entrada; cadastro e atribuição de vaga livre; consulta por placa/identificador; solicitação e acompanhamento de retirada; vistoria textual de saída; cálculo de diárias, registro de pagamento e entrega com liberação da vaga; três perfis de acesso; histórico de ações e exceções. **MVP — P1 (gestão básica):** painel e relatórios por dia, semana e mês de receita, valores pendentes, atendimentos, períodos movimentados e ocupação, com comparação entre cobrado e pago. A implantação requer cadastro de vagas, usuários/perfis e tarifa antes do primeiro atendimento.

**Evoluções posteriores:** fotos de vistoria, múltiplas unidades, integrações de pagamento, comunicação automática com cliente e permissões customizadas; dependem de validação de valor e viabilidade.

**Fora do MVP:** processamento de pagamentos por provedor, autoatendimento do cliente, reconhecimento automático de placa, importação automática de planilhas e emissão fiscal integrada. O documento não presume obrigação fiscal específica.

## 6. Jornadas e casos de uso

1. **Preparação:** administrador cadastra vagas e tarifa e atribui perfis aos funcionários; sem tarifa ou vagas disponíveis não se conclui entrada que exige vaga.
2. **Entrada:** atendente registra placa, modelo, nome e número de documento do titular, horário e descrição textual das condições; seleciona vaga livre e recebe identificador único. Sistema impede atribuição simultânea da mesma vaga a dois atendimentos ativos; se dados obrigatórios faltarem, não conclui check-in.
3. **Localização:** equipe pesquisa por placa ou identificador, vê vaga e situação do veículo; resultados homônimos/placas repetidas não devem ser confundidos com um único atendimento.
4. **Pedido de retirada:** caixa localiza atendimento, confere presencialmente documento com foto com nome/número registrados e registra pedido; horário do pedido fixa o cálculo da permanência. Mesmo sem ticket, a busca por placa é possível; divergência documental bloqueia entrega normal e exige autorização excepcional do administrador, registrada com justificativa.
5. **Busca e cobrança:** fila mostra pedidos aguardando, em busca e entregues; equipe registra início da busca, caixa mostra tempo, diárias, valor e recebe pagamento integral. Sem pagamento integral, entrega só com autorização excepcional do administrador e saldo pendente identificado.
6. **Entrega:** registrar condições de saída em texto e responsável, confirmar entrega e horário; sistema libera vaga e encerra atendimento. Pedido duplicado, vaga já ocupada e tentativa de entrega sem conferência/quitação ou autorização geram bloqueio e mensagem explicativa.
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

**Regra transversal:** não alterar silenciosamente atendimentos encerrados, pagamentos ou históricos; correções financeiras são eventos auditados. O valor numérico da tarifa é configurado pelo administrador, não estabelecido na entrevista.

## 8. Requisitos não funcionais

| ID | Requisito / validação |
| --- | --- |
| RNF-01 | Autenticar funcionários e aplicar autorizações também nas operações de leitura/escrita do serviço, não apenas na interface; validar cenários de acesso indevido por perfil. |
| RNF-02 | Proteger dados pessoais e descrições de vistoria em trânsito e no armazenamento conforme política de segurança a ser definida; restringir exibição do número do documento ao pessoal autorizado e registrar acesso/alterações relevantes. Prazo de retenção e política de descarte: a validar com responsável pelo negócio e privacidade. |
| RNF-03 | Garantir consistência de atribuição/liberação de vagas, quitação e entrega sob ações simultâneas ou tentativas repetidas; teste de concorrência não deve produzir duas ocupações ativas na mesma vaga. |
| RNF-04 | Interface utilizável por teclado e com identificação textual de estados, erros e ações; validar acessibilidade com usuários da operação. Critério normativo e nível-alvo: a definir com produto/design. |
| RNF-05 | Busca, atualização da fila e visualização de vagas devem responder adequadamente em condições reais de operação; metas de tempo/carga a definir após medir volume de atendimentos e rede no local. |
| RNF-06 | Disponibilidade, recuperação de falhas e política de backup a definir antes da entrada em produção; não perder transações confirmadas e permitir identificar operações incompletas. |
| RNF-07 | Registrar falhas e eventos operacionais necessários para investigar erros de cobrança, acesso e transições, sem expor dados pessoais desnecessários em logs; política de monitoramento a definir. |

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

## 10. Dados e integrações

**Entidades principais:** funcionário/usuário e perfil; vaga e eventos de ocupação; atendimento (identificador, placa, modelo, titular, documento, tarifa aplicada, horários, estado); vistorias textuais de entrada/saída; pedido de retirada e estados; cobrança (tempo, diárias, valores e ajustes); pagamento (valor, forma, horário, operador); autorização excepcional (motivo, responsável); trilha de auditoria.

**Ciclo de vida:** atendimento criado na entrada → vaga ocupada → retirada solicitada (valor fixado) → busca → quitação ou autorização excepcional → vistoria de saída e entrega → vaga livre. Histórico de atendimentos concluídos e valores originais permanece consultável por perfis autorizados. Pendências financeiras excepcionais devem permanecer visíveis no relatório mesmo após entrega.

**Interfaces externas:** nenhuma integração obrigatória no MVP; dinheiro, cartão e Pix são **formas informadas manualmente**, não confirmação automática de liquidação. Hardware de tickets, gateway de pagamento, sistema fiscal e importação de dados ficam fora de escopo. O `README.md` lista tecnologias do projeto, mas este PRD não as transforma em requisitos de arquitetura.

**Restrições e dados desconhecidos:** valor inicial da tarifa, cadastro real de vagas/usuários, volume de tráfego, política de proteção/retensão de documentos, horário local da unidade e apresentação do identificador ao cliente precisam ser validados na implantação; a operação não deve iniciar sem tarifa e vagas cadastradas.

## 11. Riscos, premissas e dependências

| Ponto | Impacto | Mitigação / validação |
| --- | --- | --- |
| Fonte fictícia, sem observação real da operação | Regras podem não refletir a rotina | Validar jornadas, permissões e casos de exceção com operador e administrador reais antes de construir/lançar. |
| Vistoria somente textual | Menor evidência em disputas sobre danos | Validar qualidade do registro com equipe e revisar necessidade de fotos após piloto. |
| Documento do titular e conferência presencial | Exposição de dado pessoal ou atendimento bloqueado quando outra pessoa retira | Definir procedimentos operacionais de autorização, minimização e retenção com negócio/privacidade antes de operar. |
| Tarifa por 24 h iniciadas e valor fixado no pedido | Possível divergência da política comercial real | Validar política com responsável comercial e executar exemplos de fronteira antes do lançamento. |
| Pagamento manual e exceções de saldo | Divergência entre registro e dinheiro efetivamente recebido | Administrador faz conciliação com fechamento de caixa e audita autorizações/ajustes. |
| Disponibilidade de rede e atualização simultânea | Dupla ocupação ou atraso na fila | Validar conectividade no local; testar concorrência e definir plano de contingência antes de produção. |

## 12. Questões em aberto e próximos passos

As regras funcionais que admitiam interpretações distintas foram confirmadas com o solicitante nesta conversa. Permanecem dados de implantação e validações que não constituem autorização para inventar fatos:

1. **Administrador/negócio:** informar valor inicial da diária e confirmar política aprovada em operação real; validar exemplos limítrofes e descontos/estornos com equipe de caixa.
2. **Administrador/privacidade:** definir política de acesso, retenção e descarte de número de documento, vistorias e histórico; validar como tratar retirada por terceiro ou titular ausente mediante autorização excepcional.
3. **Administrador/operação:** fornecer mapa/cadastro de vagas, usuários e rotina de contingência para falhas de rede; testar fluxo completo com atendentes.
4. **Produto/UX:** definir formato de apresentação do identificador ao cliente, sem presumir ticket físico ou canal digital, e validar interface/acessibilidade com equipe.
5. **Produto/administrador:** medir baselines, volume de pico, horários e tempo de retirada em piloto; pactuar metas de sucesso, desempenho e disponibilidade antes de produção.
6. **Negócio/financeiro:** validar periodicidade do fechamento de caixa e eventual exigência de comprovante/documento fiscal; não incluídos como integração no MVP.
