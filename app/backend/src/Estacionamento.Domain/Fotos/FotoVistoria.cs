namespace Estacionamento.Domain.Fotos;

/// <summary>Tipos de vistoria que admitem fotos opcionais.</summary>
public static class TiposVistoriaFoto
{
    public const string Entrada = "entrada";
    public const string Saida = "saida";

    public static readonly IReadOnlyList<string> Todos = [Entrada, Saida];

    public static bool EhValido(string? tipo) =>
        string.Equals(tipo, Entrada, StringComparison.Ordinal) ||
        string.Equals(tipo, Saida, StringComparison.Ordinal);
}

/// <summary>Estados do metadado de foto. O binário vive no armazenamento privado.</summary>
public static class EstadosFotoVistoria
{
    public const string Pendente = "pendente";
    public const string Disponivel = "disponivel";
    public const string LimpezaPendente = "limpeza_pendente";

    /// <summary>Estado de descarte confirmado; o conteúdo não é mais exposto.</summary>
    public const string Descartada = "descartada";

    public static readonly IReadOnlyList<string> Todos =
        [Pendente, Disponivel, LimpezaPendente, Descartada];
}

/// <summary>
/// Metadados de uma foto anexada a uma vistoria. O binário nunca é persistido
/// neste registro, e a chave do objeto é opaca e imutável.
/// </summary>
public sealed class FotoVistoria
{
    public Guid Id { get; set; }
    public Guid AtendimentoId { get; set; }
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Vínculo exclusivo da vistoria de entrada, quando aplicável.</summary>
    public Guid? VistoriaEntradaId { get; set; }

    /// <summary>Vínculo exclusivo da vistoria de saída, quando aplicável.</summary>
    public Guid? VistoriaSaidaId { get; set; }

    /// <summary>Chave opaca e imutável do objeto no bucket privado.</summary>
    public string ChaveObjeto { get; set; } = string.Empty;

    public string Estado { get; set; } = EstadosFotoVistoria.Pendente;
    public string Mime { get; set; } = "image/webp";
    public long TamanhoBytes { get; set; }
    public int Largura { get; set; }
    public int Altura { get; set; }

    /// <summary>SHA-256 do WebP tratado que foi efetivamente armazenado.</summary>
    public byte[] HashSha256 { get; set; } = [];

    /// <summary>
    /// SHA-256 do arquivo recebido, usado apenas para comparar a repetição
    /// idempotente antes de processar novamente. Não é o conteúdo original.
    /// </summary>
    public byte[] HashEntradaSha256 { get; set; } = [];

    public Guid AtorId { get; set; }
    public DateTimeOffset CriadoEmUtc { get; set; }
    public DateTimeOffset? ConfirmadoEmUtc { get; set; }

    /// <summary>Lease da reserva; após expirar, a manutenção pode reconciliar.</summary>
    public DateTimeOffset? ReservadoAteUtc { get; set; }

    /// <summary>Versão-base do atendimento no momento da reserva.</summary>
    public byte[] VersaoAtendimentoBase { get; set; } = [];

    public Guid ResultadoIdempotenteId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Contador mensal compartilhado por ambiente. O consumo de outros ambientes e
/// cópias é registrado como linhas de reconciliação com o mesmo mês.
/// </summary>
public sealed class ConsumoFotosMensal
{
    public Guid Id { get; set; }

    /// <summary>Competência no formato <c>AAAA-MM</c>, em UTC.</summary>
    public string Competencia { get; set; } = string.Empty;

    public string Ambiente { get; set; } = string.Empty;
    public long BytesArmazenados { get; set; }
    public long BytesTransferidos { get; set; }
    public long OperacoesClasseA { get; set; }
    public long OperacoesClasseB { get; set; }
    public long BytesReservados { get; set; }

    /// <summary><c>conhecido</c> ou <c>desconhecido</c>.</summary>
    public string EstadoReconciliacao { get; set; } = "conhecido";

    public DateTimeOffset AtualizadoEmUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Tipo de trabalho durável de confirmação, limpeza ou retenção.</summary>
public static class TiposTarefaFoto
{
    public const string Confirmacao = "confirmacao";
    public const string Limpeza = "limpeza";
    public const string Retencao = "retencao";
}

/// <summary>
/// Tarefa retomável de manutenção de fotos. O lease evita que duas instâncias
/// processem o mesmo objeto ao mesmo tempo.
/// </summary>
public sealed class TarefaFoto
{
    public Guid Id { get; set; }
    public Guid FotoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Estado { get; set; } = "pendente";
    public DateTimeOffset CriadoEmUtc { get; set; }
    public DateTimeOffset? LeaseAteUtc { get; set; }
    public int Tentativas { get; set; }
    public string? UltimoErroCodigo { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Regras puras de janela e limite. O servidor é a autoridade; a interface
/// apenas antecipa o feedback.
/// </summary>
public static class RegraFotosVistoria
{
    public const int MaximoFotosPorVistoria = 5;

    public static bool EntradaAberta(bool temPedido, bool entregue) =>
        !temPedido && !entregue;

    public static bool SaidaAberta(bool vistoriaRegistrada, bool entregue) =>
        vistoriaRegistrada && !entregue;

    public static bool LimiteAtingido(int totalReservadoOuDisponivel) =>
        totalReservadoOuDisponivel >= MaximoFotosPorVistoria;

    public static bool PerfilPodeEnviar(string tipo, bool ehOperador) =>
        string.Equals(tipo, TiposVistoriaFoto.Entrada, StringComparison.Ordinal)
            ? ehOperador
            : true;
}
