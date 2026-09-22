import { useState, useEffect, useCallback, useRef } from "react";
import { Edit3, Package, PackagePlus, Power, PowerOff, Search, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { HttpService } from "@/http.service";
import type { Produto, CriarProdutoRequest, AtualizarProdutoRequest } from "@/contracts/produto";
import type { Categoria } from "@/contracts/categoria";
import { currency } from "@/data/mock-data";

interface DraftProduct {
  nome: string;
  descricao: string;
  codigo: string;
  precoVenda: string;
  precoCusto: string;
  categoriaId: string;
}

const emptyDraft: DraftProduct = {
  nome: "",
  descricao: "",
  codigo: "",
  precoVenda: "",
  precoCusto: "",
  categoriaId: "",
};

export function ProductsModule({ onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void }) {
  const [produtos, setProdutos] = useState<Produto[]>([]);
  const [categorias, setCategorias] = useState<Categoria[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [apenasAtivos, setApenasAtivos] = useState(false);
  const [editingId, setEditingId] = useState<number>();
  const [formOpen, setFormOpen] = useState(false);
  const [draft, setDraft] = useState<DraftProduct>(emptyDraft);
  const [errors, setErrors] = useState<Partial<DraftProduct>>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(query);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [query]);

  const carregarProdutos = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const params = new URLSearchParams();
      if (debouncedQuery) params.set('busca', debouncedQuery);
      if (apenasAtivos) params.set('apenasAtivos', 'true');
      const res = await HttpService.get<Produto[]>(`/api/produtos?${params.toString()}`);
      if (res.success && res.data) {
        setProdutos(res.data);
      } else {
        setApiError(res.error || "Nao foi possivel carregar os produtos.");
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro de conexao com o servidor.");
    } finally {
      setLoading(false);
    }
  }, [debouncedQuery, apenasAtivos]);

  const carregarCategorias = useCallback(async () => {
    try {
      const res = await HttpService.get<Categoria[]>("/api/categorias?apenasAtivos=true");
      if (res.success && res.data) {
        setCategorias(res.data);
      }
    } catch {
      // Silently fail - categories are optional
    }
  }, []);

  useEffect(() => {
    carregarProdutos();
  }, [carregarProdutos]);

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

  function openEdit(prod: Produto) {
    setDraft({
      nome: prod.nome,
      descricao: prod.descricao || "",
      codigo: prod.codigo,
      precoVenda: prod.precoVenda.toString(),
      precoCusto: prod.precoCusto?.toString() || "",
      categoriaId: prod.categoriaId?.toString() || "",
    });
    setEditingId(prod.id);
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
    const nextErrors: Partial<DraftProduct> = {};
    if (!draft.nome.trim()) nextErrors.nome = "O nome é obrigatório.";
    if (!draft.codigo.trim()) nextErrors.codigo = "O código é obrigatório.";

    const precoVendaNum = parseFloat(draft.precoVenda.replace(",", "."));
    if (isNaN(precoVendaNum) || precoVendaNum <= 0) {
      nextErrors.precoVenda = "O preço de venda deve ser maior que zero.";
    }

    let precoCustoNum: number | undefined;
    if (draft.precoCusto.trim()) {
      precoCustoNum = parseFloat(draft.precoCusto.replace(",", "."));
      if (isNaN(precoCustoNum) || precoCustoNum < 0) {
        nextErrors.precoCusto = "O preço de custo deve ser positivo.";
      }
    }

    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;

    setSaving(true);
    setApiError(null);

    try {
      const categoriaIdNum = draft.categoriaId ? parseInt(draft.categoriaId) : undefined;
      
      if (editingId) {
        const payload: AtualizarProdutoRequest = {
          nome: draft.nome.trim(),
          descricao: draft.descricao.trim() || null,
          codigo: draft.codigo.trim(),
          precoVenda: precoVendaNum,
          precoCusto: precoCustoNum,
          ativo: true,
        };
        const res = await HttpService.put<Produto>(`/api/produtos/${editingId}`, { ...payload, categoriaId: categoriaIdNum });
        if (res.success) {
          closeForm();
          await carregarProdutos();
        } else {
          setApiError(res.error || "Erro ao atualizar produto.");
        }
      } else {
        const payload: CriarProdutoRequest = {
          nome: draft.nome.trim(),
          descricao: draft.descricao.trim() || null,
          codigo: draft.codigo.trim(),
          precoVenda: precoVendaNum,
          precoCusto: precoCustoNum,
        };
        const res = await HttpService.post<Produto>("/api/produtos", { ...payload, categoriaId: categoriaIdNum });
        if (res.success) {
          closeForm();
          await carregarProdutos();
        } else {
          setApiError(res.error || "Erro ao criar produto.");
        }
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro inesperado.");
    } finally {
      setSaving(false);
    }
  }

  async function handleInativar(id: number) {
    if (!window.confirm("Deseja realmente inativar este produto?")) return;
    try {
      const res = await HttpService.delete(`/api/produtos/${id}`);
      if (res.success) {
        await carregarProdutos();
      } else {
        alert(res.error || "Erro ao inativar produto.");
      }
    } catch (err: any) {
      alert("Falha na comunicação: " + (err?.message || "Erro de rede"));
    }
  }

  async function handleExcluirPermanente(id: number, nome: string) {
    if (!window.confirm(`Deseja realmente EXCLUIR PERMANENTEMENTE o produto "${nome}"? Esta ação não pode ser desfeita.`)) return;
    try {
      const res = await HttpService.delete(`/api/produtos/${id}/permanente`);
      if (res.success) {
        await carregarProdutos();
      } else {
        alert(res.error || "Erro ao excluir produto permanentemente.");
      }
    } catch (err: any) {
      alert("Falha na comunicação: " + (err?.message || "Erro de rede"));
    }
  }

  return (
    <div className="flex h-full min-h-0 bg-background">
      <section className="flex min-w-0 flex-1 flex-col">
        {/* Header toolbar */}
        <div className="flex flex-wrap items-end gap-3 border-b border-border bg-surface p-4">
          <div className="max-w-md flex-1">
            <label
              htmlFor="product-search-input"
              className="mb-2 block text-xs font-semibold uppercase text-muted-foreground"
            >
              Buscar produto
            </label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="product-search-input"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Nome ou código de barras"
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
            <PackagePlus className="size-4" />
            Novo produto
          </Button>
        </div>

        {apiError && (
          <div className="mx-4 mt-3 rounded border border-destructive/40 bg-destructive/10 p-2.5 text-xs text-destructive">
            {apiError}
          </div>
        )}

        {/* Content list / table */}
        <div className="min-h-0 flex-1 overflow-auto p-4">
          {loading ? (
            <div className="flex h-48 items-center justify-center text-xs text-muted-foreground">
              Carregando catálogo de produtos...
            </div>
          ) : produtos.length ? (
            <div className="overflow-hidden rounded-md border border-border bg-surface">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border bg-muted/60 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Codigo</th>
                    <th className="px-4 py-3 font-semibold">Produto</th>
                    <th className="px-4 py-3 font-semibold">Preco Venda</th>
                    <th className="px-4 py-3 font-semibold">Preco Custo</th>
                    <th className="px-4 py-3 font-semibold">Status</th>
                    <th className="w-24 px-4 py-3 text-right font-semibold">Acoes</th>
                  </tr>
                </thead>
                <tbody>
                  {produtos.map((prod) => (
                    <tr
                      key={prod.id}
                      className="border-b border-border last:border-0 hover:bg-muted/30"
                    >
                      <td className="px-4 py-3 font-mono text-xs text-muted-foreground">
                        {prod.codigo}
                      </td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-foreground">{prod.nome}</p>
                        {prod.descricao && (
                          <p className="text-xs text-muted-foreground truncate max-w-sm">
                            {prod.descricao}
                          </p>
                        )}
                      </td>
                      <td className="px-4 py-3 font-bold text-primary tabular-nums">
                        {currency.format(prod.precoVenda)}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground tabular-nums">
                        {prod.precoCusto != null ? currency.format(prod.precoCusto) : "—"}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
                            prod.ativo
                              ? "bg-success-soft text-success-foreground"
                              : "bg-muted text-muted-foreground"
                          }`}
                        >
                          {prod.ativo ? (
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
                            onClick={() => openEdit(prod)}
                            aria-label={`Editar ${prod.nome}`}
                            title="Editar produto"
                          >
                            <Edit3 className="size-4" />
                          </Button>
                          {prod.ativo && (
                            <Button
                              size="icon"
                              variant="ghost"
                              onClick={() => handleInativar(prod.id)}
                              aria-label={`Inativar ${prod.nome}`}
                              title="Inativar produto"
                              className="text-muted-foreground hover:text-destructive"
                            >
                              <PowerOff className="size-4" />
                            </Button>
                          )}
                          <Button
                            size="icon"
                            variant="ghost"
                            onClick={() => handleExcluirPermanente(prod.id, prod.nome)}
                            aria-label={`Excluir permanentemente ${prod.nome}`}
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
              <Package className="mb-3 size-8 text-muted-foreground/60" />
              <p className="text-sm font-medium">Nenhum produto cadastrado</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Clique em "Novo produto" para adicionar seu primeiro item.
              </p>
            </div>
          )}
        </div>
      </section>

      {/* Side Form Drawer */}
      {formOpen && (
        <aside className="w-[380px] shrink-0 border-l border-border bg-surface max-[760px]:absolute max-[760px]:inset-0 max-[760px]:z-10 max-[760px]:w-full">
          <div className="flex h-14 items-center justify-between border-b border-border px-4">
            <h2 className="font-semibold text-sm">
              {editingId ? "Editar produto" : "Novo produto"}
            </h2>
            <Button
              size="icon"
              variant="ghost"
              onClick={closeForm}
              aria-label="Fechar formulário"
            >
              <X className="size-4" />
            </Button>
          </div>

          <div className="space-y-4 p-5 overflow-auto max-h-[calc(100%-56px)]">
            <div>
              <label htmlFor="prod-codigo" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Código interno / Barras *
              </label>
              <Input
                id="prod-codigo"
                value={draft.codigo}
                onChange={(e) => setDraft({ ...draft, codigo: e.target.value })}
                placeholder="Ex: 789123456"
                className="shadow-none"
              />
              {errors.codigo && <p className="mt-1 text-xs text-destructive">{errors.codigo}</p>}
            </div>

            <div>
              <label htmlFor="prod-nome" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Nome do produto *
              </label>
              <Input
                id="prod-nome"
                value={draft.nome}
                onChange={(e) => setDraft({ ...draft, nome: e.target.value })}
                placeholder="Ex: Coca-Cola Lata 350ml"
                className="shadow-none"
              />
              {errors.nome && <p className="mt-1 text-xs text-destructive">{errors.nome}</p>}
            </div>

            <div>
              <label htmlFor="prod-categoria" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Categoria <span className="font-normal text-muted-foreground">(opcional)</span>
              </label>
              <Select value={draft.categoriaId} onValueChange={(value) => setDraft({ ...draft, categoriaId: value })}>
                <SelectTrigger className="shadow-none">
                  <SelectValue placeholder="Selecione uma categoria" />
                </SelectTrigger>
                <SelectContent>
                  {categorias.map((cat) => (
                    <SelectItem key={cat.id} value={cat.id.toString()}>
                      {cat.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="prod-preco-venda" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                  Preço Venda (R$) *
                </label>
                <Input
                  id="prod-preco-venda"
                  type="number"
                  step="0.01"
                  min="0.01"
                  value={draft.precoVenda}
                  onChange={(e) => setDraft({ ...draft, precoVenda: e.target.value })}
                  placeholder="0,00"
                  className="shadow-none font-semibold text-primary"
                />
                {errors.precoVenda && (
                  <p className="mt-1 text-xs text-destructive">{errors.precoVenda}</p>
                )}
              </div>

              <div>
                <label htmlFor="prod-preco-custo" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                  Preço Custo (R$)
                </label>
                <Input
                  id="prod-preco-custo"
                  type="number"
                  step="0.01"
                  min="0"
                  value={draft.precoCusto}
                  onChange={(e) => setDraft({ ...draft, precoCusto: e.target.value })}
                  placeholder="0,00"
                  className="shadow-none"
                />
                {errors.precoCusto && (
                  <p className="mt-1 text-xs text-destructive">{errors.precoCusto}</p>
                )}
              </div>
            </div>

            <div>
              <label htmlFor="prod-desc" className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
                Descrição <span className="font-normal text-muted-foreground">(opcional)</span>
              </label>
              <Input
                id="prod-desc"
                value={draft.descricao}
                onChange={(e) => setDraft({ ...draft, descricao: e.target.value })}
                placeholder="Detalhes do item..."
                className="shadow-none"
              />
            </div>

            <div className="flex justify-end gap-2 border-t border-border pt-4">
              <Button variant="outline" onClick={closeForm} disabled={saving} className="shadow-none">
                Cancelar
              </Button>
              <Button onClick={handleSave} disabled={saving}>
                {saving ? "Salvando..." : "Salvar produto"}
              </Button>
            </div>
          </div>
        </aside>
      )}
    </div>
  );
}

