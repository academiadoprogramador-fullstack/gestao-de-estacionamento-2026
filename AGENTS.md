# Instruções para agentes

## Geral

- Sempre utilizar o sistema de questões do ambiente (harness) ao invés de fazer perguntas na resposta.

## Fluxo de trabalho

Para novas funcionalidades e alterações de comportamento, executar o fluxo completo em uma **feature branch** criada a partir da `main`, seguindo a ordem **PRD → TDD → SDD → Pull Request**, sem pular etapas nem trabalhar diretamente na `main`:

1. **PRD (Product Requirements Document):** definir e validar problema, escopo, requisitos e critérios de aceite em `docs/prd.md` antes do desenho técnico.
2. **TDD (Technical Design Document):** registrar em `docs/tdd.md` as decisões de arquitetura, contratos, dados, segurança, testes e o roadmap de implementação, alinhados ao PRD, antes de especificar a mudança.
3. **SDD (Spec-Driven Development):** detalhar a mudança em especificações e tarefas rastreáveis ao PRD e ao TDD; revisar e validar essas especificações antes de implementar. Implementar e testar seguindo as tarefas aprovadas, mantendo documentação e especificações coerentes com eventuais decisões posteriores.
4. **Pull Request:** somente após implementação e verificações aplicáveis, abrir PR da feature branch para a `main` com o template de `.github/pull_request_template.md`, relacionando requisitos, decisões e especificações, descrevendo testes executados e pendências. Integrar na `main` por meio do PR, após revisão, aprovações e verificações exigidas; não fazer merge direto nem contornar proteções da branch.

Em mudanças com documentos existentes, reutilizar e atualizar os artefatos pertinentes em vez de recriá-los. Se uma etapa não se aplicar (por exemplo, correção puramente editorial), registrar a justificativa no PR; não tratar pendências de validação ou aprovação como concluídas.

## Commits

- Dividir as mudanças em commits pequenos, atômicos e focados, agrupando arquivos por unidade lógica ou tarefa. Evitar misturar documentação, refatorações e funcionalidades sem relação.
- Cada commit deve ser verificável e ter uma mensagem descritiva. Não fazer commit de trabalho incompleto que não passe nas verificações necessárias.
