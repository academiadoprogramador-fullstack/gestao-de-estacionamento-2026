const FORMATADOR_MOEDA = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
});

const FORMATADOR_DATA = new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short',
  timeStyle: 'short',
  timeZone: 'America/Sao_Paulo',
});

/** Formata valores monetários em BRL. */
export function formatarValor(valor: number | null | undefined): string {
  return valor === null || valor === undefined ? '—' : FORMATADOR_MOEDA.format(valor);
}

/** Apresenta um instante UTC no fuso America/Sao_Paulo. */
export function formatarDataHora(utc: string | null | undefined): string {
  if (!utc) {
    return '—';
  }

  const data = new Date(utc);
  return Number.isNaN(data.getTime()) ? '—' : FORMATADOR_DATA.format(data);
}
