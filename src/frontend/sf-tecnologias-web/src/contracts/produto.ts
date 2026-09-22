export interface Produto {
  id: number;
  empresaId: number;
  categoriaId?: number | null;
  categoriaNome?: string | null;
  nome: string;
  descricao?: string | null;
  codigo: string;
  precoVenda: number;
  precoCusto?: number | null;
  ativo: boolean;
  criadoEm: string;
  atualizadoEm?: string | null;
}

export interface CriarProdutoRequest {
  nome: string;
  descricao?: string | null;
  codigo: string;
  precoVenda: number;
  precoCusto?: number | null;
  categoriaId?: number | null;
}

export interface AtualizarProdutoRequest {
  nome: string;
  descricao?: string | null;
  codigo: string;
  precoVenda: number;
  precoCusto?: number | null;
  ativo: boolean;
  categoriaId?: number | null;
}

