import { useState, useEffect, useCallback, useRef } from "react";
import { Edit3, Power, PowerOff, Search, UserPlus, Users, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { HttpService } from "@/http.service";
import type { Cliente, CriarClienteRequest, AtualizarClienteRequest } from "@/contracts/cliente";

interface DraftCliente {
  nome: string;
  cpf: string;
  telefone: string;
  email: string;
  endereco: string;
  observacoes: string;
}

const emptyDraft: DraftCliente = {
  nome: "",
  cpf: "",
  telefone: "",
  email: "",
  endereco: "",
  observacoes: "",
};

function applyCpfMask(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 11);
  if (digits.length <= 3) return digits;
  if (digits.length <= 6) return `${digits.slice(0, 3)}.${digits.slice(3)}`;
  if (digits.length <= 9) return `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6)}`;
  return `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6, 9)}-${digits.slice(9)}`;
}

function applyCnpjMask(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 14);
  if (digits.length <= 2) return digits;
  if (digits.length <= 5) return `${digits.slice(0, 2)}.${digits.slice(2)}`;
  if (digits.length <= 8) return `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5)}`;
  if (digits.length <= 12) return `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8)}`;
  return `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8, 12)}-${digits.slice(12)}`;
}

function applyPhoneMask(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 11);
  if (digits.length <= 2) return digits.length ? `(${digits}` : "";
  if (digits.length <= 6) return `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
  if (digits.length <= 10) return `(${digits.slice(0, 2)}) ${digits.slice(2, 6)}-${digits.slice(6)}`;
  return `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
}

function maskCpfCnpj(value: string): string {
  const digits = value.replace(/\D/g, "");
  if (digits.length <= 11) return applyCpfMask(value);
  return applyCnpjMask(value);
}

export function CustomersModule({ onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void }) {
  const [clientes, setClientes] = useState<Cliente[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [apenasAtivos, setApenasAtivos] = useState(false);
  const [editingId, setEditingId] = useState<number>();
  const [editingAtivo, setEditingAtivo] = useState<boolean | undefined>(undefined);
  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<DraftCliente>(emptyDraft);
  const [errors, setErrors] = useState<Partial<DraftCliente>>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(query);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [query]);

  const carregarClientes = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const params = new URLSearchParams();
      if (debouncedQuery) params.set("busca", debouncedQuery);
      if (apenasAtivos) params.set("apenasAtivos", "true");
      const res = await HttpService.get<Cliente[]>(`/api/clientes?${params.toString()}`);
      if (res.success && res.data) {
        setClientes(res.data);
      } else {
        setApiError(res.error || "Nao foi possivel carregar os clientes.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    } finally {
      setLoading(false);
    }
  }, [debouncedQuery, apenasAtivos]);

  useEffect(() => {
    carregarClientes();
  }, [carregarClientes]);

  function openNew() {
    setDraft(emptyDraft);
    setEditingId(undefined);
    setEditingAtivo(undefined);
    setErrors({});
    setApiError(null);
    setFormOpen(true);
    onDirtyChange?.(true);
  }

  function openEdit(cliente: Cliente) {
    setDraft({
      nome: cliente.nome,
      cpf: cliente.cpf ? maskCpfCnpj(cliente.cpf) : "",
      telefone: cliente.telefone ? applyPhoneMask(cliente.telefone) : "",
      email: cliente.email || "",
      endereco: cliente.endereco || "",
      observacoes: cliente.observacoes || "",
    });
    setEditingId(cliente.id);
    setEditingAtivo(cliente.ativo);
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
    const nextErrors: Partial<DraftCliente> = {};
    if (!draft.nome.trim()) nextErrors.nome = "O nome e obrigatorio.";
    if (draft.email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(draft.email.trim())) {
      nextErrors.email = "Email invalido.";
    }
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;

    setSaving(true);
    setApiError(null);

    try {
      // Remove formatting from CPF/CNPJ and phone before sending to API
      const cpfClean = draft.cpf.replace(/\D/g, "") || null;
      const telefoneClean = draft.telefone.replace(/\D/g, "") || null;

      if (editingId) {
        const payload: AtualizarClienteRequest = {
          nome: draft.nome.trim(),
          cpf: cpfClean,
          telefone: telefoneClean,
          email: draft.email.trim() || null,
          endereco: draft.endereco.trim() || null,
          observacoes: draft.observacoes.trim() || null,
          // Mantém o status atual: editar um cliente inativo não pode reativá-lo.
          ativo: editingAtivo ?? true,
        };
        const res = await HttpService.put<Cliente>(`/api/clientes/${editingId}`, payload);
        if (res.success) {
          closeForm();
          await carregarClientes();
        } else {
          setApiError(res.error || "Erro ao atualizar cliente.");
        }
      } else {
        const payload: CriarClienteRequest = {
          nome: draft.nome.trim(),
          cpf: cpfClean,
          telefone: telefoneClean,
          email: draft.email.trim() || null,
          endereco: draft.endereco.trim() || null,
          observacoes: draft.observacoes.trim() || null,
        };
        const res = await HttpService.post<Cliente>("/api/clientes", payload);
        if (res.success) {
          closeForm();
          await carregarClientes();
        } else {
          setApiError(res.error || "Erro ao criar cliente.");
        }
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro inesperado.");
    } finally {
      setSaving(false);
    }
  }

  async function handleInativar(id: number) {
    if (!window.confirm("Deseja realmente inativar este cliente?")) return;
    try {
      const res = await HttpService.delete(`/api/clientes/${id}`);
      if (res.success) {
        await carregarClientes();
      } else {
        alert(res.error || "Erro ao inativar cliente.");
      }
    } catch (err: any) {
      alert("Falha na comunicacao: " + (err?.message || "Erro de rede"));
    }
  }

  async function handleExcluirPermanente(id: number, nome: string) {
    if (!window.confirm(`Deseja realmente EXCLUIR PERMANENTEMENTE o cliente "${nome}"? Esta ação não pode ser desfeita.`)) return;
    try {
      const res = await HttpService.delete(`/api/clientes/${id}/permanente`);
      if (res.success) {
        await carregarClientes();
      } else {
        alert(res.error || "Erro ao excluir cliente permanentemente.");
      }
    } catch (err: any) {
      alert("Falha na comunicacao: " + (err?.message || "Erro de rede"));
    }
  }

  return (
    <div className="flex h-full min-h-0 bg-background">
      <section className="flex min-w-0 flex-1 flex-col">
        <div className="flex flex-wrap items-end gap-3 border-b border-border bg-surface p-4">
          <div className="max-w-md flex-1">
            <label htmlFor="client-search-input" className="mb-2 block text-xs font-semibold uppercase text-muted-foreground">
              Buscar cliente
            </label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="client-search-input"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Nome, CPF/CNPJ ou telefone"
                className="bg-background pl-9 shadow-none"
              />
            </div>
          </div>

          <label className="flex items-center gap-2 text-xs font-medium text-muted-foreground cursor-pointer pb-2">
            <input
              type="checkbox"
              checked={apenasAtivos}
              onChange={(e) => setApenasAtivos(e.target.checked)}
              className="rounded border-border"
            />
            Apenas ativos
          </label>

          <Button onClick={openNew} className="h-9 gap-1.5">
            <UserPlus className="size-4" />
            Novo cliente
          </Button>
        </div>

        {apiError && (
          <div className="mx-4 mt-3 rounded border border-destructive/40 bg-destructive/10 p-2.5 text-xs text-destructive">
            {apiError}
          </div>
        )}

        <div className="min-h-0 flex-1 overflow-auto p-4">
          {loading ? (
            <div className="flex h-48 items-center justify-center text-xs text-muted-foreground">
              Carregando lista de clientes...
            </div>
          ) : clientes.length ? (
            <div className="overflow-hidden rounded-md border border-border bg-surface">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border bg-muted/60 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Nome</th>
                    <th className="px-4 py-3 font-semibold">CPF/CNPJ</th>
                    <th className="px-4 py-3 font-semibold">Telefone</th>
                    <th className="px-4 py-3 font-semibold">Email</th>
                    <th className="px-4 py-3 font-semibold">Status</th>
                    <th className="w-24 px-4 py-3 text-right font-semibold">Acoes</th>
                  </tr>
                </thead>
                <tbody>
                  {clientes.map((cliente) => (
                    <tr key={cliente.id} className="border-b border-border last:border-0 hover:bg-muted/30">
                      <td className="px-4 py-3">
                        <p className="font-medium text-foreground">{cliente.nome}</p>
                        {cliente.endereco && (
                          <p className="text-xs text-muted-foreground truncate max-w-sm">{cliente.endereco}</p>
                        )}
                      </td>
                      <td className="px-4 py-3 tabular-nums">{cliente.cpf ? maskCpfCnpj(cliente.cpf) : "—"}</td>
                      <td className="px-4 py-3 tabular-nums">{cliente.telefone ? applyPhoneMask(cliente.telefone) : "—"}</td>
                      <td className="px-4 py-3 text-muted-foreground">{cliente.email || "—"}</td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
                            cliente.ativo
                              ? "bg-success-soft text-success-foreground"
                              : "bg-muted text-muted-foreground"
                          }`}
                        >
                          {cliente.ativo ? (
                            <>
                              <Power className="size-3" /> Ativo
                            </>
                          ) : (
                            <>
                              <PowerOff className="size-3" /> Inativo
                            </>
                          )}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <Button
                            size="icon"
                            variant="ghost"
                            onClick={() => openEdit(cliente)}
                            aria-label={`Editar ${cliente.nome}`}
                            title="Editar cliente"
                          >
                            <Edit3 className="size-4" />
                          </Button>
                          {cliente.ativo && (
                            <Button
                              size="icon"
                              variant="ghost"
                              onClick={() => handleInativar(cliente.id)}
                              aria-label={`Inativar ${cliente.nome}`}
                              title="Inativar cliente"
                              className="text-muted-foreground hover:text-destructive"
                            >
                              <PowerOff className="size-4" />
                            </Button>
                          )}
                          <Button
                            size="icon"
                            variant="ghost"
                            onClick={() => handleExcluirPermanente(cliente.id, cliente.nome)}
                            aria-label={`Excluir permanentemente ${cliente.nome}`}
                            title="Excluir permanentemente"
                            className="text-muted-foreground hover:text-destructive"
                          >
                            <Trash2 className="size-4" />
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="flex h-64 flex-col items-center justify-center text-center">
              <Users className="mb-3 size-8 text-muted-foreground/60" />
              <p className="text-sm font-medium">Nenhum cliente encontrado</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Clique em "Novo cliente" para adicionar o primeiro registro.
              </p>
            </div>
          )}
        </div>
      </section>

      {formOpen && (
        <aside className="w-[380px] shrink-0 border-l border-border bg-surface max-[760px]:absolute max-[760px]:inset-0 max-[760px]:z-10 max-[760px]:w-full">
          <div className="flex h-14 items-center justify-between border-b border-border px-4">
            <h2 className="font-semibold text-sm">
              {editingId ? "Editar cliente" : "Novo cliente"}
            </h2>
            <Button size="icon" variant="ghost" onClick={closeForm} aria-label="Fechar formulario">
              <X className="size-4" />
            </Button>
          </div>

          <div className="space-y-4 p-5 overflow-auto max-h-[calc(100%-56px)]">
            <div>
              <label htmlFor="cli-nome" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Nome completo *
              </label>
              <Input
                id="cli-nome"
                value={draft.nome}
                onChange={(e) => setDraft({ ...draft, nome: e.target.value })}
                placeholder="Nome do cliente"
                className="shadow-none"
              />
              {errors.nome && <p className="mt-1 text-xs text-destructive">{errors.nome}</p>}
            </div>

            <div>
              <label htmlFor="cli-cpf" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                CPF/CNPJ
              </label>
              <Input
                id="cli-cpf"
                type="text"
                inputMode="numeric"
                value={draft.cpf}
                onChange={(e) => setDraft({ ...draft, cpf: maskCpfCnpj(e.target.value) })}
                placeholder="000.000.000-00 ou 00.000.000/0000-00"
                maxLength={18}
                className="shadow-none"
              />
            </div>

            <div>
              <label htmlFor="cli-telefone" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Telefone
              </label>
              <Input
                id="cli-telefone"
                value={draft.telefone}
                onChange={(e) => setDraft({ ...draft, telefone: applyPhoneMask(e.target.value) })}
                placeholder="(00) 00000-0000"
                maxLength={15}
                className="shadow-none"
              />
            </div>

            <div>
              <label htmlFor="cli-email" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Email
              </label>
              <Input
                id="cli-email"
                type="email"
                inputMode="email"
                autoComplete="email"
                name="email"
                value={draft.email}
                onChange={(e) => setDraft({ ...draft, email: e.target.value })}
                placeholder="cliente@email.com"
                className="shadow-none"
              />
              {errors.email && <p className="mt-1 text-xs text-destructive">{errors.email}</p>}
            </div>

            <div>
              <label htmlFor="cli-endereco" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Endereco
              </label>
              <Input
                id="cli-endereco"
                value={draft.endereco}
                onChange={(e) => setDraft({ ...draft, endereco: e.target.value })}
                placeholder="Rua, numero, bairro, cidade"
                className="shadow-none"
              />
            </div>

            <div>
              <label htmlFor="cli-obs" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Observacoes
              </label>
              <Input
                id="cli-obs"
                value={draft.observacoes}
                onChange={(e) => setDraft({ ...draft, observacoes: e.target.value })}
                placeholder="Notas internas..."
                className="shadow-none"
              />
            </div>

            <div className="flex justify-end gap-2 border-t border-border pt-4">
              <Button variant="outline" onClick={closeForm} disabled={saving} className="shadow-none">
                Cancelar
              </Button>
              <Button onClick={handleSave} disabled={saving}>
                {saving ? "Salvando..." : "Salvar cliente"}
              </Button>
            </div>
          </div>
        </aside>
      )}
    </div>
  );
}
