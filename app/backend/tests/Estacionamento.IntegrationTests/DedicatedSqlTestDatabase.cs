using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Estacionamento.IntegrationTests;

internal static class DedicatedSqlTestDatabase
{
    public static SqlConnectionStringBuilder ParseAndValidate(string connectionString, string environmentVariable)
    {
        SqlConnectionStringBuilder parsed;
        try
        {
            parsed = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"{environmentVariable} is not a valid SQL Server connection string.", exception);
        }

        if (!Regex.IsMatch(parsed.InitialCatalog, @"^Estacionamento[A-Za-z0-9_-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(parsed.InitialCatalog, @"(Development|Local|Production|Prod|Dev)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"{environmentVariable} must target a dedicated Estacionamento* database; system and development catalogs are forbidden.");
        }

        return parsed;
    }
}
