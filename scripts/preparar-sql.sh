#!/usr/bin/env bash
# Prepara, de forma idempotente, o catálogo e o usuário dedicado da API.
#
# Entradas (exclusivamente por variáveis de ambiente; nada é gravado):
#   MSSQL_SA_PASSWORD  senha administrativa já usada pelo container
#   SQL_SENHA_API      senha do usuário dedicado da API
#   SQL_DATABASE       catálogo (padrão: Estacionamento)
#   SQL_USUARIO        usuário da API (padrão: estacionamento_api)
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

# Verifica se o serviço está saudável antes de preparar o catálogo.
docker compose -f "$ARQUIVO_COMPOSE" exec -T sqlserver bash -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "SELECT 1" -o /dev/null'

# A senha administrativa é expandida dentro do container, não no host.
docker compose -f "$ARQUIVO_COMPOSE" exec -T sqlserver bash -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b' <<SQL
IF DB_ID(N'${database_sql}') IS NULL
BEGIN
    CREATE DATABASE [${database_sql}];
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'${usuario_sql}')
BEGIN
    CREATE LOGIN [${usuario_sql}] WITH PASSWORD = N'${senha_api_sql}', CHECK_POLICY = ON;
END
GO
USE [${database_sql}];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'${usuario_sql}')
BEGIN
    CREATE USER [${usuario_sql}] FOR LOGIN [${usuario_sql}];
END
GO
IF IS_ROLEMEMBER(N'db_ddladmin', N'${usuario_sql}') = 0
    ALTER ROLE [db_ddladmin] ADD MEMBER [${usuario_sql}];
IF IS_ROLEMEMBER(N'db_datareader', N'${usuario_sql}') = 0
    ALTER ROLE [db_datareader] ADD MEMBER [${usuario_sql}];
IF IS_ROLEMEMBER(N'db_datawriter', N'${usuario_sql}') = 0
    ALTER ROLE [db_datawriter] ADD MEMBER [${usuario_sql}];
GO
SQL

echo "Catálogo '${SQL_DATABASE}' e usuário '${SQL_USUARIO}' preparados (sem criar esquema de domínio)."
