# Instruções para agentes

## Geral

- Sempre utilizar o sistema de questões do ambiente (harness) ao invés de fazer perguntas na resposta.
- Escrever documentação (legível pela linguagem utilizada, ex: <summary> no C#) e comentários técnicos durante a implementação quando aplicável.
- A nomenclatura do código deve ser feita em português brasileiro onde razoável, seguindo padrões recomendados pela comunidade.

## Fluxo de trabalho

Para novas funcionalidades e alterações de comportamento, executar o fluxo completo em uma **feature branch** criada a partir da `main`, seguindo a ordem **(PRD → TDD) → SDD → Pull Request**, sem pular etapas nem trabalhar diretamente na `main`:

1. **PRD (Product Requirements Document):** definir e validar problema, escopo, requisitos e critérios de aceite em `docs/prd.md` antes do desenho técnico.
2. **TDD (Technical Design Document):** registrar em `docs/tdd.md` as decisões de arquitetura, contratos, dados, segurança, testes e o roadmap de implementação, alinhados ao PRD, antes de especificar a mudança.
3. **SDD (Spec-Driven Development):** detalhar a mudança em especificações e tarefas rastreáveis ao PRD e ao TDD; revisar e validar essas especificações antes de implementar. Implementar e testar seguindo as tarefas aprovadas, mantendo documentação e especificações coerentes com eventuais decisões posteriores.
4. **Pull Request:** somente após implementação e verificações aplicáveis, abrir PR da feature branch para a `main` com o template de `.github/pull_request_template.md`, relacionando requisitos, decisões e especificações, descrevendo testes executados e pendências. Integrar na `main` por meio do PR, após revisão, aprovações e verificações exigidas; não fazer merge direto nem contornar proteções da branch.

Em mudanças com documentos existentes, reutilizar e atualizar os artefatos pertinentes em vez de recriá-los. Se uma etapa não se aplicar (por exemplo, correção puramente editorial), registrar a justificativa no PR; não tratar pendências de validação ou aprovação como concluídas.

## Pull Requests

- Abrir PRs da feature branch para a `main`, respeitando o fluxo de trabalho acima e mantendo cada PR focado em uma mudança coesa e revisável.
- Escrever título e descrição em português brasileiro. Utilizar título descritivo no padrão Conventional Commits: `<tipo>(<escopo>): <resumo>`, com escopo opcional; por exemplo, `feat(caixa): registrar pagamento integral`.
- Preencher o template de `.github/pull_request_template.md`, preservando as seções **Resumo**, **Rastreabilidade**, **Validação** e **Riscos e pendências**.
- Explicar o que mudou e por quê; relacionar requisitos do PRD, decisões/fase do TDD e especificações/tarefas SDD aplicáveis. Vincular issues relacionadas quando existirem e justificar explicitamente etapas não aplicáveis.
- Informar comandos, cenários e resultados das verificações realizadas. Não marcar checkboxes de validação sem evidência; explicar testes não executados e limitações.
- Descrever impactos, segurança, migrações, rollback e bloqueios quando pertinentes. Incluir capturas ou referências aos protótipos em alterações visuais, sem expor dados pessoais ou segredos.
- Manter a descrição e as evidências atualizadas após mudanças solicitadas na revisão. Integrar somente após as revisões, aprovações e verificações exigidas, sem contornar proteções.
- Antes de abrir ou marcar uma PR como **Ready for review**, validar seu formato, título, descrição conforme o template, rastreabilidade, evidências e labels informativas/contextuais. Se houver pendências nessa validação, corrigi-las antes de disponibilizar a PR.
- Nunca abrir ou deixar uma PR como **Draft**. Criar a PR diretamente como **Ready for review**, somente após a validação acima; se uma PR existente estiver em Draft, concluir a validação e convertê-la para Ready for review. Esse estado não significa aprovação nem dispensa revisões ou verificações exigidas.

### Labels informativas e contextuais

- Recomenda-se aplicar labels que ajudem a identificar **tipo**, **área afetada** e, quando relevante, **contexto ou risco** da mudança.
- Consultar primeiro as labels disponíveis no repositório e reutilizar sua nomenclatura. Exemplos conceituais: tipo (`documentação`, `funcionalidade`, `correção`), área (`frontend`, `backend`, `infraestrutura`) e contexto (`acessibilidade`, `segurança`, `migração`). Esses exemplos não pressupõem labels já cadastradas.
- Selecionar apenas labels sustentadas pelo conteúdo do PR; evitar excesso, redundância ou classificações genéricas que não ajudem na revisão. Usar labels de prioridade, risco ou status somente quando houver critério definido no projeto.
- Não criar, renomear ou excluir labels globais apenas para adequá-las a um PR sem autorização. Se faltar uma classificação útil, sugeri-la na descrição ou ao responsável pelo repositório.
- Reavaliar as labels se o escopo do PR mudar. Labels complementam a descrição e a rastreabilidade; não substituem evidências, revisão ou conclusão de tarefas.

## Commits

- Dividir as mudanças em commits pequenos, atômicos e focados, agrupando arquivos por unidade lógica ou tarefa. Evitar misturar documentação, refatorações e funcionalidades sem relação.
- Cada commit deve ser verificável e ter uma mensagem descritiva. Não fazer commit de trabalho incompleto que não passe nas verificações necessárias.
- Os commits devem ser escritos em português brasileiro, seguindo o padrão [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).
