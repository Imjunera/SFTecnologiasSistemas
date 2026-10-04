import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Download,
  Loader2,
  RefreshCw,
  Rocket,
  Sparkles,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface UpdateInfo {
  available: boolean;
  currentVersion?: string;
  latestVersion?: string;
  manifestUrl?: string;
  releaseNotes?: string;
  releaseUrl?: string;
}

declare global {
  interface Window {
    api?: {
      checkForUpdates: () => Promise<UpdateInfo>;
      getVersion: () => Promise<string>;
      startUpdater: (manifestUrl: string) => Promise<{ success: boolean; error?: string }>;
      onUpdateAvailable: (callback: (info: UpdateInfo) => void) => () => void;
    };
  }
}

type Phase = "idle" | "checking" | "latest" | "error" | "available" | "updating";

/** Linhas das release notes em bullets/paragrafos estilizados. */
function ReleaseNotes({ notes }: { notes: string }) {
  const lines = notes
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  if (lines.length === 0) return null;

  return (
    <ul className="space-y-1.5">
      {lines.map((line, index) => {
        const isBullet = /^[-*•]/.test(line);
        const text = isBullet ? line.replace(/^[-*•]\s*/, "") : line;
        return (
          <li key={index} className="flex items-start gap-2 text-sm leading-relaxed text-slate-300">
            {isBullet ? (
              <span className="mt-[7px] h-1.5 w-1.5 shrink-0 rounded-full bg-gradient-to-br from-orange-400 to-rose-400" />
            ) : (
              <Sparkles className="mt-0.5 h-3.5 w-3.5 shrink-0 text-orange-300/80" />
            )}
            <span>{text}</span>
          </li>
        );
      })}
    </ul>
  );
}

export const UpdateNotification: React.FC = () => {
  const [phase, setPhase] = useState<Phase>("idle");
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const [currentVersion, setCurrentVersion] = useState<string>("");
  const [errorMessage, setErrorMessage] = useState<string>("");
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const safetyTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const primaryButtonRef = useRef<HTMLButtonElement>(null);

  const showToast = useCallback((nextPhase: Phase, message: string) => {
    setPhase(nextPhase);
    setErrorMessage(message);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => {
      setPhase((current) => (current === nextPhase ? "idle" : current));
    }, 5000);
  }, []);

  // Versão atual + escuta de atualizações empurradas pelo processo principal
  useEffect(() => {
    const getVersion = async () => {
      if (window.api?.getVersion) {
        const version = await window.api.getVersion();
        setCurrentVersion(version);
      }
    };
    getVersion();

    if (window.api?.onUpdateAvailable) {
      const unsubscribe = window.api.onUpdateAvailable((info: UpdateInfo) => {
        setUpdateInfo(info);
        if (info.available) setPhase("available");
      });
      return () => {
        unsubscribe();
      };
    }
  }, []);

  // Esc fecha o modal (exceto enquanto atualiza)
  useEffect(() => {
    if (phase !== "available") return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setPhase("idle");
    };
    window.addEventListener("keydown", onKeyDown);
    primaryButtonRef.current?.focus();
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [phase]);

  // Limpeza dos timers
  useEffect(
    () => () => {
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (safetyTimer.current) clearTimeout(safetyTimer.current);
    },
    []
  );

  const handleCheckForUpdates = async () => {
    if (!window.api?.checkForUpdates) return;
    setPhase("checking");
    try {
      const info = await window.api.checkForUpdates();
      setUpdateInfo(info);
      if (info.available) {
        setPhase("available");
      } else {
        showToast("latest", `Você está na versão mais recente${info.currentVersion ? ` (v${info.currentVersion})` : ""}`);
      }
    } catch (err) {
      showToast("error", `Não foi possível verificar atualizações: ${(err as Error).message}`);
    }
  };

  const handleUpdate = async () => {
    if (!updateInfo?.manifestUrl || !window.api?.startUpdater) return;
    setPhase("updating");

    // Segurança: se o app nao fechar (updater falhou em iniciar), volta ao modal
    if (safetyTimer.current) clearTimeout(safetyTimer.current);
    safetyTimer.current = setTimeout(() => {
      setPhase("available");
      setErrorMessage("O atualizador não respondeu. Tente novamente ou baixe manualmente.");
    }, 20000);

    const result = await window.api.startUpdater(updateInfo.manifestUrl);
    if (!result?.success) {
      if (safetyTimer.current) clearTimeout(safetyTimer.current);
      setPhase("available");
      setErrorMessage(`Falha ao iniciar o atualizador: ${result?.error || "erro desconhecido"}`);
    }
  };

  const displayedVersion = updateInfo?.currentVersion || currentVersion;

  // ---------- Botão flutuante (idle / checking / toasts) ----------
  if (phase === "idle" || phase === "checking" || phase === "latest" || phase === "error") {
    return (
      <div className="fixed bottom-4 right-4 z-50 flex flex-col items-end gap-2">
        {(phase === "latest" || phase === "error") && (
          <div
            role="status"
            aria-live="polite"
            className={cn(
              "sf-toast-in flex max-w-xs items-start gap-2.5 rounded-xl border px-3.5 py-2.5 shadow-window backdrop-blur-md",
              phase === "latest"
                ? "border-emerald-200 bg-emerald-50/95 text-emerald-800"
                : "border-red-200 bg-red-50/95 text-red-700"
            )}
          >
            {phase === "latest" ? (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            ) : (
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            )}
            <p className="text-sm font-medium leading-snug">{errorMessage}</p>
            <button
              onClick={() => setPhase("idle")}
              className="ml-1 rounded-md p-0.5 opacity-60 transition-opacity hover:opacity-100"
              aria-label="Fechar aviso"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        )}

        <button
          onClick={handleCheckForUpdates}
          disabled={phase === "checking"}
          title="Verificar atualizações"
          className={cn(
            "sf-fade-in group flex items-center gap-2 rounded-full border border-border/70 bg-card/80 py-2 pl-3 pr-3.5 text-sm font-medium text-muted-foreground shadow-window backdrop-blur-md transition-all",
            "hover:border-primary/40 hover:text-primary hover:shadow-lg active:scale-95",
            phase === "checking" && "cursor-wait opacity-80"
          )}
        >
          <span className="relative flex h-2 w-2">
            <span
              className={cn(
                "absolute inline-flex h-full w-full rounded-full sf-pulse-ring",
                phase === "checking" ? "bg-amber-400" : "bg-success"
              )}
            />
            <span
              className={cn(
                "relative inline-flex h-2 w-2 rounded-full",
                phase === "checking" ? "bg-amber-400" : "bg-success"
              )}
            />
          </span>
          {currentVersion ? `v${currentVersion}` : "Verificar atualizações"}
          {phase === "checking" ? (
            <Loader2 className="h-4 w-4 animate-spin text-primary" />
          ) : (
            <RefreshCw className="h-4 w-4 transition-transform duration-500 group-hover:rotate-180" />
          )}
        </button>
      </div>
    );
  }

  // ---------- Overlay de atualização em andamento ----------
  if (phase === "updating") {
    return (
      <div
        className="sf-fade-in fixed inset-0 z-[60] flex flex-col items-center justify-center gap-8 bg-slate-950/95 backdrop-blur-xl"
        role="alertdialog"
        aria-modal="true"
        aria-label="Atualizando o SF Tecnologias"
      >
        <div className="relative flex h-28 w-28 items-center justify-center">
          <div className="absolute inset-0 rounded-full border-2 border-transparent border-t-orange-400 border-r-rose-400 sf-orbit" />
          <div className="absolute inset-3 rounded-full border-2 border-transparent border-b-orange-300/70 sf-orbit-back" />
          <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-orange-500 to-rose-500 shadow-[0_0_45px_-5px] shadow-orange-500/50">
            <Rocket className="h-8 w-8 text-white" aria-hidden />
          </div>
        </div>

        <div className="flex flex-col items-center gap-2 text-center">
          <p className="text-lg font-semibold text-white">
            Preparando o SF Tecnologias {updateInfo?.latestVersion ? `v${updateInfo.latestVersion}` : ""}…
          </p>
          <p className="max-w-sm text-sm text-slate-400">
            O aplicativo será reiniciado automaticamente em instantes. Não desligue o computador.
          </p>
        </div>

        <div className="h-1.5 w-64 overflow-hidden rounded-full bg-slate-800">
          <div className="sf-shimmer h-full w-1/3 rounded-full bg-gradient-to-r from-transparent via-orange-400 to-transparent" />
        </div>
      </div>
    );
  }

  // ---------- Modal: atualização disponível ----------
  return (
    <div
      className="sf-fade-in fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-md"
      role="dialog"
      aria-modal="true"
      aria-label="Atualização disponível"
    >
      <div className="sf-modal-in relative w-full max-w-lg overflow-hidden rounded-3xl border border-white/10 bg-slate-900 shadow-2xl">
        {/* Faixa de brilho decorativa */}
        <div
          aria-hidden
          className="pointer-events-none absolute -top-24 left-1/2 h-48 w-[130%] -translate-x-1/2 rounded-full bg-gradient-to-r from-orange-500/25 via-rose-500/20 to-amber-400/25 blur-3xl"
        />

        {/* Cabeçalho */}
        <div className="relative flex items-start justify-between gap-4 px-7 pb-5 pt-7">
          <div className="flex items-center gap-3.5">
            <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-gradient-to-br from-orange-500 to-rose-500 shadow-lg shadow-rose-500/30">
              <Sparkles className="h-5 w-5 text-white" aria-hidden />
            </div>
            <div>
              <h2 className="text-lg font-semibold leading-tight text-white">Nova versão disponível</h2>
              <p className="text-sm text-slate-400">
                O SF Tecnologias ficou ainda melhor
              </p>
            </div>
          </div>
          <button
            onClick={() => setPhase("idle")}
            className="rounded-lg p-1.5 text-slate-500 transition-colors hover:bg-white/10 hover:text-white"
            aria-label="Fechar"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Transição de versão */}
        <div className="relative px-7">
          <div className="flex items-center justify-center gap-3 rounded-2xl border border-white/10 bg-white/[0.04] px-5 py-4">
            <div className="flex flex-col items-center">
              <span className="text-[10px] font-medium uppercase tracking-wider text-slate-500">Atual</span>
              <span className="text-sm font-semibold text-slate-400">
                {displayedVersion ? `v${displayedVersion}` : "—"}
              </span>
            </div>
            <div className="relative flex h-8 w-14 items-center justify-center">
              <ArrowRight className="h-5 w-5 text-orange-300" aria-hidden />
              <span
                aria-hidden
                className="sf-pulse-ring absolute h-2 w-2 rounded-full bg-orange-400"
                style={{ left: "calc(50% - 4px)" }}
              />
            </div>
            <div className="relative flex flex-col items-center rounded-xl bg-gradient-to-br from-orange-500/20 to-rose-500/20 px-3.5 py-1.5 ring-1 ring-orange-400/40">
              <span className="text-[10px] font-medium uppercase tracking-wider text-orange-300">Nova</span>
              <span className="bg-gradient-to-r from-orange-300 to-rose-300 bg-clip-text text-base font-bold text-transparent">
                v{updateInfo?.latestVersion}
              </span>
            </div>
          </div>
        </div>

        {/* Notas da versão */}
        <div className="relative px-7 pb-5 pt-4">
          <div className="max-h-40 overflow-y-auto rounded-2xl border border-white/5 bg-slate-950/60 px-4 py-3.5">
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500">
              <Sparkles className="h-3 w-3" aria-hidden /> Novidades desta versão
            </p>
            {updateInfo?.releaseNotes ? (
              <ReleaseNotes notes={updateInfo.releaseNotes} />
            ) : (
              <p className="text-sm text-slate-400">
                Correções, melhorias de desempenho e os recursos mais recentes do sistema.
              </p>
            )}
          </div>
        </div>

        {/* Erro do atualizador */}
        {errorMessage && (
          <div className="relative mx-7 mb-4 flex items-start gap-2.5 rounded-xl border border-destructive/40 bg-destructive/15 px-3.5 py-2.5 text-sm text-red-300">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{errorMessage}</span>
          </div>
        )}

        {/* Ações */}
        <div className="relative flex items-center gap-3 border-t border-white/10 bg-white/[0.03] px-7 py-4">
          <p className="mr-auto hidden items-center gap-1.5 text-xs text-slate-500 sm:flex">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400" />
            Seus dados ficam preservados
          </p>
          <Button
            variant="ghost"
            onClick={() => setPhase("idle")}
            className="text-slate-300 hover:bg-white/10 hover:text-white"
          >
            Mais tarde
          </Button>
          <Button
            ref={primaryButtonRef}
            onClick={handleUpdate}
            className="sf-glow bg-gradient-to-r from-orange-500 to-rose-500 text-white transition-transform hover:scale-[1.02] hover:from-orange-400 hover:to-rose-400"
          >
            <Download aria-hidden />
            Atualizar agora
          </Button>
        </div>
      </div>
    </div>
  );
};

export default UpdateNotification;
