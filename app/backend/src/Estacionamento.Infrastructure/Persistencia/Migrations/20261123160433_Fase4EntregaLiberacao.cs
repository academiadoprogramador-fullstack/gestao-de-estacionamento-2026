using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estacionamento.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Fase4EntregaLiberacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VistoriasSaida",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextoCifrado = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VistoriasSaida", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VistoriasSaida_AspNetUsers_AtorId",
                        column: x => x.AtorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VistoriasSaida_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VistoriasSaida_PedidosRetirada_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "PedidosRetirada",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EntregasVeiculo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtendimentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VistoriaSaidaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OcupacaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntregueEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntregasVeiculo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_AspNetUsers_AtorId",
                        column: x => x.AtorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_Atendimentos_AtendimentoId",
                        column: x => x.AtendimentoId,
                        principalTable: "Atendimentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_Ocupacoes_OcupacaoId",
                        column: x => x.OcupacaoId,
                        principalTable: "Ocupacoes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_PedidosRetirada_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "PedidosRetirada",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_Vagas_VagaId",
                        column: x => x.VagaId,
                        principalTable: "Vagas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasVeiculo_VistoriasSaida_VistoriaSaidaId",
                        column: x => x.VistoriaSaidaId,
                        principalTable: "VistoriasSaida",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConsumosAutorizacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntregaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcessaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RevisaoReferencia = table.Column<int>(type: "int", nullable: false),
                    ConsumidaEmUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumosAutorizacao", x => x.Id);
                    table.CheckConstraint("CK_ConsumosAutorizacao_Tipo", "[Tipo] IN ('documental', 'financeira')");
                    table.ForeignKey(
                        name: "FK_ConsumosAutorizacao_ConcessoesAutorizacao_ConcessaoId",
                        column: x => x.ConcessaoId,
                        principalTable: "ConcessoesAutorizacao",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConsumosAutorizacao_EntregasVeiculo_EntregaId",
                        column: x => x.EntregaId,
                        principalTable: "EntregasVeiculo",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumosAutorizacao_ConcessaoId",
                table: "ConsumosAutorizacao",
                column: "ConcessaoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumosAutorizacao_EntregaId_Tipo",
                table: "ConsumosAutorizacao",
                columns: new[] { "EntregaId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_AtendimentoId",
                table: "EntregasVeiculo",
                column: "AtendimentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_AtorId",
                table: "EntregasVeiculo",
                column: "AtorId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_EntregueEmUtc",
                table: "EntregasVeiculo",
                column: "EntregueEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_OcupacaoId",
                table: "EntregasVeiculo",
                column: "OcupacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_PedidoId",
                table: "EntregasVeiculo",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_VagaId",
                table: "EntregasVeiculo",
                column: "VagaId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasVeiculo_VistoriaSaidaId",
                table: "EntregasVeiculo",
                column: "VistoriaSaidaId");

            migrationBuilder.CreateIndex(
                name: "IX_VistoriasSaida_AtendimentoId",
                table: "VistoriasSaida",
                column: "AtendimentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VistoriasSaida_AtorId",
                table: "VistoriasSaida",
                column: "AtorId");

            migrationBuilder.CreateIndex(
                name: "IX_VistoriasSaida_PedidoId",
                table: "VistoriasSaida",
                column: "PedidoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumosAutorizacao");

            migrationBuilder.DropTable(
                name: "EntregasVeiculo");

            migrationBuilder.DropTable(
                name: "VistoriasSaida");
        }
    }
}
