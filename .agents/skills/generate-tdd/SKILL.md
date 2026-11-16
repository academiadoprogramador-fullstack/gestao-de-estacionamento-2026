---
name: generate-tdd
description: Gerar um documento técnico de design (TDD) detalhado a partir de requisitos e contexto do projeto. Use quando o usuário pedir design técnico, arquitetura da solução ou invocar /generate-tdd; entrevista decisões de linguagem, frameworks, bibliotecas e estrutura antes de documentar.
---

# Gerar TDD (Technical Design Document)

Produza um **documento técnico de design**, não um plano de test-driven development. Centralize nele as decisões técnicas necessárias à implementação, em português e com detalhe suficiente para orientar engenharia e revisão. Este fluxo produz **somente documentação**: não escreva código, não instale dependências e não altere artefatos OpenSpec, salvo pedido explícito do usuário.

## Descoberta e decisões

1. Use o contexto da conversa ou de `/generate-tdd`. Se não houver sistema, funcionalidade ou objetivo técnico identificável, pergunte qual é o alvo antes de prosseguir.
2. Leia o contexto relevante existente: `README.md`, `docs/prd.md` ou outros PRDs/requisitos pertinentes, documentação técnica, `AGENTS.md` aplicável, configurações e código existentes quando houver. Separe fatos verificados, propostas do README, requisitos aprovados e hipóteses. Não trate badges, exemplos ou sugestões como escolhas técnicas confirmadas; não contradiga requisitos sem explicitar o conflito.
3. Faça uma **entrevista técnica com o usuário antes de finalizar**. Identifique lacunas e conflitos e pergunte sobre decisões que alterem o desenho ou a implementação. Use a ferramenta `question` do ambiente, quando disponível, agrupando perguntas relacionadas em rodadas curtas, com opções e implicações concisas. Pergunte, conforme relevância:
   - restrições existentes, prioridade dos requisitos, escala, ambiente de operação, equipe e critérios de escolha;
   - linguagem e versão/runtime por componente; frameworks e versões; bibliotecas principais (incluindo ORM, autenticação, validação, UI, testes), com justificativa e alternativas;
   - arquitetura e fronteiras entre componentes, organização de pastas/módulos, responsabilidades, comunicação e contratos;
   - persistência e modelagem, migrations, integrações, consistência/transações, concorrência e tratamento de falhas;
   - identidade, autorização, segurança/privacidade, observabilidade, deploy, configuração, CI/CD, backup e recuperação;
   - estratégia de testes e verificação dos requisitos não funcionais.
4. Não transforme uma lista de tecnologias encontrada no repositório em decisão definitiva. Para cada escolha significativa, mostre opções plausíveis e trade-offs adequados ao contexto, e peça a escolha do usuário ou sua autorização explícita para recomendar e decidir. Registre restrições confirmadas separadamente das decisões tomadas nesta conversa. Faça novas rodadas se respostas deixarem ambiguidades importantes; evite perguntas irrelevantes para o escopo.
5. Se o usuário não responder ou não houver ferramenta de perguntas, **pare e solicite as respostas**; não entregue um TDD que finja que decisões arquiteturais, linguagens, frameworks ou bibliotecas foram aprovadas. Dados ainda não mensuráveis podem ser `A definir`, com responsável e forma de validação; decisões bloqueantes não devem ser escondidas em uma seção de pendências. Se a tecnologia específica ou versão for determinante para o design, consulte documentação oficial/atual antes de descrever APIs ou compatibilidade; não invente suporte ou versões.
6. Por padrão, escreva em `docs/tdd.md`. Use outro nome ou caminho somente se o usuário solicitar explicitamente. Se o caminho já existir, leia-o e peça confirmação antes de sobrescrever ou mesclar; não substitua trabalho existente silenciosamente.

## Estrutura do documento

Use Markdown e **sugira diagramas Mermaid** para tornar o design mais fácil de revisar: diagrama de componentes ou contexto para fronteiras e dependências; ER para entidades e relacionamentos; sequência ou fluxograma para jornadas e integrações; estados para ciclos de vida. Inclua os diagramas que realmente esclareçam decisões confirmadas, com legendas ou explicações curtas, sem forçar todos os tipos quando não forem pertinentes. Se faltar uma decisão essencial para desenhar um diagrama, pergunte antes em vez de representar uma hipótese como arquitetura aprovada. Adapte a profundidade ao escopo sem omitir seções; marque o que não se aplica com justificativa. Referencie os IDs de requisitos do PRD, quando existirem. Evite pseudocódigo ou contratos inventados como se já estivessem aprovados.

1. **Cabeçalho e fontes**: nome, status (`Rascunho`), data, escopo, autores/decisores conhecidos, PRD/requisitos usados e divergências a resolver.
2. **Resumo e objetivos técnicos**: problema, restrições confirmadas, atributos de qualidade relevantes, limites e itens fora de escopo.
3. **Decisões técnicas centralizadas**: tabela com ID (`DT-01`, etc.), tema, decisão, estado (confirmada/pendente), origem/decisor, justificativa, alternativas e consequências. Inclua linguagem, runtime, framework e bibliotecas por componente, com versões somente quando confirmadas; diferencie o que já existia do que foi decidido na entrevista. Referencie os IDs nas seções seguintes; não espalhe escolhas contraditórias pelo documento.
4. **Arquitetura e estrutura**: visão de contexto/componentes, responsabilidades, fronteiras, organização de diretórios/módulos e dependências; explique por que o recorte atende ao escopo e como evolui.
5. **Dados e persistência**: entidades, relacionamentos, invariantes, estados e transições, índices/consultas importantes, migrações, retenção e recuperação; detalhe integridade e concorrência relevantes.
6. **Interfaces e integrações**: contratos de API/eventos, autenticação/autorização, entradas/saídas, erros, versionamento e dependências externas; para cada fluxo crítico, descreva sequência, falhas, retries/idempotência quando aplicáveis.
7. **Segurança e operação**: ameaças pertinentes, proteção de dados, segredos, configuração por ambiente, observabilidade, implantação, CI/CD, backup/restore e monitoramento, distinguindo o que está definido do que depende de validação.
8. **Qualidade e verificação**: estratégia de testes unitários, integração, contrato e ponta a ponta conforme aplicável; cenários de concorrência, segurança, acessibilidade e desempenho pertinentes; critérios de aceite técnicos rastreáveis aos requisitos.
9. **Roadmap de implementação (obrigatório em todo TDD)**: inclua sempre uma seção explícita de roadmap com fases incrementais em ordem de dependência, escopo e entregáveis de cada fase, pré-requisitos, critérios verificáveis de conclusão/passagem e marcos de MVP/P0, P1/piloto e produção quando aplicáveis. Vincule fases aos requisitos e às decisões relevantes, identifique validações e responsáveis por pendências que bloqueiam fases ou produção, e cubra migração/rollout e rollback quando cabíveis. Não reduza o roadmap a uma lista genérica de tarefas, não invente datas, duração ou recursos sem confirmação, nem confunda MVP funcional com autorização de produção. Mesmo que não haja cronograma definido, mantenha o roadmap por fases; sem implementar neste fluxo.
10. **Riscos, alternativas e pendências**: trade-offs, mitigação, perguntas não bloqueantes com responsável e condição de resolução; mapa de rastreabilidade entre requisitos, componentes, decisões e verificações.

## Revisão e entrega

- Revise coerência entre decisões, arquitetura, contratos, dados, testes, roadmap e PRD; valide que toda decisão estrutural relevante tenha origem/justificativa, que cada fase tenha entregáveis e critério de saída e que nenhuma tecnologia seja tratada como aprovada apenas por constar em documentação sugestiva.
- Antes de escrever, confirme que as decisões bloqueantes foram esclarecidas pelo usuário. Escreva o arquivo, confirme que existe e responda com seu link, resumo das decisões principais e pendências reais. Não reproduza o TDD inteiro na resposta, salvo se solicitado.
