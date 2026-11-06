# Configuração local de desenvolvimento

A API encerra o startup antes de abrir qualquer endpoint quando uma configuração obrigatória estiver ausente ou inválida. Em desenvolvimento local, use o User Secrets da API (`backend/src/Estacionamento.Api/Estacionamento.Api.csproj`) para a conexão e senhas sintéticas; o `.env` do Docker Compose serve apenas ao contêiner SQL Server. Não coloque credenciais ou chaves em arquivos versionados. User Secrets não é criptografado e não deve guardar credenciais de produção.

| Chave de configuração (equivalente no ambiente) | Obrigatória | Uso |
| --- | --- | --- |
| `ConnectionStrings__Identity` | Sim | Conexão com banco SQL Server isolado para o ambiente atual. |
| `Security__SpaOrigin` | Sim | Origem HTTPS permitida para a SPA, sem caminho adicional. |
| `DataProtection__KeyRingPath` | Sim | Diretório persistente de Data Protection isolado por ambiente. |
| `MSSQL_SA_PASSWORD` | Ao iniciar SQL Server Compose | Senha local do SQL Server Developer; pode ser definida em `backend/.env` (ignorado pelo Git) ou no ambiente do shell. Não a compartilhe entre ambientes. |
| `TEST_SQLSERVER_CONNECTION` | Ao executar testes de integração | Conexão dedicada ao banco SQL Server de teste. |
| `TEST_MIGRATION_SQLSERVER_CONNECTION` | Ao verificar a migração inicial | Conexão para um banco SQL Server de teste vazio e isolado. |
| `TEST_VALET_PASSWORD`, `TEST_CASHIER_PASSWORD`, `TEST_ADMIN_PASSWORD` | Ao executar integração/E2E | Senhas sintéticas dos bancos de teste isolados; nunca use as senhas da base local de desenvolvimento. |
| `SyntheticAccounts__ValetPassword` | Ao provisionar conta sintética local | Senha fornecida por canal local seguro; nunca versionada. |
| `SyntheticAccounts__CashierPassword` | Ao provisionar conta sintética local | Senha fornecida por canal local seguro; nunca versionada. |
| `SyntheticAccounts__AdministratorPassword` | Ao provisionar conta sintética local | Senha fornecida por canal local seguro; nunca versionada. |

A duração de inatividade da sessão pode ser configurada por `Authentication__IdleTimeoutSeconds` (padrão local de 1200 segundos).

Em `Development`, o startup da API aplica migrações pendentes e provisiona idempotentemente as três contas/papéis sintéticos antes de abrir endpoints. Falha de conexão ou ausência de qualquer senha impede o startup. Senhas vêm do User Secrets/ambiente e não são redefinidas para contas já existentes. Fora de `Development` não há migração nem provisionamento automático; o comando legado `dotnet run --project backend/src/Estacionamento.Api -- --provision-synthetic-accounts` permanece disponível somente no ambiente local.

O `backend/docker-compose.yml` exige `MSSQL_SA_PASSWORD` e não fornece valor padrão. Para iniciar o banco com a senha definida em `backend/.env`, execute `cd backend && docker compose up -d`; o arquivo é local e não deve ser versionado. Uma variável já definida no shell tem precedência sobre o `.env`. Use a mesma senha na conexão da API; se o volume do banco já existir, alterar apenas o `.env` não altera a senha do SQL Server existente. A origem, o caminho do key ring e as conexões não possuem fallback silencioso. Configurações e chaves de teste devem permanecer separadas de quaisquer valores ou recursos de produção.

O projeto da API já tem `UserSecretsId`. Para configurar ou atualizar os valores locais (sem incluir segredos no repositório), execute de `backend/`:

```bash
dotnet user-secrets set 'ConnectionStrings:Identity' 'Server=localhost,14333;Database=EstacionamentoDev;User Id=sa;Password=SENHA_DO_ARQUIVO_BACKEND_DOT_ENV;Encrypt=True;TrustServerCertificate=True' --project src/Estacionamento.Api
dotnet user-secrets set 'Security:SpaOrigin' 'https://localhost:4200' --project src/Estacionamento.Api
dotnet user-secrets set 'DataProtection:KeyRingPath' "$HOME/.local/share/estacionamento/key-ring" --project src/Estacionamento.Api
dotnet user-secrets set 'SyntheticAccounts:ValetPassword' 'SENHA_LOCAL_FORTE' --project src/Estacionamento.Api
dotnet user-secrets set 'SyntheticAccounts:CashierPassword' 'SENHA_LOCAL_FORTE' --project src/Estacionamento.Api
dotnet user-secrets set 'SyntheticAccounts:AdministratorPassword' 'SENHA_LOCAL_FORTE' --project src/Estacionamento.Api
```

Substitua os exemplos por senhas distintas e fortes em cada máquina. `dotnet user-secrets list --project src/Estacionamento.Api` revela os valores em texto claro: evite usá-lo em logs ou capturas de tela. Inicie a API em `Development` com `dotnet run --project src/Estacionamento.Api --no-launch-profile --urls http://127.0.0.1:5188` (defina `ASPNETCORE_ENVIRONMENT=Development` no shell). Variáveis de ambiente com os nomes da tabela têm precedência sobre User Secrets.

O harness Playwright serve a SPA em `https://localhost:4200` e encaminha somente `/api/*` para o upstream fixo `http://127.0.0.1:5188`. O cabeçalho `X-Forwarded-Proto` é confiado pela API apenas por meio do proxy loopback de desenvolvimento. A porta da API é acessível localmente para testes diretos; esse arranjo não representa nem comprova restrição de ingresso entre sites numa implantação Azure.

## Revisão e aplicação controlada de migrações fora do bootstrap local

Em `Development`, as migrações são aplicadas automaticamente no startup, exclusivamente ao banco local configurado. Para revisar o SQL antes de atualizar um banco de teste e para migrações controladas fora desse ambiente, use os comandos explícitos abaixo com uma conexão de teste dedicada:

```bash
cd backend
dotnet tool restore
ConnectionStrings__Identity="$IDENTITY_TEST_CONNECTION" dotnet dotnet-ef migrations script --idempotent --project src/Estacionamento.Infrastructure/Estacionamento.Infrastructure.csproj --startup-project src/Estacionamento.Api/Estacionamento.Api.csproj
ConnectionStrings__Identity="$IDENTITY_TEST_CONNECTION" dotnet dotnet-ef database update --project src/Estacionamento.Infrastructure/Estacionamento.Infrastructure.csproj --startup-project src/Estacionamento.Api/Estacionamento.Api.csproj
```

Para verificar reconstrução em banco vazio, defina `TEST_MIGRATION_SQLSERVER_CONNECTION` apontando para um banco de teste dedicado e execute `dotnet test Estacionamento.slnx`. O teste aplica as migrações e confere que o esquema contém somente as tabelas técnicas do Identity. A execução CI usa instância SQL Server efêmera e base de migração separada; nunca aponte esses testes a dados de produção.

Os testes de integração iniciam a API em `Testing`, sem carregar User Secrets nem executar o bootstrap local; exigem `TEST_SQLSERVER_CONNECTION` e as senhas de teste acima. O harness E2E inicia a API em `Development` com banco de teste próprio e passa as três senhas via ambiente, exercitando também a migração e o provisionamento automáticos.
