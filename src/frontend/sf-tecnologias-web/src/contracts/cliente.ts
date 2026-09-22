export interface Cliente {
  id: number;
  empresaId: number;
  nome: string;
  cpf?: string | null;
  telefone?: string | null;
  email?: string | null;
  endereco?: string | null;
  observacoes?: string | null;
  ativo: boolean;
  criadoEm: string;
  atualizadoEm?: string | null;
}

export interface CriarClienteRequest {
  nome: string;
  cpf?: string | null;
  telefone?: string | null;
  email?: string | null;
  endereco?: string | null;
  observacoes?: string | null;
}

export interface AtualizarClienteRequest {
  nome: string;
  cpf?: string | null;
  telefone?: string | null;
  email?: string | null;
  endereco?: string | null;
  observacoes?: string | null;
  ativo: boolean;
}
