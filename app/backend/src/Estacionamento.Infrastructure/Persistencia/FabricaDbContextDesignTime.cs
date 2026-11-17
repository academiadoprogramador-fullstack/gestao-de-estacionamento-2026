using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Estacionamento.Infrastructure.Persistencia;

/// <summary>
/// Fábrica de design-time usada somente por <c>dotnet ef</c>. Não executa o
/// bootstrap da API e não lê User Secrets; a conexão pode ser fictícia porque a
/// geração de migration não abre conexão com o banco.
/// </summary>
public sealed class FabricaDbContextDesignTime : IDesignTimeDbContextFactory<EstacionamentoDbContext>
{
    public EstacionamentoDbContext CreateDbContext(string[] args)
    {
        var conexao = Environment.GetEnvironmentVariable("ConnectionStrings__Estacionamento")
            ?? "Server=127.0.0.1,1433;Database=Estacionamento;User Id=design;Password=design;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<EstacionamentoDbContext>()
            .UseSqlServer(conexao, sql => sql.MigrationsAssembly(typeof(EstacionamentoDbContext).Assembly.FullName))
            .Options;

        return new EstacionamentoDbContext(options);
    }
}
