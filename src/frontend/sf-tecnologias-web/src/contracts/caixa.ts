export type StatusSessaoCaixa = "Aberta" | "Fechada";

export interface SessaoCaixa {
  id: number;
  empresaId: number;
  usuarioId: number;
  /** Nome do operador responsável pela sessão (retornado pela API). */
  usuarioNome?: string;
  mesaId?: number | null;
  mesaNumero?: number | null;
  dataAbertura: string;
  valorInicial: number;
  dataFechamento?: string | null;
  valorFechamento?: number | null;
  status: StatusSessaoCaixa;
  totalVendas: number;
  quantidadeVendas: number;
}

export interface AbrirCaixaRequest {
  valorInicial: number;
  mesaId?: number | null;
}

export interface FecharCaixaRequest {
  valorFechamento: number;
}
