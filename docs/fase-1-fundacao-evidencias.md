# Evidências da fundação — marco local

Este registro distingue verificações reproduzidas localmente de gates que ainda exigem execução/revisão fora desta sessão. Não representa aprovação de homologação ou produção.

## Verificações locais

- `dotnet build backend/Estacionamento.slnx --no-restore`: aprovado, sem warnings ou erros.
- MSTest contra SQL Server real: 4 testes arquiteturais e 15 testes de integração aprovados; os testes exercitam migrações, bootstrap local e sua ausência em `Testing`, Identity, sessão, autorização, antiforgery, logs, cache e Data Protection.
- Migração inicial em banco SQL Server de teste separado e vazio: aplicada e esquema técnico validado sem tabelas operacionais.
- `dotnet format Estacionamento.slnx --verify-no-changes --no-restore`: aprovado.
- Vitest: 4 testes aprovados; build Angular concluído.
- Playwright sobre SPA HTTPS e API em processos separados: 3 testes aprovados para abertura same-origin, login/logout, cookie host-only seguro, autorização e expiração.
- Teste de persistência do key ring: payload protegido continua legível após recriar o provider; permissões locais restritas verificadas.
- Verificação de vulnerabilidades NuGet: nenhum pacote vulnerável foi reportado pelas fontes atuais.

## Pendências de implantação ainda abertas

CI e sua execução não fazem parte desta change. Permanecem pendentes os responsáveis e critérios da matriz em `docs/pendencias-implantacao.md`, além de homologação Azure, topologia/ingress real, TLS entre serviços, decisões de privacidade e gates operacionais. Nenhum teste local ou dado sintético os aprova.
