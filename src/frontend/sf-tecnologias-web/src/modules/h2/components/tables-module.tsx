import { useState, useEffect, useCallback, useRef } from "react";
import { Clock3, Users, X, Plus, Power, PowerOff, LayoutGrid, ShoppingCart, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { HttpService } from "@/http.service";
import type { Mesa, CriarMesaRequest, AtualizarMesaRequest, StatusMesa } from "@/contracts/mesa";

interface DraftMesa {
  numero: string;
  descricao: string;
  capacidade: string;
}

const emptyDraft: DraftMesa = { numero: "", descricao: "", capacidade: "4" };

const statusStyle: Record<StatusMesa, string> = {
  Livre: "border-success/40 bg-success-soft text-success-foreground",
  Ocupada: "border-primary/35 bg-brand-soft text-primary",
  Reservada: "border-warning/50 bg-warning-soft text-warning-foreground",
};

interface TablesModuleProps {
  onDirtyChange?: (dirty: boolean) => void;
  onAbrirCaixa?: (mesaId: number, mesaNumero: number) => void;
}

export function TablesModule({ onDirtyChange, onAbrirCaixa }: TablesModuleProps) {
  const [mesas, setMesas] = useState<Mesa[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [apenasAtivos, setApenasAtivos] = useState(true);
  const [selectedId, setSelectedId] = useState<number>();
  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<DraftMesa>(emptyDraft);
  const [errors, setErrors] = useState<Partial<DraftMesa>>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  const selected = mesas.find((m) => m.id === selectedId);

  useEffect(() => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedQuery(query), 300);
    return () => clearTimeout(debounceRef.current);
  }, [query]);

  const carregarMesas = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const params = new URLSearchParams();
      if (debouncedQuery) params.set("busca", debouncedQuery);
      if (apenasAtivos) params.set("apenasAtivos", "true");
      const res = await HttpService.get<Mesa[]>(`/api/mesas?${params.toString()}`);
      if (res.success && res.data) {
        setMesas(res.data);
      } else {
        setApiError(res.error || "Nao foi possivel carregar as mesas.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    } finally {
      setLoading(false);
    }
  }, [debouncedQuery, apenasAtivos]);

  useEffect(() => { carregarMesas(); }, [carregarMesas]);

  function openNew() {
    setDraft(emptyDraft);
    setErrors({});
    setApiError(null);
    setFormOpen(true);
    onDirtyChange?.(true);
  }

  function closeForm() {
    setFormOpen(false);
    setErrors({});
    setApiError(null);
    onDirtyChange?.(false);
  }

  async function handleSave() {
    const nextErrors: Partial<DraftMesa> = {};
    const numero = parseInt(draft.numero);
    if (!draft.numero.trim() || isNaN(numero) || numero <= 0) nextErrors.numero = "O numero e obrigatorio e deve ser maior que zero.";

    let capacidade: number | undefined;
    if (draft.capacidade.trim()) {
      capacidade = parseInt(draft.capacidade);
      if (isNaN(capacidade) || capacidade <= 0) nextErrors.capacidade = "A capacidade deve ser maior que zero.";
    }

    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;

    setSaving(true);
    setApiError(null);

    try {
      const payload: CriarMesaRequest = {
        numero,
        descricao: draft.descricao.trim() || null,
        capacidade,
      };
      const res = await HttpService.post<Mesa>("/api/mesas", payload);
      if (res.success) {
        closeForm();
        await carregarMesas();
      } else {
        setApiError(res.error || "Erro ao criar mesa.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro inesperado.");
    } finally {
      setSaving(false);
    }
  }

  async function handleStatusChange(mesa: Mesa, status: StatusMesa) {
    try {
      const payload: AtualizarMesaRequest = {
        numero: mesa.numero,
        descricao: mesa.descricao,
        capacidade: mesa.capacidade,
        status,
        ativo: mesa.ativo,
      };
      const res = await HttpService.put<Mesa>(`/api/mesas/${mesa.id}`, payload);
      if (res.success) {
        await carregarMesas();
      } else {
        setApiError(res.error || "Erro ao alterar status da mesa.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    }
  }

  async function handleInativar(mesa: Mesa) {
    if (!window.confirm(`Deseja realmente inativar a Mesa ${mesa.numero}?`)) return;
    try {
      const res = await HttpService.delete(`/api/mesas/${mesa.id}`);
      if (res.success) {
        setSelectedId(undefined);
        await carregarMesas();
      } else {
        setApiError(res.error || "Erro ao inativar mesa.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    }
  }

  async function handleExcluirPermanente(mesa: Mesa) {
    if (!window.confirm(`Deseja realmente EXCLUIR PERMANENTEMENTE a Mesa ${mesa.numero}? Esta ação não pode ser desfeita.`)) return;
    try {
      const res = await HttpService.delete(`/api/mesas/${mesa.id}/permanente`);
      if (res.success) {
        setSelectedId(undefined);
        await carregarMesas();
      } else {
        setApiError(res.error || "Erro ao excluir mesa permanentemente.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    }
  }

  return (
    <div className="flex h-full min-h-0 bg-background">
      <section className="flex min-w-0 flex-1 flex-col">
        <div className="flex flex-wrap items-end gap-3 border-b border-border bg-surface p-4">
          <div className="max-w-md flex-1">
            <label htmlFor="mesa-search-input" className="mb-2 block text-xs font-semibold uppercase text-muted-foreground">Buscar mesa</label>
            <div className="relative">
              <LayoutGrid className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="mesa-search-input"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Numero ou descricao"
                className="bg-background pl-9 shadow-none"
              />
            </div>
          </div>
          <label className="flex items-center gap-2 text-xs font-medium text-muted-foreground cursor-pointer pb-2">
            <input type="checkbox" checked={apenasAtivos} onChange={(e) => setApenasAtivos(e.target.checked)} className="rounded border-border" />
            Apenas ativos
          </label>
          <Button onClick={openNew} className="h-9 gap-1.5">
            <Plus className="size-4" />
            Nova mesa
          </Button>
        </div>

        {apiError && (
          <div className="mx-4 mt-3 rounded border border-destructive/40 bg-destructive/10 p-2.5 text-xs text-destructive">
            {apiError}
          </div>
        )}

        <div className="min-h-0 flex-1 overflow-auto p-5">
          {loading ? (
            <div className="flex h-48 items-center justify-center text-xs text-muted-foreground">Carregando mesas...</div>
          ) : mesas.length ? (
            <>
              <div className="mb-5 flex gap-4 text-xs">
                <span className="flex items-center gap-2"><i className="size-2.5 rounded-full bg-success" />Livre</span>
                <span className="flex items-center gap-2"><i className="size-2.5 rounded-full bg-primary" />Ocupada</span>
                <span className="flex items-center gap-2"><i className="size-2.5 rounded-full bg-warning" />Reservada</span>
              </div>
              <div className="grid grid-cols-3 gap-3 lg:grid-cols-4 xl:grid-cols-6">
                {mesas.map((mesa) => (
                  <Button
                    key={mesa.id}
                    variant="outline"
                    onClick={() => setSelectedId(mesa.id)}
                    className={`h-28 flex-col items-stretch justify-between whitespace-normal p-3 text-left shadow-none ${statusStyle[mesa.status]} ${selectedId === mesa.id ? "ring-2 ring-ring ring-offset-2" : ""}`}
                  >
                    <span className="flex w-full justify-between">
                      <strong className="text-base">Mesa {String(mesa.numero).padStart(2, "0")}</strong>
                      <span className="text-xs font-semibold">{mesa.status}</span>
                    </span>
                    <span className="flex w-full items-center justify-between text-xs font-normal">
                      <span className="flex items-center gap-1"><Users className="size-3.5" />{mesa.capacidade}</span>
                      {mesa.descricao && <span className="truncate max-w-20">{mesa.descricao}</span>}
                    </span>
                  </Button>
                ))}
              </div>
            </>
          ) : (
            <div className="flex h-64 flex-col items-center justify-center text-center">
              <LayoutGrid className="mb-3 size-8 text-muted-foreground/60" />
              <p className="text-sm font-medium">Nenhuma mesa cadastrada</p>
              <p className="mt-1 text-xs text-muted-foreground">Clique em "Nova mesa" para adicionar a primeira mesa.</p>
            </div>
          )}
        </div>
      </section>

      {selected && (
        <aside className="w-80 shrink-0 border-l border-border bg-surface">
          <div className="flex h-14 items-center justify-between border-b border-border px-4">
            <h2 className="font-semibold text-sm">Mesa {String(selected.numero).padStart(2, "0")}</h2>
            <Button size="icon" variant="ghost" onClick={() => setSelectedId(undefined)} aria-label="Fechar detalhes"><X className="size-4" /></Button>
          </div>
          <div className="p-5">
            <span className={`inline-flex rounded-md border px-2.5 py-1 text-xs font-semibold ${statusStyle[selected.status]}`}>{selected.status}</span>
            <dl className="mt-6 space-y-4 text-sm">
              <div className="flex justify-between"><dt className="text-muted-foreground">Capacidade</dt><dd className="font-medium">{selected.capacidade} pessoas</dd></div>
              {selected.descricao && <div className="flex justify-between"><dt className="text-muted-foreground">Descricao</dt><dd className="font-medium text-right max-w-36 truncate">{selected.descricao}</dd></div>}
              <div className="flex justify-between"><dt className="text-muted-foreground">Criada em</dt><dd className="font-medium">{new Date(selected.criadoEm).toLocaleDateString("pt-BR")}</dd></div>
              <div className="flex justify-between border-t border-border pt-4"><dt className="text-muted-foreground">Ativa</dt><dd className="font-medium">{selected.ativo ? "Sim" : "Nao"}</dd></div>
            </dl>
            <div className="mt-7 space-y-2">
              {selected.status === "Livre" && (
                <Button className="w-full gap-1.5" onClick={() => onAbrirCaixa?.(selected.id, selected.numero)}>
                  <ShoppingCart className="size-4" />Abrir Caixa na Mesa
                </Button>
              )}
              {selected.status === "Ocupada" && <Button className="w-full gap-1.5" onClick={() => handleStatusChange(selected, "Livre")}><PowerOff className="size-4" />Encerrar / Livre</Button>}
              {selected.status === "Reservada" && <Button className="w-full gap-1.5" onClick={() => handleStatusChange(selected, "Ocupada")}><Power className="size-4" />Confirmar chegada</Button>}
              {selected.status !== "Reservada" && <Button variant="outline" className="w-full shadow-none gap-1.5" onClick={() => handleStatusChange(selected, "Reservada")}><Clock3 className="size-4" />Reservar</Button>}
              {selected.ativo && (
                <Button variant="outline" className="w-full shadow-none gap-1.5 text-destructive hover:text-destructive hover:bg-destructive/10" onClick={() => handleInativar(selected)}>
                  <PowerOff className="size-4" />Inativar mesa
                </Button>
              )}
              <Button variant="outline" className="w-full shadow-none gap-1.5 text-destructive hover:text-destructive hover:bg-destructive/10" onClick={() => handleExcluirPermanente(selected)}>
                <Trash2 className="size-4" />Excluir permanentemente
              </Button>
            </div>
          </div>
        </aside>
      )}

      {formOpen && (
        <aside className="w-[380px] shrink-0 border-l border-border bg-surface max-[760px]:absolute max-[760px]:inset-0 max-[760px]:z-10 max-[760px]:w-full">
          <div className="flex h-14 items-center justify-between border-b border-border px-4">
            <h2 className="font-semibold text-sm">Nova mesa</h2>
            <Button size="icon" variant="ghost" onClick={closeForm} aria-label="Fechar formulario"><X className="size-4" /></Button>
          </div>
          <div className="space-y-4 p-5 overflow-auto max-h-[calc(100%-56px)]">
            <div>
              <label htmlFor="mesa-numero" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">Numero *</label>
              <Input
                id="mesa-numero"
                type="number"
                min="1"
                value={draft.numero}
                onChange={(e) => setDraft({ ...draft, numero: e.target.value })}
                placeholder="Ex: 1"
                className="shadow-none"
              />
              {errors.numero && <p className="mt-1 text-xs text-destructive">{errors.numero}</p>}
            </div>
            <div>
              <label htmlFor="mesa-capacidade" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">Capacidade</label>
              <Input
                id="mesa-capacidade"
                type="number"
                min="1"
                value={draft.capacidade}
                onChange={(e) => setDraft({ ...draft, capacidade: e.target.value })}
                placeholder="4"
                className="shadow-none"
              />
              {errors.capacidade && <p className="mt-1 text-xs text-destructive">{errors.capacidade}</p>}
            </div>
            <div>
              <label htmlFor="mesa-desc" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">Descricao <span className="font-normal text-muted-foreground">(opcional)</span></label>
              <Input
                id="mesa-desc"
                value={draft.descricao}
                onChange={(e) => setDraft({ ...draft, descricao: e.target.value })}
                placeholder="Ex: Area externa"
                className="shadow-none"
              />
            </div>
            <div className="flex justify-end gap-2 border-t border-border pt-4">
              <Button variant="outline" onClick={closeForm} disabled={saving} className="shadow-none">Cancelar</Button>
              <Button onClick={handleSave} disabled={saving}>{saving ? "Salvando..." : "Criar mesa"}</Button>
            </div>
          </div>
        </aside>
      )}
    </div>
  );
}
