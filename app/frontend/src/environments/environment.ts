/**
 * Configuração pública da SPA. Este arquivo é versionado e não pode conter
 * segredos: tudo aqui chega ao navegador.
 */
export const ambiente = {
  producao: false,
  /** URL-base HTTPS da API, em origem distinta da SPA. */
  apiUrl: 'https://localhost:4300',
  /** Origem local da SPA, usada apenas em mensagens e testes. */
  origemSpa: 'https://localhost:4200',
  /** Intervalo padrão do polling de vagas, configurável por ambiente. */
  intervaloPollingSegundos: 30,
} as const;

export function urlApi(caminho: string): string {
  const base = ambiente.apiUrl.replace(/\/$/, '');
  const relativo = caminho.startsWith('/') ? caminho : `/${caminho}`;
  return `${base}${relativo}`;
}
