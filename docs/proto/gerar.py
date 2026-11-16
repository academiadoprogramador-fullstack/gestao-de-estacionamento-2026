"""Gera cenas Excalidraw editáveis e prévias SVG a partir de dados sintéticos.

Executar com Python 3, sem dependências externas. Não acessa banco, serviços ou
credenciais e não representa uma aplicação implementada.
"""

from pathlib import Path
import html
import json
import textwrap


PASTA = Path(__file__).resolve().parent
COR_TEXTO = "#172554"
ELEMENTOS = []
CONTADOR = 0


def elemento(tipo, x, y, largura, altura, **campos):
    """Cria elementos nativos com IDs estáveis para edição no Excalidraw."""
    global CONTADOR
    CONTADOR += 1
    item = {
        "id": f"elemento-{CONTADOR:04d}",
        "type": tipo, "x": x, "y": y, "width": largura, "height": altura,
        "angle": 0, "strokeColor": COR_TEXTO, "backgroundColor": "transparent",
        "fillStyle": "solid", "strokeWidth": 1, "strokeStyle": "solid",
        "roughness": 0, "opacity": 100, "groupIds": [], "frameId": None,
        "roundness": None, "seed": CONTADOR * 1009, "version": 1,
        "versionNonce": CONTADOR * 17, "isDeleted": False, "boundElements": [],
        "updated": 0, "link": None, "locked": False,
    }
    item.update(campos)
    ELEMENTOS.append(item)
    return item


def caixa(x, y, largura, altura, cor="#f8fafc", borda="#94a3b8"):
    elemento("rectangle", x, y, largura, altura,
             backgroundColor=cor, strokeColor=borda, roundness={"type": 3})


def texto(x, y, conteudo, largura=1080, tamanho=18, cor=COR_TEXTO):
    limite = max(10, int(largura / (tamanho * 0.57)))
    linhas = []
    for linha in conteudo.split("\n"):
        linhas.extend(textwrap.wrap(linha, limite, break_long_words=False) or [""])
    valor = "\n".join(linhas)
    return elemento(
        "text", x, y, largura, len(linhas) * tamanho * 1.25,
        text=valor, originalText=valor, fontSize=tamanho, fontFamily=2,
        textAlign="left", verticalAlign="top", containerId=None,
        lineHeight=1.25, autoResize=True, strokeColor=cor,
    )


def cabecalho(cena, perfil):
    caixa(0, 0, 1200, 900, "#ffffff", "#ffffff")
    texto(32, 20, cena["titulo"], tamanho=30)
    texto(32, 64, f"PROTÓTIPO · {perfil} · dados sintéticos", tamanho=16, cor="#475569")
    caixa(32, 104, 1136, 48, "#dbeafe", "#2563eb")
    texto(48, 117, "Gestão de Estacionamento  /  " + perfil, tamanho=18)
    texto(32, 824, cena["requisitos"], tamanho=16, cor="#475569")
    texto(32, 854, "Wireframe de discussão. Não é interface executável nem evidência de conformidade.", tamanho=16, cor="#475569")


def desenhar_tela(cena):
    cabecalho(cena, cena["perfil"])
    caixa(32, 176, 728, 606)
    texto(52, 194, cena["rota"], largura=680, tamanho=18, cor="#475569")
    for i, linha in enumerate(cena["principal"]):
        y = 240 + i * 56
        caixa(52, y, 688, 50, "#ffffff", "#cbd5e1")
        texto(66, y + 3, linha, largura=660, tamanho=18)
    caixa(784, 176, 384, 382, "#eff6ff", "#93c5fd")
    texto(804, 194, "Regras e permissões", largura=344, tamanho=22)
    for i, linha in enumerate(cena["apoio"]):
        texto(804, 242 + i * 48, linha, largura=340, tamanho=18)
    caixa(784, 582, 384, 200, "#fff7ed", "#c2410c")
    texto(804, 600, "Estado / bloqueio", largura=340, tamanho=22)
    for i, linha in enumerate(cena["estado"]):
        texto(804, 646 + i * 55, linha, largura=340, tamanho=18)
    x = 52
    for acao in cena["acoes"]:
        largura = min(324, max(180, len(acao) * 10 + 28))
        caixa(x, 718, largura, 48, "#bfdbfe", "#2563eb")
        texto(x + 12, 731, acao, largura=largura - 24, tamanho=18)
        x += largura + 16


def desenhar_fluxo(cena):
    cabecalho(cena, "Fluxo de referência")
    for i, (titulo, detalhe) in enumerate(cena["etapas"]):
        linha = i // 3
        coluna = i % 3 if linha % 2 == 0 else 2 - i % 3
        x, y = 32 + coluna * 388, 188 + linha * 196
        caixa(x, y, 352, 152, "#eff6ff" if i < 8 else "#dcfce7", "#2563eb")
        texto(x + 16, y + 16, f"{i + 1}. {titulo}", largura=320, tamanho=22)
        texto(x + 16, y + 72, detalhe, largura=320, tamanho=18)
        if i < len(cena["etapas"]) - 1:
            if (i + 1) // 3 != linha:
                sx, sy, dx, dy = x + 176, y + 152, 0, 44
            elif linha % 2 == 0:
                sx, sy, dx, dy = x + 352, y + 76, 36, 0
            else:
                sx, sy, dx, dy = x, y + 76, -36, 0
            elemento("arrow", sx, sy, abs(dx), abs(dy),
                     points=[[0, 0], [dx, dy]], startBinding=None, endBinding=None,
                     startArrowhead=None, endArrowhead="arrow")
    texto(32, 778, cena["nota"], largura=1136, tamanho=16, cor="#475569")


def salvar(cena):
    """Exporta a mesma geometria como cena nativa e prévia SVG independente."""
    documento = {
        "type": "excalidraw", "version": 2,
        "source": "gestao-de-estacionamento/docs/proto",
        "elements": ELEMENTOS,
        "appState": {"viewBackgroundColor": "#ffffff", "gridSize": None},
        "files": {},
    }
    (PASTA / f'{cena["id"]}.excalidraw').write_text(
        json.dumps(documento, ensure_ascii=False, indent=2) + "\n")
    partes = [
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 900" role="img" aria-labelledby="titulo descricao">',
        f'<title id="titulo">{html.escape(cena["titulo"])}</title>',
        '<desc id="descricao">Protótipo com dados sintéticos. Consulte a cena editável e as regras no índice.</desc>',
        '<defs><marker id="seta" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0 0 L8 4 L0 8" fill="#172554"/></marker></defs>',
    ]
    for e in ELEMENTOS:
        if e["type"] == "rectangle":
            partes.append(f'<rect x="{e["x"]}" y="{e["y"]}" width="{e["width"]}" height="{e["height"]}" rx="8" fill="{e["backgroundColor"]}" stroke="{e["strokeColor"]}"/>')
        elif e["type"] == "text":
            partes.append(f'<text x="{e["x"]}" y="{e["y"] + e["fontSize"]}" font-family="Arial, sans-serif" font-size="{e["fontSize"]}" fill="{e["strokeColor"]}">')
            for i, linha in enumerate(e["text"].split("\n")):
                partes.append(f'<tspan x="{e["x"]}" dy="{0 if i == 0 else e["fontSize"] * 1.25}">{html.escape(linha)}</tspan>')
            partes.append("</text>")
        elif e["type"] == "arrow":
            dx, dy = e["points"][-1]
            partes.append(f'<path d="M{e["x"]} {e["y"]} l{dx} {dy}" fill="none" stroke="#172554" stroke-width="2" marker-end="url(#seta)"/>')
    partes.append("</svg>")
    (PASTA / f'{cena["id"]}.svg').write_text("\n".join(partes) + "\n")
    return {"id": cena["id"], "elements": ELEMENTOS.copy()}


def main():
    global ELEMENTOS, CONTADOR
    dados = json.loads((PASTA / "cenas.json").read_text())
    lotes = []
    for grupo, desenhar in (("telas", desenhar_tela), ("fluxos", desenhar_fluxo)):
        for cena in dados[grupo]:
            ELEMENTOS, CONTADOR = [], 0
            desenhar(cena)
            lotes.append(salvar(cena))
    # Payload de geometria para renderização via MCP, sem credenciais.
    (PASTA / "mcp-cenas.json").write_text(json.dumps(lotes, ensure_ascii=False) + "\n")
    print(f"{len(lotes)} cenas editáveis e {len(lotes)} prévias SVG geradas.")


if __name__ == "__main__":
    main()
