export type StatusSessaoCaixa = "Aberta" | "Fechada";

export interface SessaoCaixa {
  id: number;
  empresaId: number;
  usuarioId: number;
  mesaId?: number | null;
  mesaNumero?: number | null;
  dataAbertura: string;
  valorInicial: number;
  dataFechamento?: string | null;
  valorFechamento?: number | null;
  status: StatusSessaoCaixa;
}

export interface AbrirCaixaRequest {
  valorInicial: number;
  mesaId?: number | null;
}

export interface FecharCaixaRequest {
  valorFechamento: number;
}
