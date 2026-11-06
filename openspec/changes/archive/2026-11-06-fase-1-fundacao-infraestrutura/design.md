# Design

## Context

Ver `proposal.md` e `docs/tdd.md` §§3, 5, 6 e 8. Esta change limita a Fase 1 a execução e verificação locais; CI fica fora do escopo e poderá ser tratado em mudança separada. A arquitetura-alvo de sites separados e homologação Azure permanece como restrição/gate futuro.

## Goals / Non-Goals

**Goals:** Estabelecer uma fatia vertical executável localmente com sessão real e fronteira de mesma origem verificável no navegador. Deixar camadas e migração mínima preparadas para fases posteriores.

**Non-Goals:** Não definir esquema de atendimento/vagas/financeiro, implementar gestão administrativa de contas em UI ou simular fluxos futuros. Não criar IaC ou CD, provisionar recursos Azure, implantar em homologação/produção nem afirmar que testes locais comprovam isolamento de ingresso, autenticação entre sites e TLS na Azure.

## Decisions

1. **Estrutura e dependências (DT-01–05/10):** criar `backend/Estacionamento.slnx` com projetos Api, Application, Domain, Infrastructure e MSTest unitário/integração; `frontend/` Angular 22 standalone/zoneless, Signals e Signal Forms para tela de login; Tailwind/Lucide com controles nativos acessíveis; `tests/e2e/` Playwright. Api compõe Application/Infrastructure; Domain não depende de frameworks. Nesta fase, entidades persistentes apenas de Identity e configuração técnica mínima. Fixar versões compatíveis de .NET 10, EF Core/provider, Angular 22 e runners depois de verificar documentação e disponibilidade no início do apply; commit de manifests/lockfiles. Alternativa: API única com login servido por páginas de servidor; rejeitada pelo TDD que exige SPA. Validar Signal Forms na versão fixada, sem substituir a decisão aprovada silenciosamente.

2. **Topologia de teste local (DT-07):** executar site web com build Angular e proxy reverso sem domínio de negócio para `/api/*`, API em processo separado e SQL Server real isolado localmente. Usar origem HTTPS local confiável para exercitar cookie Secure no navegador; configurar upstream fixo/allowlist sem URLs dinâmicas do cliente, preservar `Cookie`, `Set-Cookie`, `/api` e antiforgery e impedir cache privado. O navegador não deve chamar API em outra origem; cabeçalhos de identidade não conferem privilégios e forwarded headers somente de proxy conhecido. Alternativa SPA chamando API cross-site via CORS: rejeitada por DT-07. **Não** tratar isolamento de portas locais como prova de ingress restrito em Azure; teste de rede/autenticação entre sites e escolha de serviço/SKU ficam para mudança posterior.

3. **Bootstrap de autenticação (DT-06/07):** ASP.NET Core Identity com SQL Server e papéis fixos, sem autorregistro; provisionamento inicial idempotente no startup somente em `Development`, com credenciais externas obrigatórias (nunca seed de senha no git). O comando administrativo local permanece disponível. Endpoints JSON `/api/v1/session/antiforgery` (GET anônimo, não cacheável), `/api/v1/session/login` (POST), `/api/v1/session` (GET), `/api/v1/session/logout` (POST) e uma operação de verificação restrita a administrador. Cookie host-only, Secure, HttpOnly, Path `/api`, SameSite apropriado ao acesso de mesma origem, política de expiração/bloqueio configurável. O GET antiforgery estabelece cookie/token de bootstrap antes do login; SPA envia o token em header nas mutações, reobtém quando necessário e não copia a sessão para storage. Middleware aplica validação antiforgery e origem esperada em **todos** os métodos mutáveis autenticados por cookie, inclusive login/logout; autorização deny-by-default com exceções explícitas. Rate limit de login, política de senhas e respostas indistinguíveis para credenciais inválidas. Alternativa JWT ou SameSite sem antiforgery: rejeitada por DT-06 e risco CSRF. Testes HTTP comprovam 401/403 e controle de perfis; UI guard é apenas conveniência.

4. **Dados e configuração (DT-05/09):** migração EF Core inicial somente com tabelas Identity e artefatos técnicos indispensáveis; nenhuma entidade operacional. SQL Server real isolado localmente; segredos por variáveis/User Secrets fora dos arquivos versionados. Persistir key ring Data Protection em armazenamento local durável por ambiente de teste, com proteção e permissões apropriadas para dados sintéticos; verificar reinício e não compartilhar chaves entre ambientes. Configurar conexão, origem esperada, upstream do proxy e chaves obrigatórias com falha explícita quando ausentes; em `Development`, migrar e provisionar em escopo antes de servir, sem aplicar migrações no boot em outros ambientes. Testes usam `Testing` e banco dedicado, sem User Secrets locais. Proteção por chaves gerenciadas, Azure SQL, restore e rotação em múltiplas instâncias são gates de implantação futura, não benefícios comprovados pelo teste local. Alternativa key ring efêmero: rejeitada por invalidar sessões após reinício. Criptografia de documento/vistoria permanece para quando houver esses dados.

5. **Entrega e comprovação (DT-10/11):** testes locais executam MSTest e integração com SQL Server real, Vitest e Playwright com edge local; autenticação/cookie, rejeição CSRF, papéis, cabeçalhos privados e ausência de chamadas cross-site são cobertos. CI no GitHub Actions fica fora desta change. Verificar bootstrap automático em `Development` e migração controlada em banco de teste vazio; sem jobs de deploy ou aprovação de migração de homologação nesta mudança. Evidências locais provam apenas a fronteira lógica; browser e ingress na Azure serão verificados quando houver ambiente publicado. Alternativa migrations automáticas fora de `Development`: rejeitada. Reservar smokes de check-in/pedido/entrega para fases posteriores.

6. **Pendências e gates (PRD §12/TDD §8):** registrar estado/owner a designar para fuso, moeda, volumes, domínios, produto/SKU/rede edge/API, política de acesso/retenção/descarte, backup/chaves, RPO/RTO e contingência. Testes locais usam dados sintéticos; esses dados não são decisões de implantação. Atualizar `docs/tdd.md` no início do apply para separar o marco local desta mudança do gate de homologação Azure, sem enfraquecer bloqueios de produção. Escolhas de serviço, segurança de rede e validações de navegador em Azure ficam expressamente pendentes de nova mudança. Alternativa inventar parâmetros finais: descartada.

## Risks / Trade-offs

- Proxy alterando host/caminho ou `Set-Cookie` → teste E2E com origem local segura e configuração de forward headers/allowlist; repetir em Azure na mudança de implantação.
- Teste local não comprova restrição de ingresso da API em nuvem → negar identidade falsificada em teste HTTP agora e manter teste externo/restrição de rede e autenticação entre serviços como gate futuro.
- Bootstrap antiforgery e login sem sessão conflitam → emitir token anônimo pela mesma origem, verificar POST de login/logout com e sem token, inclusive após expiração.
- Key ring perdido em reinício → armazenamento local durável de testes e ensaio de reinício; proteção gerenciada e restore serão verificados antes de publicação.
- Dependências novas e versões não confirmadas → verificar compatibilidade Angular 22/.NET 10 antes de fixar, registrar incompatibilidade e retornar para decisão sem trocar stack unilateralmente.
- TDD vigente prevê homologação Azure na Fase 1 → atualizar o roadmap em `docs/tdd.md` antes de implementar; registrar homologação/produção como não verificadas após conclusão local.

## Migration Plan

1. Na implementação, alinhar `docs/tdd.md` ao marco local e explicitar o gate Azure como etapa posterior.
2. Aplicar migração inicial Identity em SQL Server de teste vazio por comando explícito; em `Development`, migrar e provisionar contas sintéticas no startup com segredos externos.
3. Executar SPA/edge/API localmente e rodar os testes em navegador com origem segura; documentar evidências e comandos de reprodução.
4. Não executar migrações em Azure, deploy ou rollback de release nesta mudança. Planejar publicação e rollback compatível em mudança futura, sem apagar registros confirmados.
