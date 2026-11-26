using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estacionamento.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Fase5FotosVistoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumosFotosMensais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Competencia = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Ambiente = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BytesArmazenados = table.Column<long>(type: "bigint", nullable: false),
                    BytesTransferidos = table.Column<long>(type: "bigint", nullable: false),
                    OperacoesClasseA = table.Column<long>(type: "bigint", nullable: false),
                    OperacoesClasseB = table.Column<long>(type: "bigint", nullable: false),
                    BytesReservados = table.Column<long>(type: "bigint", nullable: false),
                    EstadoReconciliacao = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AtualizadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumosFotosMensais", x => x.Id);
                    table.CheckConstraint("CK_ConsumosFotosMensais_Estado", "[EstadoReconciliacao] IN ('conhecido', 'desconhecido')");
                    table.CheckConstraint("CK_ConsumosFotosMensais_NaoNegativos", "[BytesArmazenados] >= 0 AND [BytesTransferidos] >= 0 AND [OperacoesClasseA] >= 0 AND [OperacoesClasseB] >= 0 AND [BytesReservados] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "FotosVistoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    VistoriaEntradaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VistoriaSaidaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChaveObjeto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Mime = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Largura = table.Column<int>(type: "int", nullable: false),
                    Altura = table.Column<int>(type: "int", nullable: false),
                    HashSha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    HashEntradaSha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReservadoAteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersaoAtendimentoBase = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ResultadoIdempotenteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosVistoria", x => x.Id);
                    table.CheckConstraint("CK_FotosVistoria_Dimensoes", "[Largura] > 0 AND [Altura] > 0 AND [TamanhoBytes] > 0");
                    table.CheckConstraint("CK_FotosVistoria_Estado", "[Estado] IN ('pendente', 'disponivel', 'limpeza_pendente', 'descartada')");
                    table.CheckConstraint("CK_FotosVistoria_Tipo", "[Tipo] IN ('entrada', 'saida')");
                    table.CheckConstraint("CK_FotosVistoria_Vinculo", "([Tipo] = 'entrada' AND [VistoriaEntradaId] IS NOT NULL AND [VistoriaSaidaId] IS NULL) OR ([Tipo] = 'saida' AND [VistoriaSaidaId] IS NOT NULL AND [VistoriaEntradaId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_FotosVistoria_AspNetUsers_AtorId",
                        column: x => x.AtorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FotosVistoria_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FotosVistoria_ResultadosIdempotentes_ResultadoIdempotenteId",
                        column: x => x.ResultadoIdempotenteId,
                        principalTable: "ResultadosIdempotentes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FotosVistoria_VistoriasEntrada_VistoriaEntradaId",
                        column: x => x.VistoriaEntradaId,
                        principalTable: "VistoriasEntrada",
                        principalColumn: "AtendimentoId");
                    table.ForeignKey(
                        name: "FK_FotosVistoria_VistoriasSaida_VistoriaSaidaId",
                        column: x => x.VistoriaSaidaId,
                        principalTable: "VistoriasSaida",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TarefasFotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseAteUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    UltimoErroCodigo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarefasFotos", x => x.Id);
                    table.CheckConstraint("CK_TarefasFotos_Estado", "[Estado] IN ('pendente', 'concluida', 'falha')");
                    table.CheckConstraint("CK_TarefasFotos_Tipo", "[Tipo] IN ('confirmacao', 'limpeza', 'retencao')");
                    table.ForeignKey(
                        name: "FK_TarefasFotos_FotosVistoria_FotoId",
                        column: x => x.FotoId,
                        principalTable: "FotosVistoria",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumosFotosMensais_Competencia_Ambiente",
                table: "ConsumosFotosMensais",
                columns: new[] { "Competencia", "Ambiente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_AtendimentoId_Tipo_Estado",
                table: "FotosVistoria",
                columns: new[] { "AtendimentoId", "Tipo", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_AtorId",
                table: "FotosVistoria",
                column: "AtorId");

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_ChaveObjeto",
                table: "FotosVistoria",
                column: "ChaveObjeto",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_Estado_ConfirmadoEmUtc",
                table: "FotosVistoria",
                columns: new[] { "Estado", "ConfirmadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_Estado_ReservadoAteUtc",
                table: "FotosVistoria",
                columns: new[] { "Estado", "ReservadoAteUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_ResultadoIdempotenteId",
                table: "FotosVistoria",
                column: "ResultadoIdempotenteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_VistoriaEntradaId",
                table: "FotosVistoria",
                column: "VistoriaEntradaId");

            migrationBuilder.CreateIndex(
                name: "IX_FotosVistoria_VistoriaSaidaId",
                table: "FotosVistoria",
                column: "VistoriaSaidaId");

            migrationBuilder.CreateIndex(
                name: "IX_TarefasFotos_Estado_LeaseAteUtc",
                table: "TarefasFotos",
                columns: new[] { "Estado", "LeaseAteUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TarefasFotos_FotoId_Tipo",
                table: "TarefasFotos",
                columns: new[] { "FotoId", "Tipo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumosFotosMensais");

            migrationBuilder.DropTable(
                name: "TarefasFotos");

            migrationBuilder.DropTable(
                name: "FotosVistoria");
        }
    }
}
