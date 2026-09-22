export type StatusMesa = "Livre" | "Ocupada" | "Reservada";

export interface Mesa {
  id: number;
  empresaId: number;
  numero: number;
  descricao?: string | null;
  capacidade: number;
  status: StatusMesa;
  ativo: boolean;
  criadoEm: string;
  atualizadoEm?: string | null;
}

export interface CriarMesaRequest {
  numero: number;
  descricao?: string | null;
  capacidade?: number;
}

export interface AtualizarMesaRequest {
  numero: number;
  descricao?: string | null;
  capacidade?: number;
  status?: StatusMesa;
  ativo: boolean;
}
