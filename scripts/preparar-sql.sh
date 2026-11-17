#!/usr/bin/env bash
# Prepara, de forma idempotente, o login dedicado da API e suas permissões.
#
# O catálogo e o esquema NÃO são criados por SQL paralelo: em Development, a
# própria migration do EF (`Database.MigrateAsync`) cria o catálogo quando ele
# não existe e aplica o esquema. Para isso, o login local recebe a permissão
# `dbcreator` no SQL Server de desenvolvimento. A API nunca usa a conta `sa`.
#
# Entradas (exclusivamente por variáveis de ambiente; nada é gravado):
#   MSSQL_SA_PASSWORD  senha administrativa já usada pelo container
#   SQL_SENHA_API      senha do login dedicado da API
#   SQL_DATABASE       catálogo que a migration criará (padrão: Estacionamento)
#   SQL_USUARIO        login da API (padrão: estacionamento_api)
#   COMPOSE_PROJECT_NAME  projeto Compose alternativo para a instância de teste
#
# A senha administrativa é avaliada dentro do container; a senha da API entra
# por stdin do sqlcmd e nunca por argumentos de linha de comando.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARQUIVO_COMPOSE="${COMPOSE_FILE:-$RAIZ/compose.yml}"

: "${MSSQL_SA_PASSWORD:?Defina MSSQL_SA_PASSWORD no ambiente}"
: "${SQL_SENHA_API:?Defina SQL_SENHA_API no ambiente}"
SQL_DATABASE="${SQL_DATABASE:-Estacionamento}"
SQL_USUARIO="${SQL_USUARIO:-estacionamento_api}"

if [[ ! "$SQL_DATABASE" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "SQL_DATABASE aceita apenas letras, números e sublinhado." >&2
  exit 1
fi

if [[ ! "$SQL_USUARIO" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "SQL_USUARIO aceita apenas letras, números e sublinhado." >&2
  exit 1
fi

senha_api_sql="${SQL_SENHA_API//\'/\'\'}"
database_sql="${SQL_DATABASE//]/]]}"
usuario_sql="${SQL_USUARIO//]/]]}"

# Verifica se o serviço está saudável antes de preparar o login.
docker compose -f "$ARQUIVO_COMPOSE" exec -T sqlserver bash -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "SELECT 1" -o /dev/null'

# A senha administrativa é expandida dentro do container, não no host.
docker compose -f "$ARQUIVO_COMPOSE" exec -T sqlserver bash -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b' <<SQL
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'${usuario_sql}')
BEGIN
    CREATE LOGIN [${usuario_sql}] WITH PASSWORD = N'${senha_api_sql}', CHECK_POLICY = ON;
END
GO
IF IS_SRVROLEMEMBER(N'dbcreator', N'${usuario_sql}') = 0
    ALTER SERVER ROLE [dbcreator] ADD MEMBER [${usuario_sql}];
GO
-- Se o catálogo já existe (volume antigo), garante o usuário mapeado e os
-- papéis necessários. Se não existe, a migration o criará e o login será o
-- proprietário; nenhum banco é criado por este script.
IF DB_ID(N'${database_sql}') IS NOT NULL
BEGIN
    EXEC(N'USE [${database_sql}];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''${usuario_sql}'')
            CREATE USER [${usuario_sql}] FOR LOGIN [${usuario_sql}];
        IF IS_ROLEMEMBER(N''db_ddladmin'', N''${usuario_sql}'') = 0
            ALTER ROLE [db_ddladmin] ADD MEMBER [${usuario_sql}];
        IF IS_ROLEMEMBER(N''db_datareader'', N''${usuario_sql}'') = 0
            ALTER ROLE [db_datareader] ADD MEMBER [${usuario_sql}];
        IF IS_ROLEMEMBER(N''db_datawriter'', N''${usuario_sql}'') = 0
            ALTER ROLE [db_datawriter] ADD MEMBER [${usuario_sql}];');
END
GO
SQL

echo "Login '${SQL_USUARIO}' preparado com dbcreator. O catálogo '${SQL_DATABASE}' e o esquema serão criados/aplicados pela migration do EF."
