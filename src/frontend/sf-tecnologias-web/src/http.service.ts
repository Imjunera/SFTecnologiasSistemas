import type { ApiResponse } from './contracts/error';

/**
 * HTTP service wrapper for window.api.request and fetch.
 * Provides typed methods for HTTP requests with automatic token injection.
 */
export class HttpService {
  static async get<T>(url: string, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>('GET', url, undefined, config);
  }

  static async post<T>(url: string, data: any, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>('POST', url, data, config);
  }

  static async put<T>(url: string, data: any, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>('PUT', url, data, config);
  }

  static async delete<T>(url: string, config: RequestInit = {}): Promise<ApiResponse<T>> {
    return this.request<T>('DELETE', url, undefined, config);
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
        token = sessionStorage.getItem('sf-access-token');
      }
    } catch {
      token = null;
    }

    const headers = new Headers({
      'Content-Type': 'application/json',
      ...(config.headers ?? {}),
    });

    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    // Temporary: Add tenant and user headers for endpoints without JWT auth
    // TODO: Remove this when JWT authentication is fixed
    const tenantId = sessionStorage.getItem('sf-empresa-id');
    const userId = sessionStorage.getItem('sf-usuario-id');
    if (tenantId) {
      headers.set('X-Tenant-Id', tenantId);
    }
    if (userId) {
      headers.set('X-User-Id', userId);
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

        // Handle 204 No Content (DELETE responses)
        if (response && response.status === 204) {
          return { status: 204, data: null, success: true } as ApiResponse<T>;
        }

        return response as ApiResponse<T>;
      } else {
        // Direct browser fallback for development
        const baseUrl = 'http://localhost:5000';
        const res = await fetch(baseUrl + url, {
          method,
          headers,
          body: data !== undefined ? JSON.stringify(data) : undefined,
          ...config,
        });

        const resData = await res.json().catch(() => null);
        return {
          status: res.status,
          data: resData,
          success: res.ok,
          message: res.ok ? 'Sucesso' : 'Erro na requisição',
          error: res.ok ? undefined : (resData?.error || resData?.title || 'Erro na requisição'),
        } as ApiResponse<T>;
      }
    } catch (err: any) {
      return {
        status: err.status ?? 500,
        data: null,
        success: false,
        error: err.message ?? 'Erro desconhecido',
        message: err.message ?? 'Erro desconhecido',
        details: err.details ?? null,
      } as ApiResponse<T>;
    }
  }
}
