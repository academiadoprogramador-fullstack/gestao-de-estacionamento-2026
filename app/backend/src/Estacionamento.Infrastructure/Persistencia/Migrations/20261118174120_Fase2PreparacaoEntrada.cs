using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estacionamento.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Fase2PreparacaoEntrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "SequenciaIdentificadorAtendimento");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AspNetUsers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "EventosAuditoriaNegocio",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlvoTipo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AlvoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OcorridoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Correlacao = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Detalhe = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAuditoriaNegocio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TarifasDiarias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PublicadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarifasDiarias", x => x.Id);
                    table.CheckConstraint("CK_TarifasDiarias_ValorPositivo", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_TarifasDiarias_AspNetUsers_PublicadoPorId",
                        column: x => x.PublicadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Vagas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CodigoNormalizado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vagas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vagas_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Atendimentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentificadorPublico = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Placa = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Modelo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Titular = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentoTitularCifrado = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TarifaVersaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TarifaValorSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EntradaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Atendimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Atendimentos_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Atendimentos_TarifasDiarias_TarifaVersaoId",
                        column: x => x.TarifaVersaoId,
                        principalTable: "TarifasDiarias",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracaoTarifa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    VersaoCorrenteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracaoTarifa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracaoTarifa_TarifasDiarias_VersaoCorrenteId",
                        column: x => x.VersaoCorrenteId,
                        principalTable: "TarifasDiarias",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventosAtendimento",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AtorOpaco = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OcorridoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Correlacao = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DetalheOpaco = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAtendimento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosAtendimento_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Ocupacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InicioUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FimUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ocupacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ocupacoes_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Ocupacoes_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Ocupacoes_Vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "Vagas",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ResultadosIdempotentes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operacao = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Chave = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersaoCanonicalizacao = table.Column<int>(type: "int", nullable: false),
                    HashSha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdentificadorPublico = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EntradaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultadosIdempotentes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResultadosIdempotentes_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResultadosIdempotentes_Vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "Vagas",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VistoriasEntrada",
                columns: table => new
                {
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextoCifrado = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VistoriasEntrada", x => x.AtendimentoId);
                    table.ForeignKey(
                        name: "FK_VistoriasEntrada_AspNetUsers_AtorId",
                        column: x => x.AtorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VistoriasEntrada_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ConfiguracaoTarifa",
                columns: new[] { "Id", "VersaoCorrenteId" },
                values: new object[] { 1, null });

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_EntradaUtc",
                table: "Atendimentos",
                column: "EntradaUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_IdentificadorPublico",
                table: "Atendimentos",
                column: "IdentificadorPublico",
                unique: true,
                filter: "[IdentificadorPublico] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_OperadorId",
                table: "Atendimentos",
                column: "OperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_Placa_EntradaUtc",
                table: "Atendimentos",
                columns: new[] { "Placa", "EntradaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_TarifaVersaoId",
                table: "Atendimentos",
                column: "TarifaVersaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracaoTarifa_VersaoCorrenteId",
                table: "ConfiguracaoTarifa",
                column: "VersaoCorrenteId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosAtendimento_AtendimentoId_OcorridoEmUtc",
                table: "EventosAtendimento",
                columns: new[] { "AtendimentoId", "OcorridoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoriaNegocio_AlvoTipo_AlvoId_OcorridoEmUtc",
                table: "EventosAuditoriaNegocio",
                columns: new[] { "AlvoTipo", "AlvoId", "OcorridoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Ocupacoes_AtendimentoId",
                table: "Ocupacoes",
                column: "AtendimentoId",
                unique: true,
                filter: "[FimUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ocupacoes_ResponsavelId",
                table: "Ocupacoes",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Ocupacoes_VagaId",
                table: "Ocupacoes",
                column: "VagaId",
                unique: true,
                filter: "[FimUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResultadosIdempotentes_AtendimentoId",
                table: "ResultadosIdempotentes",
                column: "AtendimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ResultadosIdempotentes_AtorId_Operacao_Chave",
                table: "ResultadosIdempotentes",
                columns: new[] { "AtorId", "Operacao", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResultadosIdempotentes_VagaId",
                table: "ResultadosIdempotentes",
                column: "VagaId");

            migrationBuilder.CreateIndex(
                name: "IX_TarifasDiarias_PublicadoPorId",
                table: "TarifasDiarias",
                column: "PublicadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_TarifasDiarias_Versao",
                table: "TarifasDiarias",
                column: "Versao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vagas_CodigoNormalizado",
                table: "Vagas",
                column: "CodigoNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vagas_CriadoPorId",
                table: "Vagas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_VistoriasEntrada_AtorId",
                table: "VistoriasEntrada",
                column: "AtorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracaoTarifa");

            migrationBuilder.DropTable(
                name: "EventosAtendimento");

            migrationBuilder.DropTable(
                name: "EventosAuditoriaNegocio");

            migrationBuilder.DropTable(
                name: "Ocupacoes");

            migrationBuilder.DropTable(
                name: "ResultadosIdempotentes");

            migrationBuilder.DropTable(
                name: "VistoriasEntrada");

            migrationBuilder.DropTable(
                name: "Vagas");

            migrationBuilder.DropTable(
                name: "Atendimentos");

            migrationBuilder.DropTable(
                name: "TarifasDiarias");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AspNetUsers");

            migrationBuilder.DropSequence(
                name: "SequenciaIdentificadorAtendimento");
        }
    }
}
