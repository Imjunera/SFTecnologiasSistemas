export interface Categoria {
  id: number;
  empresaId: number;
  nome: string;
  descricao?: string | null;
  ordem: number;
  ativo: boolean;
  criadoEm: string;
  atualizadoEm?: string | null;
}

export interface CriarCategoriaRequest {
  nome: string;
  descricao?: string | null;
  ordem?: number;
}

export interface AtualizarCategoriaRequest {
  nome: string;
  descricao?: string | null;
  ordem?: number;
  ativo: boolean;
}
