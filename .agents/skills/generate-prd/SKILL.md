---
name: generate-prd
description: Gerar um documento de requisitos de produto (PRD) completo a partir de um prompt inicial. Use quando o usuário pedir um PRD, especificação de produto, ou invocar /generate-prd.
---

# Gerar PRD

Transforme a ideia inicial do usuário em um PRD em português, pronto para revisão por produto, design e engenharia. Este fluxo produz **somente documentação**: não escreva código, não inicie implementação e não crie uma mudança OpenSpec a menos que o usuário peça explicitamente.

## Entrada e descoberta

1. Use o prompt fornecido na conversa ou pelo comando `/generate-prd`. Se não houver uma ideia de produto identificável, pergunte o que o usuário quer criar antes de continuar.
2. Leia apenas o contexto relevante do projeto, incluindo **`docs/entrevista.md` quando existir e disser respeito ao produto**, além de `README.md`, requisitos existentes e documentação do domínio. Trate a entrevista como fonte de necessidades, não como especificação definitiva; identifique quem foi entrevistado, o que foi dito e o que ainda precisa de validação. Se o documento indicar que a entrevista é fictícia, registre essa limitação explicitamente no PRD. Não confunda comportamento atual com funcionalidade proposta. Não dependa de OpenSpec nem altere seus artefatos.
3. Faça uma **entrevista de esclarecimento com o usuário** antes de finalizar o artefato. Compare o prompt e as fontes com o escopo, as regras de negócio, os fluxos, os requisitos e os critérios de aceite pretendidos. Identifique especificações ausentes, conflitantes ou ambíguas que admitam interpretações diferentes; não escolha uma interpretação por conta própria. Use o **sistema de questões do ambiente** (ferramenta `question`, quando disponível) para perguntar de forma objetiva, agrupando questões relacionadas e oferecendo opções quando úteis. Faça rodadas adicionais se as respostas ainda deixarem especificações ambíguas. Em particular, esclareça regras de negócio citadas como pendentes na entrevista (por exemplo, cálculo de diárias e identificação do cliente) antes de descrevê-las como requisitos fechados.
4. Se o usuário não responder, ou se o sistema de questões não estiver disponível, pare e solicite as respostas; **não finalize nem entregue um PRD com especificações ambíguas disfarçadas de decisões**. Informações que não representem decisões de especificação e não possam ser verificadas (como métricas basais ou dados de mercado) podem ficar como `A definir`, com ação de validação. Não invente entrevistas, dados, obrigações legais, integrações ou decisões como se fossem fatos confirmados.
5. Por padrão, crie `docs/prd.md`. Use outro nome ou caminho somente se o usuário solicitar explicitamente. Se o caminho já existir, leia-o e peça confirmação antes de sobrescrever ou mesclar; não substitua trabalho existente silenciosamente.

## Estrutura do documento

Use Markdown com títulos claros e detalhe proporcional ao produto. Todas as seções abaixo devem aparecer; quando uma informação não puder ser determinada, registre `A definir` e a decisão pendente, em vez de preencher com afirmações fictícias.

1. **Cabeçalho**: nome do produto/funcionalidade, status `Rascunho`, data e origem do pedido.
2. **Resumo executivo**: proposta de valor, problema e resultado pretendido.
3. **Contexto e problema**: situação atual, evidências disponíveis, dores e oportunidades; diferencie fatos observados de hipóteses.
4. **Objetivos e métricas de sucesso**: objetivos mensuráveis, indicadores, definição de medição e metas sugeridas quando justificáveis; identifique baselines e metas pendentes de validação.
5. **Público-alvo e stakeholders**: perfis, necessidades, responsáveis e interessados conhecidos ou a validar.
6. **Escopo**: funcionalidades incluídas no MVP, evoluções posteriores e itens fora de escopo; prioridades e dependências importantes.
7. **Jornadas e casos de uso**: fluxos principais, alternativas relevantes e estados de erro/ausência de dados, do ponto de vista dos usuários.
8. **Requisitos funcionais**: itens identificados (`RF-01`, etc.), priorizados, específicos e verificáveis; indique regras de negócio e dependências quando necessário.
9. **Requisitos não funcionais**: itens identificados (`RNF-01`, etc.) sobre acessibilidade, segurança/privacidade, desempenho, disponibilidade, observabilidade e demais atributos pertinentes; não invente números ou compromissos normativos sem base.
10. **Critérios de aceite**: critérios testáveis por funcionalidade ou caso de uso, associados aos IDs dos requisitos, incluindo cenários de falha relevantes.
11. **Dados e integrações**: entidades e informações necessárias, ciclo de vida, interfaces externas e restrições conhecidas; marque desconhecidos.
12. **Riscos, premissas e dependências**: impacto, mitigação ou ação de validação para cada ponto relevante.
13. **Questões em aberto e próximos passos**: decisões pendentes com responsável sugerido quando possível e forma de validá-las.

## Revisão e entrega

- Verifique se o MVP é coerente com o problema, se os requisitos são rastreáveis aos critérios de aceite, se a entrevista foi considerada e se fatos, hipóteses e decisões pendentes estão distinguíveis. Antes de entregar, confirme que nenhuma especificação ambígua permaneceu sem esclarecimento pelo sistema de questões.
- Escreva o documento no caminho escolhido e confirme sua existência. Responda com o link do arquivo e um resumo breve das principais decisões e pendências; não apresente o texto inteiro na resposta, salvo se solicitado.
