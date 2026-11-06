# Proposal

## Why

O PRD aprovado e o TDD §8 exigem uma base verificável antes dos fluxos de entrada, retirada e cobrança. O repositório contém documentação e OpenSpec, mas ainda não contém a aplicação nem um caminho seguro de autenticação até a API; a Fase 1 estabelece e testa essa base localmente, sem antecipar implantação na Azure ou regras de negócio das fases seguintes.

## What Changes

- Criar os esqueletos Angular 22 e .NET 10 (`.slnx`, camadas de arquitetura limpa), com versões verificadas e fixadas e testes locais iniciais.
- Executar SPA, edge local e API separados com navegador acessando a API somente via `/api` na origem da SPA; testar cookies e CSRF em navegador real localmente.
- Estabelecer SQL Server local, migração inicial mínima de identidade, configuração de segredos e Data Protection persistente para testes de reinício, sem provisionar serviços em nuvem.
- Habilitar sessão Identity em cookie de primeira parte, login/logout/consulta de sessão, antiforgery inclusive para login/logout, perfis fixos e proteção padrão das rotas; demonstrar negação de acesso indevido.
- Registrar decisões e responsáveis pendentes de fuso, moeda, volumes, topologia Azure, acesso/retensão de dados e operação; manter testes de ingress, segredos gerenciados e publicação em homologação/produção como gates futuros.
- Exclui desta mudança provisionamento/publicação na Azure, IaC/CD de deploy, cadastro operacional de vagas/tarifas/usuários pela UI, check-in, retirada, pagamentos e relatórios (Fases seguintes).

## Capabilities

### New Capabilities

- `identity/employee-session`: sessão de funcionários, papéis, proteção das operações e CSRF na origem da SPA.
- `platform/web-api-edge`: acesso à API pelo proxy de mesma origem em ambiente local, preparado para isolamento de ingresso na implantação futura.
- `platform/environment-readiness`: base executável localmente, configuração segura, persistência mínima, testes automatizados e pendências explícitas de publicação.

### Modified Capabilities

Nenhuma; não existem specs principais ainda.

## Impact

Novos diretórios `frontend/`, `backend/`, `tests/e2e/` e configuração local de edge, API e SQL Server. CI não faz parte desta change e será tratado separadamente. Introduz contrato HTTP inicial `/api/v1` de sessão/antiforgery e dependências Angular 22, .NET 10 e Identity/EF Core SQL Server; serviços Azure ficam para mudança posterior. Alinha-se ao PRD RF-10, RNF-01/02/06/07 e à arquitetura-alvo do TDD DT-01–07/10/11, com critério de saída da Fase 1 ajustado para validação local.
