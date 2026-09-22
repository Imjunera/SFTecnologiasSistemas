import { useState, useEffect, useCallback, useRef } from "react";
import { Edit3, Power, PowerOff, Search, Tag, Plus, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { HttpService } from "@/http.service";
import type { Categoria, CriarCategoriaRequest, AtualizarCategoriaRequest } from "@/contracts/categoria";

interface DraftCategoria {
  nome: string;
  descricao: string;
  ordem: string;
}

const emptyDraft: DraftCategoria = {
  nome: "",
  descricao: "",
  ordem: "",
};

export function CategoriesModule({ onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void }) {
  const [categorias, setCategorias] = useState<Categoria[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [apenasAtivos, setApenasAtivos] = useState(false);
  const [editingId, setEditingId] = useState<number>();
  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<DraftCategoria>(emptyDraft);
  const [errors, setErrors] = useState<Partial<DraftCategoria>>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(query);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [query]);

  const carregarCategorias = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const params = new URLSearchParams();
      if (debouncedQuery) params.set('busca', debouncedQuery);
      if (apenasAtivos) params.set('apenasAtivos', 'true');
      const res = await HttpService.get<Categoria[]>(`/api/categorias?${params.toString()}`);
      if (res.success && res.data) {
        setCategorias(res.data);
      } else {
        setApiError(res.error || "Nao foi possivel carregar as categorias.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    } finally {
      setLoading(false);
    }
  }, [debouncedQuery, apenasAtivos]);

  useEffect(() => {
    carregarCategorias();
  }, [carregarCategorias]);

  function openNew() {
    setDraft(emptyDraft);
    setEditingId(undefined);
    setErrors({});
    setApiError(null);
    setFormOpen(true);
    onDirtyChange?.(true);
  }

  function openEdit(cat: Categoria) {
    setDraft({
      nome: cat.nome,
      descricao: cat.descricao || "",
      ordem: cat.ordem?.toString() || "",
    });
    setEditingId(cat.id);
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
    const nextErrors: Partial<DraftCategoria> = {};
    if (!draft.nome.trim()) nextErrors.nome = "O nome é obrigatório.";

    let ordemNum: number | undefined;
    if (draft.ordem.trim()) {
      ordemNum = parseInt(draft.ordem);
      if (isNaN(ordemNum) || ordemNum < 0) {
        nextErrors.ordem = "A ordem deve ser um número positivo.";
      }
    }

    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;

    setSaving(true);
    setApiError(null);

    try {
      if (editingId) {
        const payload: AtualizarCategoriaRequest = {
          nome: draft.nome.trim(),
          descricao: draft.descricao.trim() || null,
          ordem: ordemNum,
          ativo: true,
        };
        const res = await HttpService.put<Categoria>(`/api/categorias/${editingId}`, payload);
        if (res.success) {
          closeForm();
          await carregarCategorias();
        } else {
          setApiError(res.error || "Erro ao atualizar categoria.");
        }
      } else {
        const payload: CriarCategoriaRequest = {
          nome: draft.nome.trim(),
          descricao: draft.descricao.trim() || null,
          ordem: ordemNum,
        };
        const res = await HttpService.post<Categoria>("/api/categorias", payload);
        if (res.success) {
          closeForm();
          await carregarCategorias();
        } else {
          setApiError(res.error || "Erro ao criar categoria.");
        }
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro inesperado.");
    } finally {
      setSaving(false);
    }
  }

  async function handleInativar(id: number) {
    if (!window.confirm("Deseja realmente inativar esta categoria?")) return;
    try {
      const res = await HttpService.delete(`/api/categorias/${id}`);
      if (res.success) {
        await carregarCategorias();
      } else {
        alert(res.error || "Erro ao inativar categoria.");
      }
    } catch (err: any) {
      alert("Falha na comunicacao: " + (err?.message || "Erro de rede"));
    }
  }

  async function handleExcluirPermanente(id: number, nome: string) {
    if (!window.confirm(`Deseja realmente EXCLUIR PERMANENTEMENTE a categoria "${nome}"? Esta ação não pode ser desfeita.`)) return;
    try {
      const res = await HttpService.delete(`/api/categorias/${id}/permanente`);
      if (res.success) {
        await carregarCategorias();
      } else {
        alert(res.error || "Erro ao excluir categoria permanentemente.");
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
            <label
              htmlFor="category-search-input"
              className="mb-2 block text-xs font-semibold uppercase text-muted-foreground"
            >
              Buscar categoria
            </label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="category-search-input"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Nome da categoria"
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
            <Plus className="size-4" />
            Nova categoria
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
              Carregando categorias...
            </div>
          ) : categorias.length ? (
            <div className="overflow-hidden rounded-md border border-border bg-surface">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border bg-muted/60 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Ordem</th>
                    <th className="px-4 py-3 font-semibold">Nome</th>
                    <th className="px-4 py-3 font-semibold">Descricao</th>
                    <th className="px-4 py-3 font-semibold">Status</th>
                    <th className="w-24 px-4 py-3 text-right font-semibold">Acoes</th>
                  </tr>
                </thead>
                <tbody>
                  {categorias.map((cat) => (
                    <tr
                      key={cat.id}
                      className="border-b border-border last:border-0 hover:bg-muted/30"
                    >
                      <td className="px-4 py-3 tabular-nums text-muted-foreground">
                        {cat.ordem ?? "—"}
                      </td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-foreground">{cat.nome}</p>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground truncate max-w-sm">
                        {cat.descricao || "—"}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
                            cat.ativo
                              ? "bg-success-soft text-success-foreground"
                              : "bg-muted text-muted-foreground"
                          }`}
                        >
                          {cat.ativo ? (
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
                            onClick={() => openEdit(cat)}
                            aria-label={`Editar ${cat.nome}`}
                            title="Editar categoria"
                          >
                            <Edit3 className="size-4" />
                          </Button>
                          {cat.ativo && (
                            <Button
                              size="icon"
                              variant="ghost"
                              onClick={() => handleInativar(cat.id)}
                              aria-label={`Inativar ${cat.nome}`}
                              title="Inativar categoria"
                              className="text-muted-foreground hover:text-destructive"
                            >
                              <PowerOff className="size-4" />
                            </Button>
                          )}
                          <Button
                            size="icon"
                            variant="ghost"
                            onClick={() => handleExcluirPermanente(cat.id, cat.nome)}
                            aria-label={`Excluir permanentemente ${cat.nome}`}
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
              <Tag className="mb-3 size-8 text-muted-foreground/60" />
              <p className="text-sm font-medium">Nenhuma categoria cadastrada</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Clique em "Nova categoria" para adicionar sua primeira categoria.
              </p>
            </div>
          )}
        </div>
      </section>

      {formOpen && (
        <aside className="w-[380px] shrink-0 border-l border-border bg-surface max-[760px]:absolute max-[760px]:inset-0 max-[760px]:z-10 max-[760px]:w-full">
          <div className="flex h-14 items-center justify-between border-b border-border px-4">
            <h2 className="font-semibold text-sm">
              {editingId ? "Editar categoria" : "Nova categoria"}
            </h2>
            <Button
              size="icon"
              variant="ghost"
              onClick={closeForm}
              aria-label="Fechar formulario"
            >
              <X className="size-4" />
            </Button>
          </div>

          <div className="space-y-4 p-5 overflow-auto max-h-[calc(100%-56px)]">
            <div>
              <label htmlFor="cat-nome" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Nome *
              </label>
              <Input
                id="cat-nome"
                value={draft.nome}
                onChange={(e) => setDraft({ ...draft, nome: e.target.value })}
                placeholder="Ex: Bebidas"
                className="shadow-none"
              />
              {errors.nome && <p className="mt-1 text-xs text-destructive">{errors.nome}</p>}
            </div>

            <div>
              <label htmlFor="cat-desc" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Descricao <span className="font-normal text-muted-foreground">(opcional)</span>
              </label>
              <Input
                id="cat-desc"
                value={draft.descricao}
                onChange={(e) => setDraft({ ...draft, descricao: e.target.value })}
                placeholder="Detalhes da categoria..."
                className="shadow-none"
              />
            </div>

            <div>
              <label htmlFor="cat-ordem" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Ordem <span className="font-normal text-muted-foreground">(opcional)</span>
              </label>
              <Input
                id="cat-ordem"
                type="number"
                min="0"
                value={draft.ordem}
                onChange={(e) => setDraft({ ...draft, ordem: e.target.value })}
                placeholder="0"
                className="shadow-none"
              />
              {errors.ordem && <p className="mt-1 text-xs text-destructive">{errors.ordem}</p>}
            </div>

            <div className="flex justify-end gap-2 border-t border-border pt-4">
              <Button variant="outline" onClick={closeForm} disabled={saving} className="shadow-none">
                Cancelar
              </Button>
              <Button onClick={handleSave} disabled={saving}>
                {saving ? "Salvando..." : "Salvar categoria"}
              </Button>
            </div>
          </div>
        </aside>
      )}
    </div>
  );
}
