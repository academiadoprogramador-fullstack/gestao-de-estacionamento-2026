using System.Data;
using System.Net.Mail;
using Estacionamento.Application.Auditoria;
using Estacionamento.Application.Comum;
using Estacionamento.Application.Funcionarios;
using Estacionamento.Application.Tempo;
using Estacionamento.Domain.Acesso;
using Estacionamento.Domain.Atendimentos;
using Estacionamento.Domain.Autenticacao;
using Estacionamento.Infrastructure.Identidade;
using Estacionamento.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Estacionamento.Infrastructure.Servicos;

/// <summary>
/// Administração de funcionários sem autorregistro. A criação usa Identity e
/// a alteração de acesso serializa a proteção do último administrador ativo,
/// incrementa a versão de acesso e revoga sessões/refreshes na mesma transação.
/// </summary>
public sealed class ServicoFuncionarios : IServicoFuncionarios
{
    private const string NomeAdministradorNormalizado = "ADMINISTRADOR";

    private readonly UserManager<UsuarioAplicacao> _usuarios;
    private readonly EstacionamentoDbContext _db;
    private readonly IAuditoriaNegocio _auditoria;
    private readonly IRelogio _relogio;

    public ServicoFuncionarios(
        UserManager<UsuarioAplicacao> usuarios,
        EstacionamentoDbContext db,
        IAuditoriaNegocio auditoria,
        IRelogio relogio)
    {
        _usuarios = usuarios;
        _db = db;
        _auditoria = auditoria;
        _relogio = relogio;
    }

    public async Task<FuncionarioResumo> CriarAsync(
        AtorAtual ator,
        RequisicaoCriarFuncionario requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var nome = (requisicao.Nome ?? string.Empty).Trim();
        var email = (requisicao.Email ?? string.Empty).Trim();
        var perfis = NormalizarPerfis(requisicao.Perfis);

        if (nome.Length is < 1 or > 200)
        {
            throw ExcecaoAplicacao.Validacao("nome_invalido", "O nome deve ter de 1 a 200 caracteres.");
        }

        if (!EmailValido(email))
        {
            throw ExcecaoAplicacao.Validacao("email_invalido", "Informe um e-mail com formato válido.");
        }

        if (string.IsNullOrEmpty(requisicao.SenhaInicial) || requisicao.SenhaInicial.Length < 8)
        {
            throw ExcecaoAplicacao.Validacao(
                "senha_invalida",
                "A senha inicial deve ter ao menos 8 caracteres.");
        }

        var agora = _relogio.UtcNow;
        var usuario = new UsuarioAplicacao
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Nome = nome,
            Ativo = true,
            VersaoAcesso = 1
        };

        await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var criacao = await _usuarios.CreateAsync(usuario, requisicao.SenhaInicial);
            if (!criacao.Succeeded)
            {
                await transacao.RollbackAsync(cancellationToken);
                throw CriacaoFalhou(criacao);
            }

            var atribuicao = await _usuarios.AddToRolesAsync(usuario, perfis);
            if (!atribuicao.Succeeded)
            {
                await transacao.RollbackAsync(cancellationToken);
                throw ExcecaoAplicacao.Validacao(
                    "perfis_invalidos",
                    "Não foi possível atribuir os perfis informados.");
            }

            _auditoria.Adicionar(new EventoAuditoriaNegocio(
                TiposAuditoriaNegocio.FuncionarioCriado,
                ator.Id,
                "funcionario",
                usuario.Id,
                agora,
                correlacao,
                null));

            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosSql.EhViolacaoUnicidade(excecao))
        {
            await transacao.RollbackAsync(cancellationToken);
            throw ExcecaoAplicacao.Conflito("email_em_uso", "Já existe uma conta com o e-mail informado.");
        }

        return new FuncionarioResumo(
            usuario.Id,
            usuario.Nome,
            usuario.Email ?? string.Empty,
            usuario.Ativo,
            perfis,
            TokenConcorrencia.Serializar(usuario.RowVersion));
    }

    public async Task<Pagina<FuncionarioResumo>> ListarAsync(
        AtorAtual ator,
        string? status,
        int pagina,
        int tamanho,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var consulta = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            consulta = status.Trim().ToLowerInvariant() switch
            {
                "ativo" => consulta.Where(usuario => usuario.Ativo),
                "inativo" => consulta.Where(usuario => !usuario.Ativo),
                _ => throw ExcecaoAplicacao.Validacao(
                    "status_invalido",
                    "O status deve ser 'ativo' ou 'inativo'.")
            };
        }

        var total = await consulta.CountAsync(cancellationToken);
        var usuarios = await consulta
            .OrderBy(usuario => usuario.Nome)
            .ThenBy(usuario => usuario.Id)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(usuario => new
            {
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Ativo,
                usuario.RowVersion
            })
            .ToListAsync(cancellationToken);

        var ids = usuarios.Select(usuario => usuario.Id).ToArray();
        var perfis = await (
                from vinculo in _db.UserRoles.AsNoTracking()
                join perfil in _db.Roles.AsNoTracking() on vinculo.RoleId equals perfil.Id
                where ids.Contains(vinculo.UserId)
                select new { vinculo.UserId, perfil.Name })
            .ToListAsync(cancellationToken);

        var perfisPorUsuario = perfis
            .GroupBy(item => item.UserId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => (IReadOnlyList<string>)grupo
                    .Select(item => item.Name ?? string.Empty)
                    .Where(nome => nome.Length > 0)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        return new Pagina<FuncionarioResumo>(
            usuarios.Select(usuario => new FuncionarioResumo(
                usuario.Id,
                usuario.Nome,
                usuario.Email ?? string.Empty,
                usuario.Ativo,
                perfisPorUsuario.GetValueOrDefault(usuario.Id, []),
                TokenConcorrencia.Serializar(usuario.RowVersion)))
                .ToArray(),
            pagina,
            tamanho,
            total);
    }

    public async Task<FuncionarioResumo> AlterarAcessoAsync(
        AtorAtual ator,
        Guid funcionarioId,
        RequisicaoAlterarAcesso requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        for (var tentativa = 0; tentativa < 3; tentativa += 1)
        {
            try
            {
                _db.ChangeTracker.Clear();
                return await AlterarAcessoUmaVezAsync(
                    ator,
                    funcionarioId,
                    requisicao,
                    correlacao,
                    cancellationToken);
            }
            catch (Exception excecao) when (tentativa < 2 && EhDeadlock(excecao))
            {
                // A transação foi revertida pelo SQL Server. Reexecutar a
                // unidade completa é seguro porque nenhum efeito foi commitado.
                await Task.Delay(20 * (tentativa + 1), cancellationToken);
            }
        }

        throw ExcecaoAplicacao.Conflito(
            "concurrency_conflict",
            "A alteração de acesso concorreu com outra operação. Recarregue e tente novamente.");
    }

    private async Task<FuncionarioResumo> AlterarAcessoUmaVezAsync(
        AtorAtual ator,
        Guid funcionarioId,
        RequisicaoAlterarAcesso requisicao,
        string? correlacao,
        CancellationToken cancellationToken = default)
    {
        if (!ator.EhAdministrador)
        {
            throw ExcecaoAplicacao.AcessoNegado();
        }

        var perfis = NormalizarPerfis(requisicao.Perfis);

        if (!TokenConcorrencia.TentarDesserializar(requisicao.TokenConcorrencia, out var token))
        {
            throw ExcecaoAplicacao.Validacao(
                "token_concorrencia_invalido",
                "Informe o token de concorrência corrente da conta.");
        }

        await using var transacao = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // A leitura bloqueante das contas administradoras ativas é feita antes
        // da conta alvo, em ordem estável, para serializar a proteção do último
        // administrador e evitar deadlocks entre alterações cruzadas.
        var administradoresAtivos = await _db.Database
            .SqlQueryRaw<Guid>(
                "SELECT u.[Id] AS [Value] FROM [AspNetUsers] AS u WITH (UPDLOCK, HOLDLOCK) " +
                "INNER JOIN [AspNetUserRoles] AS ur ON ur.[UserId] = u.[Id] " +
                "INNER JOIN [AspNetRoles] AS r ON r.[Id] = ur.[RoleId] " +
                "WHERE u.[Ativo] = 1 AND r.[NormalizedName] = {0} ORDER BY u.[Id]",
                NomeAdministradorNormalizado)
            .ToListAsync(cancellationToken);

        var usuario = await _db.Users
            .FromSqlInterpolated(
                $"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {funcionarioId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ExcecaoAplicacao.NaoEncontrado(
                "funcionario_nao_encontrado",
                "Funcionário não encontrado.");

        if (usuario.RowVersion.Length == 0 || !usuario.RowVersion.SequenceEqual(token))
        {
            throw ExcecaoAplicacao.Conflito(
                "stale_access_version",
                "O acesso da conta foi alterado. Recarregue o estado atual antes de tentar novamente.");
        }

        var eraAdministrador = await _usuarios.IsInRoleAsync(usuario, Perfis.Administrador);
        var continuaraAdministrador = requisicao.Ativo && perfis.Contains(Perfis.Administrador, StringComparer.Ordinal);

        if (eraAdministrador && usuario.Ativo && !continuaraAdministrador && administradoresAtivos.Count <= 1)
        {
            throw ExcecaoAplicacao.Conflito(
                "last_active_administrator",
                "A operação deixaria a unidade sem nenhum Administrador ativo.");
        }

        var atuais = await _usuarios.GetRolesAsync(usuario);
        var remover = atuais.Except(perfis, StringComparer.Ordinal).ToArray();
        var adicionar = perfis.Except(atuais, StringComparer.Ordinal).ToArray();

        if (remover.Length > 0)
        {
            var resultado = await _usuarios.RemoveFromRolesAsync(usuario, remover);
            if (!resultado.Succeeded)
            {
                throw ExcecaoAplicacao.Validacao("perfis_invalidos", "Não foi possível remover os perfis informados.");
            }
        }

        if (adicionar.Length > 0)
        {
            var resultado = await _usuarios.AddToRolesAsync(usuario, adicionar);
            if (!resultado.Succeeded)
            {
                throw ExcecaoAplicacao.Validacao("perfis_invalidos", "Não foi possível atribuir os perfis informados.");
            }
        }

        var agora = _relogio.UtcNow;
        usuario.Ativo = requisicao.Ativo;
        usuario.VersaoAcesso += 1;

        try
        {
            var atualizacao = await _usuarios.UpdateAsync(usuario);
            if (!atualizacao.Succeeded)
            {
                throw ExcecaoAplicacao.Conflito(
                    "stale_access_version",
                    "O acesso da conta foi alterado. Recarregue o estado atual.");
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ExcecaoAplicacao.Conflito(
                "stale_access_version",
                "O acesso da conta foi alterado. Recarregue o estado atual.");
        }

        await _db.SessoesAutenticacao
            .Where(sessao => sessao.UsuarioId == usuario.Id && sessao.RevogadaEmUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(sessao => sessao.RevogadaEmUtc, agora)
                    .SetProperty(sessao => sessao.CausaRevogacao, CausasRevogacao.AlteracaoAcesso),
                cancellationToken);

        await _db.RefreshTokens
            .Where(refresh =>
                refresh.RevogadoEmUtc == null &&
                _db.SessoesAutenticacao.Any(sessao => sessao.Id == refresh.SessaoId && sessao.UsuarioId == usuario.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(refresh => refresh.RevogadoEmUtc, agora),
                cancellationToken);

        _auditoria.Adicionar(new EventoAuditoriaNegocio(
            TiposAuditoriaNegocio.AcessoAlterado,
            ator.Id,
            "funcionario",
            usuario.Id,
            agora,
            correlacao,
            $"ativo:{usuario.Ativo};versao:{usuario.VersaoAcesso}"));

        await _db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);

        return new FuncionarioResumo(
            usuario.Id,
            usuario.Nome,
            usuario.Email ?? string.Empty,
            usuario.Ativo,
            perfis,
            TokenConcorrencia.Serializar(usuario.RowVersion));
    }

    private static IReadOnlyList<string> NormalizarPerfis(IReadOnlyList<string>? perfis)
    {
        var normalizados = (perfis ?? [])
            .Select(perfil => (perfil ?? string.Empty).Trim())
            .Where(perfil => perfil.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizados.Length == 0)
        {
            throw ExcecaoAplicacao.Validacao(
                "perfis_obrigatorios",
                "Informe ao menos um dos perfis fixos.");
        }

        if (normalizados.Any(perfil => !Perfis.Todos.Contains(perfil, StringComparer.Ordinal)))
        {
            throw ExcecaoAplicacao.Validacao(
                "perfis_invalidos",
                "Os perfis devem ser Administrador, Caixa ou Manobrista.");
        }

        return normalizados;
    }

    private static bool EmailValido(string email) =>
        email.Length > 0 &&
        !email.Contains(' ', StringComparison.Ordinal) &&
        MailAddress.TryCreate(email, out var endereco) &&
        string.Equals(endereco.Address, email, StringComparison.OrdinalIgnoreCase);

    private static ExcecaoAplicacao CriacaoFalhou(IdentityResult resultado)
    {
        var duplicado = resultado.Errors.Any(erro =>
            erro.Code is "DuplicateUserName" or "DuplicateEmail");

        return duplicado
            ? ExcecaoAplicacao.Conflito("email_em_uso", "Já existe uma conta com o e-mail informado.")
            : ExcecaoAplicacao.Validacao("dados_conta_invalidos", "Não foi possível criar a conta com os dados informados.");
    }

    private static bool EhDeadlock(Exception excecao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is SqlException sql && sql.Number == 1205)
            {
                return true;
            }
        }

        return false;
    }
}
