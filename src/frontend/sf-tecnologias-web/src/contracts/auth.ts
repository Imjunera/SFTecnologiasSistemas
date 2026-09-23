export interface LoginRequest {
  empresaCodigo: string;
  senha: string;
  email?: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresIn: string; // ISO string from DateTime
  usuarioId: number;
  empresaId: number;
  nome: string;
  permissoes: string[];
}
