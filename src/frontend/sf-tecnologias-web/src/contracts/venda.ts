export interface VendaItem {
  id: number;
  produtoId?: number | null;
  produtoNome: string;
  quantidade: number;
  precoUnitario: number;
  subtotal: number;
}

export type StatusVenda = "Concluida" | "Cancelada";

export interface Venda {
  id: number;
  empresaId: number;
  usuarioId?: number | null;
  usuarioNome?: string | null;
  sessaoCaixaId?: number | null;
  clienteId?: number | null;
  clienteNome?: string | null;
  dataVenda: string;
  formaPagamento: string;
  valorTotal: number;
  status: StatusVenda;
  quantidadeItens: number;
  itens?: VendaItem[] | null;
}

export interface CriarVendaItemRequest {
  produtoId: number;
  quantidade: number;
}

export interface CriarVendaRequest {
  sessaoCaixaId?: number | null;
  clienteId?: number | null;
  formaPagamento: string;
  itens: CriarVendaItemRequest[];
}

export interface ResumoFormaPagamento {
  formaPagamento: string;
  quantidade: number;
  total: number;
}

export interface ResumoCaixa {
  sessaoId: number;
  mesaId?: number | null;
  mesaNumero?: number | null;
  dataAbertura: string;
  valorInicial: number;
  quantidadeVendas: number;
  totalVendas: number;
  valorEsperado: number;
  porFormaPagamento: ResumoFormaPagamento[];
}
