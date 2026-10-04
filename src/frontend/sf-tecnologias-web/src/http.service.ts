import type { ApiResponse } from "./contracts/error";

/**
 * HTTP service wrapper for window.api.request and fetch.
 * Provides typed methods for HTTP requests with automatic token injection.
 */
export class HttpService {
  static async get<T>(url: string, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>("GET", url, undefined, config);
  }

  static async post<T>(url: string, data: any, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>("POST", url, data, config);
  }

  static async put<T>(url: string, data: any, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>("PUT", url, data, config);
  }

  static async delete<T>(url: string, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>("DELETE", url, undefined, config);
  }

  private static async request<T>(
    method: string,
    url: string,
    data: any | undefined,
    config: RequestInit = {}
  ): Promise<ApiResponse<T>> {
    let token: string | null = null;
    try {
      if ((window as any).api?.getToken) {
        token = await (window as any).api.getToken();
      } else {
        token = sessionStorage.getItem("sf-access-token");
      }
    } catch {
      token = null;
    }

    const headers = new Headers({
      "Content-Type": "application/json",
      ...(config.headers ?? {}),
    });

    if (token) {
      headers.set("Authorization", `Bearer ${token}`);
    }

    try {
      if ((window as any).api?.request) {
        const headerObj: Record<string, string> = {};
        headers.forEach((value, key) => {
          headerObj[key] = value;
        });

        const response = await (window as any).api.request({
          method,
          url,
          data,
          headers: headerObj,
        });

        return HttpService.normalizar<T>(response);
      } else {
        // Direct browser fallback for development
        const baseUrl = "http://localhost:5000";
        const res = await fetch(baseUrl + url, {
          method,
          headers,
          body: data !== undefined ? JSON.stringify(data) : undefined,
          ...config,
        });

        const resData = await res.json().catch(() => null);
        return HttpService.normalizar<T>({
          status: res.status,
          data: resData,
          success: res.ok,
          message: res.ok ? "Sucesso" : "Erro na requisição",
        });
      }
    } catch (err: any) {
      return {
        status: err.status ?? 500,
        data: null,
        success: false,
        error: err.message ?? "Erro desconhecido",
        message: err.message ?? "Erro desconhecido",
        details: err.details ?? null,
      } as ApiResponse<T>;
    }
  }

  /**
   * Uniformiza a resposta vinda do IPC (Electron) ou do fetch.
   *
   * O processo principal devolve { status, data, success } sem a chave `error` quando a
   * API responde 4xx/5xx com corpo (ex.: { error: "Ja existe um produto..." }). Sem esta
   * normalização, cada módulo caía no texto genérico e o operador nunca via o motivo real.
   * Quando não há mensagem no corpo, `error` fica undefined de propósito: assim cada tela
   * usa seu próprio fallback (ex.: "Credenciais inválidas.").
   */
  private static normalizar<T>(response: any): ApiResponse<T> {
    if (!response) {
      return {
        status: 500,
        data: null,
        success: false,
        error: "Sem resposta do servidor",
      } as ApiResponse<T>;
    }

    const status = response.status ?? 500;
    const success = response.success ?? (status >= 200 && status < 300);
    const body = response.data ?? null;

    // 204 No Content (DELETE)
    if (status === 204) {
      return { status, data: null, success: true } as ApiResponse<T>;
    }

    let error: string | undefined = response.error;
    if (!success && !error && typeof body === "object" && body !== null) {
      const candidato = body.error ?? body.detail ?? body.title ?? body.message;
      if (typeof candidato === "string" && candidato.trim()) error = candidato;
    }

    return {
      status,
      data: body,
      success,
      error,
      message: response.message,
      details: response.details ?? null,
    } as ApiResponse<T>;
  }
}
