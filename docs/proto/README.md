# Protótipos de telas e fluxos

Wireframes de baixa fidelidade baseados em [PRD](../prd.md) e [TDD](../tdd.md).
`openspec/specs/` não contém especificações de funcionalidades; os documentos
consolidados são as fontes de requisitos desta entrega.

Todos os exemplos são sintéticos. Os desenhos não são interfaces implementadas,
não realizam ações e não demonstram acessibilidade, segurança ou testes concluídos.
Layout, agrupamentos e a navegação visual são propostas de apresentação, não
alterações das regras de negócio.

## Telas

| Tela | Prévia | Editável | Requisitos |
| --- | --- | --- | --- |
| Acesso da equipe | [SVG](01-acesso.svg) | [Excalidraw](01-acesso.excalidraw) | RF-10; RNF-01/04 |
| Registrar entrada | [SVG](02-entrada.svg) | [Excalidraw](02-entrada.excalidraw) | RF-01/02/07/10/11 |
| Consulta e fotos de entrada | [SVG](03-atendimentos.svg) | [Excalidraw](03-atendimentos.excalidraw) | RF-03/10/11/13 |
| Fila do manobrista | [SVG](04-fila.svg) | [Excalidraw](04-fila.excalidraw) | RF-02/05/10/11 |
| Pedido e conferência | [SVG](05-pedido.svg) | [Excalidraw](05-pedido.excalidraw) | RF-04/06/07/11 |
| Caixa e ajustes administrativos | [SVG](06-caixa.svg) | [Excalidraw](06-caixa.excalidraw) | RF-06/07/08/10/11 |
| Vistoria de saída e entrega | [SVG](07-entrega.svg) | [Excalidraw](07-entrega.excalidraw) | RF-02/09/11/13 |
| Equipe, vagas e tarifas | [SVG](08-administracao.svg) | [Excalidraw](08-administracao.excalidraw) | RF-02/07/10/11 |
| Relatórios administrativos | [SVG](09-relatorios.svg) | [Excalidraw](09-relatorios.excalidraw) | RF-12 |
| Exceções documentais/financeiras | [SVG](10-excecoes.svg) | [Excalidraw](10-excecoes.excalidraw) | RF-07/09/10/11 |
| Recuperação e indisponibilidade | [SVG](11-recuperacao.svg) | [Excalidraw](11-recuperacao.excalidraw) | RNF-01/03/06/07 |

## Fluxos

Siga a numeração e as setas: primeira linha para a direita, segunda para a
esquerda e terceira novamente para a direita.
Os fluxos de bloqueio e de fotos incluem caminhos alternativos descritos dentro
das etapas; não representam bypass de requisito nem confirmação automática.

| Fluxo | Prévia | Editável |
| --- | --- | --- |
| Atendimento da preparação à liberação | [SVG](12-fluxo-atendimento.svg) | [Excalidraw](12-fluxo-atendimento.excalidraw) |
| Bloqueios e exceções independentes | [SVG](13-fluxo-excecoes.svg) | [Excalidraw](13-fluxo-excecoes.excalidraw) |
| Fotos privadas, falhas e repetição segura | [SVG](14-fluxo-fotos-replay.svg) | [Excalidraw](14-fluxo-fotos-replay.excalidraw) |

## Ler e editar

- Abra uma prévia SVG no navegador ou no visualizador do repositório.
- Importe o arquivo `.excalidraw` correspondente no [Excalidraw](https://excalidraw.com).
- Textos e formas são editáveis; não há imagens de pessoas, documentos ou veículos reais.
- O perfil no topo indica a visão desenhada: a fila do manobrista não contém finanças,
  e caixa não herda as ações exclusivas do administrador.
- A tela de saída mostra texto **já salvo**; antes disso, salvar a vistoria é requisito
  para fotos e entrega. Fotos continuam opcionais e sua inclusão fecha nos marcos do PRD.
- As ações desenhadas são rótulos, não links ou uma aplicação clicável.

## Fontes e geração

`cenas.json` concentra textos/perfis/requisitos. `gerar.py` produz cenas nativas e
prévias SVG com a mesma geometria, sem dependências externas:

```sh
python3 docs/proto/gerar.py
```

As prévias SVG são exportações locais esquemáticas, não capturas de uma aplicação.
`mcp-cenas.json` contém os elementos para renderização no Excalidraw via MCP do
Composio. O formato foi consultado em `EXCALIDRAW_MCP_READ_ME`; a renderização remota
utiliza `EXCALIDRAW_MCP_CREATE_VIEW`, sem publicação de links públicos de colaboração.
Não enviar credenciais ou dados pessoais ao MCP.

As renderizações de revisão no MCP foram agrupadas em operação, gestão e fluxos,
com resumos das mesmas cenas. Os arquivos locais contêm os wireframes detalhados;
os SVGs não são capturas/exportações remotas do MCP. O registro das chamadas
concluídas está em [mcp-registro.json](mcp-registro.json); os checkpoints pertencem
à integração, não são links públicos nem substituem os arquivos locais.

Ao editar manualmente um `.excalidraw`, preserve o trabalho antes de regenerar:
o gerador sobrescreve os arquivos derivados. Mudanças nas regras exigem atualizar
primeiro PRD/TDD; o protótipo não encerra itens de implementação do roadmap.
