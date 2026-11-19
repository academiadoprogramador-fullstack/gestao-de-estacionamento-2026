using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estacionamento.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Fase3RetiradaCaixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CodigoResposta",
                table: "ResultadosIdempotentes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultadoJson",
                table: "ResultadosIdempotentes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LancamentosFinanceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FormaPagamento = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    PagamentoOriginalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JustificativaCifrada = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OcorridoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosFinanceiros", x => x.Id);
                    table.CheckConstraint("CK_LancamentosFinanceiros_FormaVinculoJustificativa", "([Tipo] = 'pagamento' AND [FormaPagamento] IS NOT NULL AND [PagamentoOriginalId] IS NULL AND [JustificativaCifrada] IS NULL) OR ([Tipo] = 'estorno' AND [FormaPagamento] IS NULL AND [PagamentoOriginalId] IS NOT NULL AND [JustificativaCifrada] IS NOT NULL) OR ([Tipo] = 'desconto' AND [FormaPagamento] IS NULL AND [PagamentoOriginalId] IS NULL AND [JustificativaCifrada] IS NOT NULL)");
                    table.CheckConstraint("CK_LancamentosFinanceiros_Tipo", "[Tipo] IN ('desconto', 'pagamento', 'estorno')");
                    table.CheckConstraint("CK_LancamentosFinanceiros_ValorPositivo", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_AspNetUsers_AtorId",
                        column: x => x.AtorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_LancamentosFinanceiros_PagamentoOriginalId",
                        column: x => x.PagamentoOriginalId,
                        principalTable: "LancamentosFinanceiros",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PedidosRetirada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Conferencia = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TitularPresente = table.Column<bool>(type: "bit", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SolicitadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SolicitadoPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuscaIniciadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BuscaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosRetirada", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidosRetirada_AspNetUsers_BuscaPorId",
                        column: x => x.BuscaPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PedidosRetirada_AspNetUsers_SolicitadoPorId",
                        column: x => x.SolicitadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PedidosRetirada_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RazoesAtendimento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revisao = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RazoesAtendimento", x => x.Id);
                    table.CheckConstraint("CK_RazoesAtendimento_RevisaoPositiva", "[Revisao] >= 1");
                    table.ForeignKey(
                        name: "FK_RazoesAtendimento_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Cobrancas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DuracaoTicks = table.Column<long>(type: "bigint", nullable: false),
                    Diarias = table.Column<int>(type: "int", nullable: false),
                    TarifaVersaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TarifaValorSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalOriginal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cobrancas", x => x.Id);
                    table.CheckConstraint("CK_Cobrancas_DiariasMinimas", "[Diarias] >= 1");
                    table.CheckConstraint("CK_Cobrancas_TarifaNaoNegativa", "[TarifaValorSnapshot] >= 0");
                    table.CheckConstraint("CK_Cobrancas_TotalNaoNegativo", "[TotalOriginal] >= 0");
                    table.ForeignKey(
                        name: "FK_Cobrancas_PedidosRetirada_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "PedidosRetirada",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cobrancas_TarifasDiarias_TarifaVersaoId",
                        column: x => x.TarifaVersaoId,
                        principalTable: "TarifasDiarias",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConcessoesAutorizacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RevisaoReferencia = table.Column<int>(type: "int", nullable: false),
                    JustificativaCifrada = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ConcedidaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcedidaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcessoesAutorizacao", x => x.Id);
                    table.CheckConstraint("CK_ConcessoesAutorizacao_RevisaoPositiva", "[RevisaoReferencia] >= 1");
                    table.CheckConstraint("CK_ConcessoesAutorizacao_Tipo", "[Tipo] IN ('documental', 'financeira')");
                    table.ForeignKey(
                        name: "FK_ConcessoesAutorizacao_AspNetUsers_ConcedidaPorId",
                        column: x => x.ConcedidaPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConcessoesAutorizacao_PedidosRetirada_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "PedidosRetirada",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RevogacoesAutorizacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcessaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JustificativaCifrada = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    RevogadaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevogadaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevogacoesAutorizacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevogacoesAutorizacao_AspNetUsers_RevogadaPorId",
                        column: x => x.RevogadaPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RevogacoesAutorizacao_ConcessoesAutorizacao_ConcessaoId",
                        column: x => x.ConcessaoId,
                        principalTable: "ConcessoesAutorizacao",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cobrancas_PedidoId",
                table: "Cobrancas",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cobrancas_TarifaVersaoId",
                table: "Cobrancas",
                column: "TarifaVersaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConcessoesAutorizacao_ConcedidaPorId",
                table: "ConcessoesAutorizacao",
                column: "ConcedidaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConcessoesAutorizacao_PedidoId_Tipo_ConcedidaEmUtc",
                table: "ConcessoesAutorizacao",
                columns: new[] { "PedidoId", "Tipo", "ConcedidaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_AtendimentoId_OcorridoEmUtc",
                table: "LancamentosFinanceiros",
                columns: new[] { "AtendimentoId", "OcorridoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_AtorId",
                table: "LancamentosFinanceiros",
                column: "AtorId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_PagamentoOriginalId",
                table: "LancamentosFinanceiros",
                column: "PagamentoOriginalId",
                unique: true,
                filter: "[Tipo] = 'estorno'");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosRetirada_AtendimentoId",
                table: "PedidosRetirada",
                column: "AtendimentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PedidosRetirada_BuscaPorId",
                table: "PedidosRetirada",
                column: "BuscaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosRetirada_Estado_SolicitadoEmUtc_Id",
                table: "PedidosRetirada",
                columns: new[] { "Estado", "SolicitadoEmUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidosRetirada_SolicitadoPorId",
                table: "PedidosRetirada",
                column: "SolicitadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_RazoesAtendimento_AtendimentoId",
                table: "RazoesAtendimento",
                column: "AtendimentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevogacoesAutorizacao_ConcessaoId",
                table: "RevogacoesAutorizacao",
                column: "ConcessaoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevogacoesAutorizacao_RevogadaPorId",
                table: "RevogacoesAutorizacao",
                column: "RevogadaPorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cobrancas");

            migrationBuilder.DropTable(
                name: "LancamentosFinanceiros");

            migrationBuilder.DropTable(
                name: "RazoesAtendimento");

            migrationBuilder.DropTable(
                name: "RevogacoesAutorizacao");

            migrationBuilder.DropTable(
                name: "ConcessoesAutorizacao");

            migrationBuilder.DropTable(
                name: "PedidosRetirada");

            migrationBuilder.DropColumn(
                name: "CodigoResposta",
                table: "ResultadosIdempotentes");

            migrationBuilder.DropColumn(
                name: "ResultadoJson",
                table: "ResultadosIdempotentes");
        }
    }
}
