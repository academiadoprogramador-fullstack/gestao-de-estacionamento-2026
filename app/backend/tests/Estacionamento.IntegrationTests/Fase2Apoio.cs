using System.Net.Http.Json;
using System.Text.Json;
using Estacionamento.Application.Configuracao;

namespace Estacionamento.IntegrationTests;

/// <summary>
/// Utilitários dos cenários da Fase 2. Cada cliente mantém seu próprio bearer
/// em memória; o servidor continua sendo a autoridade de perfil.
/// </summary>
internal static class Fase2Apoio
{
    /// <summary>
    /// Fábrica isolada com limite de login elevado para os cenários da fase,
    /// que autenticam administrador, caixa e manobrista no mesmo teste.
    /// </summary>
    public static FabricaApiTeste NovaFabrica(
        Dictionary<string, string?>? configuracaoExtra = null)
    {
        var configuracao = new Dictionary<string, string?>
        {
            ["Autenticacao:LimiteTentativasLoginPorMinuto"] = "100"
        };

        if (configuracaoExtra is not null)
        {
            foreach (var (chave, valor) in configuracaoExtra)
            {
                configuracao[chave] = valor;
            }
        }

        return new FabricaApiTeste(configuracaoExtra: configuracao);
    }

    public static async Task<(HttpClient Cliente, RespostaSessao Sessao)> EntrarAsync(
        FabricaApiTeste fabrica,
        string email,
        string senha)
    {
        var cliente = fabrica.CriarCliente();
        var sessao = await ClienteSessao.LoginAsync(cliente, email, senha);
        ClienteSessao.UsarBearer(cliente, sessao.TokenAcesso);
        return (cliente, sessao);
    }

    public static async Task<(HttpClient Cliente, RespostaSessao Sessao)> EntrarAdministradorAsync(
        FabricaApiTeste fabrica) =>
        await EntrarAsync(
            fabrica,
            ContasTeste.Contas().Administrador!.Email,
            ContasTeste.Contas().Administrador!.Senha);

    public static async Task<(HttpClient Cliente, RespostaSessao Sessao)> EntrarCaixaAsync(
        FabricaApiTeste fabrica) =>
        await EntrarAsync(
            fabrica,
            ContasTeste.Contas().Caixa!.Email,
            ContasTeste.Contas().Caixa!.Senha);

    public static async Task<(HttpClient Cliente, RespostaSessao Sessao)> EntrarManobristaAsync(
        FabricaApiTeste fabrica) =>
        await EntrarAsync(
            fabrica,
            ContasTeste.Contas().Manobrista!.Email,
            ContasTeste.Contas().Manobrista!.Senha);

    public static async Task<JsonDocument> CriarVagaAsync(HttpClient administrador, string codigo)
    {
        var resposta = await administrador.PostAsJsonAsync("/api/v1/spaces", new { codigo });
        Assert.AreEqual(
            System.Net.HttpStatusCode.Created,
            resposta.StatusCode,
            await resposta.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    public static async Task PublicarTarifaAsync(
        HttpClient administrador,
        decimal valor,
        int? versaoBase = null)
    {
        var resposta = await administrador.PostAsJsonAsync(
            "/api/v1/daily-rates",
            new { valor, versaoBaseEsperada = versaoBase });
        Assert.AreEqual(
            System.Net.HttpStatusCode.Created,
            resposta.StatusCode,
            await resposta.Content.ReadAsStringAsync());
    }

    /// <summary>Cria vaga e tarifa mínimas para um check-in.</summary>
    public static async Task<Guid> PrepararEntradaAsync(
        HttpClient administrador,
        string codigoVaga = "A-01",
        decimal valor = 20.00m)
    {
        using var vaga = await CriarVagaAsync(administrador, codigoVaga);
        await PublicarTarifaAsync(administrador, valor);
        return vaga.RootElement.GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> RegistrarEntradaAsync(
        HttpClient operador,
        Guid vagaId,
        Guid? chave = null,
        string placa = "ABC1D23",
        string modelo = "Modelo Sintético",
        string titular = "Titular Sintético",
        string documento = "000.000.000-00",
        string vistoria = "Vistoria de entrada sintética.",
        object? extras = null)
    {
        var corpo = new Dictionary<string, object?>
        {
            ["placa"] = placa,
            ["modelo"] = modelo,
            ["titular"] = titular,
            ["documento"] = documento,
            ["vistoria"] = vistoria,
            ["vagaId"] = vagaId
        };

        if (extras is not null)
        {
            foreach (var propriedade in JsonSerializer.SerializeToElement(extras).EnumerateObject())
            {
                corpo[propriedade.Name] = propriedade.Value;
            }
        }

        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attendances")
        {
            Content = JsonContent.Create(corpo)
        };

        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave.Value.ToString());
        }

        return operador.SendAsync(requisicao);
    }

    public static async Task<JsonDocument> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());

    public static async Task<string?> CodigoDoErroAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            return documento.RootElement.TryGetProperty("code", out var codigo)
                ? codigo.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<Guid> ObterIdFuncionarioAsync(
        HttpClient administrador,
        string email)
    {
        var resposta = await administrador.GetAsync("/api/v1/employees?page=1&pageSize=100");
        Assert.AreEqual(
            System.Net.HttpStatusCode.OK,
            resposta.StatusCode,
            await resposta.Content.ReadAsStringAsync());

        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var item = documento.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(funcionario => string.Equals(
                funcionario.GetProperty("email").GetString(),
                email,
                StringComparison.OrdinalIgnoreCase));

        return item.GetProperty("id").GetGuid();
    }

    public static async Task<string> ObterTokenFuncionarioAsync(
        HttpClient administrador,
        string email)
    {
        var resposta = await administrador.GetAsync("/api/v1/employees?page=1&pageSize=100");
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var item = documento.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(funcionario => string.Equals(
                funcionario.GetProperty("email").GetString(),
                email,
                StringComparison.OrdinalIgnoreCase));

        return item.GetProperty("tokenConcorrencia").GetString()!;
    }

    public static async Task<Guid> ObterIdFuncionarioSinteticoAsync(
        HttpClient administrador,
        string email) =>
        await ObterIdFuncionarioAsync(administrador, email);

    public static string EmailAdministrador => ContasTeste.Contas().Administrador!.Email;

    public static string EmailCaixa => ContasTeste.Contas().Caixa!.Email;

    public static string EmailManobrista => ContasTeste.Contas().Manobrista!.Email;
}
