import React, { useEffect, useState, useCallback } from "react";
import type { LoginResponse } from "./contracts/auth";
import { HttpService } from "./http.service";
import { H2Application } from "./modules/h2/components/h2-application";
import { UpdateNotification } from "./components/UpdateNotification";

interface UserInfo {
  nome: string;
  empresaId: number;
  empresaNome: string;
}

function decodeJwtPayload(token: string): Record<string, any> | null {
  try {
    const base64Url = token.split(".")[1];
    const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    return JSON.parse(jsonPayload);
  } catch {
    return null;
  }
}

export const App: React.FC = () => {
  const [token, setToken] = useState<string | null>(null);
  const [userInfo, setUserInfo] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const checkSession = async () => {
      try {
        let t: string | null = null;
        if ((window as any).api?.getToken) {
          t = await (window as any).api.getToken();
        } else {
          t = sessionStorage.getItem("sf-access-token");
        }
        if (t) {
          const payload = decodeJwtPayload(t);
          if (payload) {
            const user: UserInfo = {
              nome: payload.usuario_nome || "Usuario",
              empresaId: Number(payload.empresa_id) || 0,
              empresaNome: payload.empresa_nome || `Empresa #${payload.empresa_id}`,
            };
            setToken(t);
            setUserInfo(user);
          } else {
            sessionStorage.removeItem("sf-access-token");
            sessionStorage.removeItem("sf-user-info");
          }
        }
      } catch (err) {
        console.error("Error checking session:", err);
      } finally {
        setLoading(false);
      }
    };
    checkSession();
  }, []);

  const handleLogin = useCallback(
    async (empresaCodigo: string, senha: string, email?: string): Promise<string | null> => {
      try {
        let response: LoginResponse | undefined;
        if ((window as any).api?.login) {
          response = (await (window as any).api.login(
            empresaCodigo,
            senha,
            email
          )) as LoginResponse;
        } else {
          const result = await HttpService.post<LoginResponse>("/api/auth/login", {
            empresaCodigo,
            senha,
            email: email || undefined,
          });
          if (result.success && result.data?.accessToken) {
            sessionStorage.setItem("sf-access-token", result.data.accessToken);
            response = result.data;
          } else {
            return result.error || "Credenciais inválidas.";
          }
        }

        if (response?.accessToken) {
          setToken(response.accessToken);
          const payload = decodeJwtPayload(response.accessToken);
          const user: UserInfo = {
            nome: response.nome || "Usuário",
            empresaId: response.empresaId || 0,
            empresaNome: payload?.empresa_nome || `Empresa #${response.empresaId}`,
          };
          setUserInfo(user);
          sessionStorage.setItem("sf-user-info", JSON.stringify(user));
          // Notify Electron to resize window to main app
          if ((window as any).api?.loginSuccess) {
            await (window as any).api.loginSuccess();
          }
          return null;
        }
        return "Credenciais inválidas.";
      } catch (err: any) {
        return err?.message || "Falha na autenticação.";
      }
    },
    []
  );

  const handleLogout = async () => {
    try {
      if ((window as any).api?.logout) {
        await (window as any).api.logout();
      }
    } catch (err) {
      console.error("Logout error:", err);
    }
    sessionStorage.removeItem("sf-access-token");
    sessionStorage.removeItem("sf-user-info");
    setToken(null);
    setUserInfo(null);
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center bg-workspace text-foreground">
        <div className="flex items-center gap-3">
          <div className="size-6 animate-spin rounded-full border-2 border-primary border-t-transparent" />
          <span className="text-sm font-medium">Carregando SF Tecnologias...</span>
        </div>
      </div>
    );
  }

  if (token && userInfo) {
    return (
      <>
        <H2Application
          empresaNome={userInfo.empresaNome}
          usuarioNome={userInfo.nome}
          onLogout={handleLogout}
        />
        <UpdateNotification />
      </>
    );
  }

  return (
    <div className="flex h-full items-center justify-center bg-[#f0f0f0] p-4">
      <div className="w-full max-w-[360px] rounded-lg border border-[#d0d0d0] bg-white shadow-md">
        <div className="flex flex-col items-center px-8 pt-8 pb-6">
          <img
            src="./logo-sf.png"
            alt="SF Tecnologias"
            className="mb-4 h-20 w-auto object-contain"
          />
          <h1 className="text-lg font-bold tracking-tight text-[#1a1a2e]">SF Tecnologias</h1>
          <p className="mt-1 text-xs text-[#6b7280]">Acesso ao sistema</p>
        </div>
        <div className="border-t border-[#e5e7eb] px-8 pb-8 pt-6">
          <LoginForm onLogin={handleLogin} />
        </div>
      </div>
    </div>
  );
};

const LoginForm: React.FC<{
  onLogin: (empresaCodigo: string, senha: string, email?: string) => Promise<string | null>;
}> = ({ onLogin }) => {
  const [empresaCodigo, setEmpresaCodigo] = useState("");
  const [senha, setSenha] = useState("");
  const [email, setEmail] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const errorMsg = await onLogin(empresaCodigo, senha, email.trim() || undefined);
      if (errorMsg) {
        setError(errorMsg);
      }
    } catch (err: any) {
      setError(err?.message || "Erro ao efetuar login.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label
          className="mb-1 block text-xs font-semibold uppercase tracking-wide text-[#374151]"
          htmlFor="empresa-id"
        >
          EMPRESA_ID
        </label>
        <input
          id="empresa-id"
          type="text"
          value={empresaCodigo}
          onChange={(e) => setEmpresaCodigo(e.target.value)}
          placeholder="Ex: H2CONV"
          required
          disabled={loading}
          className="h-10 w-full rounded border border-[#d1d5db] bg-white px-3 text-sm text-[#1a1a2e] placeholder:text-[#9ca3af] focus:border-[#1e3a5f] focus:outline-none focus:ring-1 focus:ring-[#1e3a5f]"
        />
      </div>
      <div>
        <label
          className="mb-1 block text-xs font-semibold uppercase tracking-wide text-[#374151]"
          htmlFor="senha"
        >
          Senha
        </label>
        <input
          id="senha"
          type="password"
          value={senha}
          onChange={(e) => setSenha(e.target.value)}
          placeholder="••••••••"
          required
          disabled={loading}
          className="h-10 w-full rounded border border-[#d1d5db] bg-white px-3 text-sm text-[#1a1a2e] placeholder:text-[#9ca3af] focus:border-[#1e3a5f] focus:outline-none focus:ring-1 focus:ring-[#1e3a5f]"
        />
      </div>
      <div>
        <label
          className="mb-1 block text-xs font-semibold uppercase tracking-wide text-[#374151]"
          htmlFor="email"
        >
          E-mail (se houver mais de um usuário)
        </label>
        <input
          id="email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="opcional"
          disabled={loading}
          className="h-10 w-full rounded border border-[#d1d5db] bg-white px-3 text-sm text-[#1a1a2e] placeholder:text-[#9ca3af] focus:border-[#1e3a5f] focus:outline-none focus:ring-1 focus:ring-[#1e3a5f]"
        />
      </div>
      <button
        type="submit"
        disabled={loading}
        className="h-10 w-full rounded bg-[#1e3a5f] text-sm font-semibold uppercase tracking-wider text-white hover:bg-[#162d4a] active:bg-[#0f2038] disabled:opacity-50"
      >
        {loading ? "Autenticando..." : "Entrar"}
      </button>
      {error && (
        <div className="rounded border border-red-200 bg-red-50 p-2 text-center text-xs text-red-700">
          {error}
        </div>
      )}
    </form>
  );
};

export default App;
