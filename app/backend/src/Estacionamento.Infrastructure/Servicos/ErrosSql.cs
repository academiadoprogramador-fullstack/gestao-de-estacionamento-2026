using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Identificação de erros específicos do SQL Server sem expor a mensagem
/// original ao cliente.
/// </summary>
internal static class ErrosSql
{
    private const int ViolacaoChavePrimariaOuUnicidade = 2627;
    private const int ViolacaoIndiceUnico = 2601;
    private const int ViolacaoChaveEstrangeira = 547;

    public static bool EhViolacaoUnicidade(DbUpdateException excecao) =>
        excecao.InnerException is SqlException sql &&
        sql.Number is ViolacaoChavePrimariaOuUnicidade or ViolacaoIndiceUnico;

    public static bool EhViolacaoChaveEstrangeira(DbUpdateException excecao) =>
        excecao.InnerException is SqlException sql && sql.Number == ViolacaoChaveEstrangeira;

    /// <summary>
    /// Indica se a violação de unicidade ocorreu em um índice cujo nome contém
    /// o trecho informado. A mensagem do SQL é inspecionada apenas localmente.
    /// </summary>
    public static bool ViolacaoUnicidadeEm(DbUpdateException excecao, string trechoIndice) =>
        EhViolacaoUnicidade(excecao) &&
        excecao.InnerException!.Message.Contains(trechoIndice, StringComparison.OrdinalIgnoreCase);
}
