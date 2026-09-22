import { useEffect, useMemo, useRef, useState } from "react";
import { CheckCircle2, Minus, PackageSearch, Plus, Search, ShoppingCart, Trash2, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { HttpService } from "@/http.service";
import type { Produto } from "@/contracts/produto";
import type { SessaoCaixa } from "@/contracts/caixa";
import { currency } from "@/data/mock-data";

type CartLine = Produto & { quantity: number };

interface CheckoutModuleProps {
  onDirtyChange?: (dirty: boolean) => void;
  mesaIdInicial?: number | null;
}

export function CheckoutModule({ onDirtyChange, mesaIdInicial }: CheckoutModuleProps) {
  const [produtos, setProdutos] = useState<Produto[]>([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [payment, setPayment] = useState("dinheiro");
  const [notice, setNotice] = useState<string>();
  const [sessaoCaixa, setSessaoCaixa] = useState<SessaoCaixa | null>(null);
  const [valorAbertura, setValorAbertura] = useState("0");
  const [abrirCaixaLoading, setAbrirCaixaLoading] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    onDirtyChange?.(cart.length > 0);
  }, [cart.length, onDirtyChange]);

  useEffect(() => {
    searchRef.current?.focus();
  }, []);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      setApiError(null);
      try {
        const res = await HttpService.get<Produto[]>("/api/produtos?apenasAtivos=true");
        if (cancelled) return;
        if (res.success && res.data) {
          setProdutos(res.data);
        } else {
          setApiError(res.error || "Nao foi possivel carregar os produtos.");
        }
      } catch (err: any) {
        if (!cancelled) {
          setApiError(err?.message || "Erro de conexao com o servidor.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;
    async function checkSessao() {
      try {
        const res = await HttpService.get<SessaoCaixa>("/api/caixa/sessao-atual");
        if (cancelled) return;
        if (res.success && res.data) {
          setSessaoCaixa(res.data);
        }
      } catch {
        // No session open
      }
    }
    checkSessao();
    return () => { cancelled = true; };
  }, []);

  async function handleAbrirCaixa() {
    setAbrirCaixaLoading(true);
    setApiError(null);
    try {
      const res = await HttpService.post<SessaoCaixa>("/api/caixa/abrir", {
        valorInicial: parseFloat(valorAbertura.replace(",", ".")) || 0,
        mesaId: mesaIdInicial || null,
      });
      if (res.success && res.data) {
        setSessaoCaixa(res.data);
        setNotice("Caixa aberto com sucesso");
      } else {
        const errorMessage = res.error || res.details || "Erro ao abrir caixa.";
        setApiError(errorMessage);
        console.error("Erro ao abrir caixa:", res);
      }
    } catch (err: any) {
      const errorMessage = err?.message || err?.details || "Erro ao abrir caixa.";
      setApiError(errorMessage);
      console.error("Erro ao abrir caixa:", err);
    } finally {
      setAbrirCaixaLoading(false);
    }
  }

  const filtered = useMemo(() =>
    produtos.filter((p) =>
      `${p.nome} ${p.codigo} Geral`.toLowerCase().includes(query.toLowerCase()),
    ), [produtos, query]);
  const total = useMemo(() => cart.reduce((sum, item) => sum + item.precoVenda * item.quantity, 0), [cart]);

  function addProduct(produto: Produto) {
    setCart((current) => {
      const existing = current.find((item) => item.id === produto.id);
      if (existing) return current.map((item) => item.id === produto.id ? { ...item, quantity: item.quantity + 1 } : item);
      return [...current, { ...produto, quantity: 1 }];
    });
    setNotice(`${produto.nome} adicionado`);
  }

  function changeQuantity(id: number, change: number) {
    setCart((current) => current
      .map((item) => item.id === id ? { ...item, quantity: Math.max(0, item.quantity + change) } : item)
      .filter((item) => item.quantity > 0));
  }

  function cancelSale() {
    if (cart.length === 0 || window.confirm("Cancelar a venda atual e remover todos os itens?")) {
      setCart([]);
      setNotice("Venda cancelada");
      searchRef.current?.focus();
    }
  }

  function finishSale() {
    if (!cart.length) return;
    setCart([]);
    setNotice(`Venda de ${currency.format(total)} concluida com sucesso`);
    searchRef.current?.focus();
  }

  if (!sessaoCaixa) {
    return (
      <div className="flex h-full items-center justify-center bg-background p-4">
        <div className="w-full max-w-sm rounded-md border border-border bg-surface p-6 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold">Abrir Caixa</h2>
          {mesaIdInicial && (
            <p className="mb-3 text-xs text-muted-foreground">Mesa vinculada: <span className="font-medium text-foreground">#{mesaIdInicial}</span></p>
          )}
          <p className="mb-4 text-xs text-muted-foreground">Abra uma sessao de caixa para iniciar as vendas.</p>
          <label className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">Valor inicial (R$)</label>
          <Input
            type="number"
            step="0.01"
            min="0"
            value={valorAbertura}
            onChange={(e) => setValorAbertura(e.target.value)}
            placeholder="0,00"
            className="mb-4 shadow-none"
          />
          {apiError && <p className="mb-3 text-xs text-destructive">{apiError}</p>}
          <Button onClick={handleAbrirCaixa} disabled={abrirCaixaLoading} className="w-full">
            {abrirCaixaLoading ? "Abrindo..." : "Abrir Caixa"}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="grid h-full min-h-0 grid-cols-[minmax(0,1fr)_minmax(360px,42%)] bg-background max-[900px]:grid-cols-1 max-[900px]:overflow-auto">
      <section className="flex min-h-0 flex-col border-r border-border max-[900px]:min-h-[520px] max-[900px]:border-r-0 max-[900px]:border-b">
        <div className="border-b border-border bg-surface p-4">
          <label htmlFor="product-search" className="mb-2 block text-xs font-semibold uppercase text-muted-foreground">Buscar produto</label>
          <div className="relative">
            <Search className="absolute left-3 top-1/2 size-5 -translate-y-1/2 text-muted-foreground" />
            <Input ref={searchRef} id="product-search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Digite o nome ou o codigo" className="h-11 bg-background pl-11 text-base shadow-none" />
          </div>
        </div>
        <div className="min-h-0 flex-1 overflow-auto p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold">Produtos</h2>
            <span className="text-xs text-muted-foreground">{filtered.length} encontrados</span>
          </div>
          {loading ? (
            <div className="flex h-48 items-center justify-center text-xs text-muted-foreground">
              Carregando produtos...
            </div>
          ) : apiError ? (
            <div className="flex h-48 flex-col items-center justify-center text-center text-muted-foreground">
              <PackageSearch className="mb-3 size-8" />
              <p className="text-sm font-medium text-destructive">{apiError}</p>
              <p className="mt-1 text-xs">Verifique a conexao e tente novamente.</p>
            </div>
          ) : filtered.length ? (
            <div className="grid grid-cols-2 gap-2 xl:grid-cols-3">
              {filtered.map((produto) => (
                <Button key={produto.id} variant="outline" onClick={() => addProduct(produto)} className="h-auto min-h-24 items-start justify-between whitespace-normal rounded-md p-3 text-left shadow-none">
                  <span className="flex h-full min-w-0 flex-col items-start">
                    <span className="line-clamp-2 text-sm font-semibold leading-snug">{produto.nome}</span>
                    <span className="mt-1 text-xs font-normal text-muted-foreground">Geral · — un.</span>
                    <span className="mt-auto pt-3 text-base font-bold text-primary">{currency.format(produto.precoVenda)}</span>
                  </span>
                  <Plus className="mt-0.5 size-4 shrink-0 text-primary" />
                </Button>
              ))}
            </div>
          ) : (
            <div className="flex h-48 flex-col items-center justify-center text-center text-muted-foreground">
              <PackageSearch className="mb-3 size-8" />
              <p className="text-sm font-medium text-foreground">Nenhum produto encontrado</p>
              <p className="mt-1 text-xs">Tente buscar por outro nome ou codigo.</p>
            </div>
          )}
        </div>
      </section>

      <section className="flex min-h-0 flex-col bg-surface">
        <div className="flex h-14 shrink-0 items-center justify-between border-b border-border px-4">
          <div className="flex items-center gap-2"><ShoppingCart className="size-4 text-primary" /><h2 className="text-sm font-semibold">Itens da venda</h2></div>
          <span className="text-xs text-muted-foreground">{cart.reduce((sum, item) => sum + item.quantity, 0)} itens</span>
        </div>
        <div className="min-h-0 flex-1 overflow-auto">
          {cart.length ? cart.map((item) => (
            <div key={item.id} className="grid grid-cols-[1fr_auto] gap-3 border-b border-border p-4">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium">{item.nome}</p>
                <p className="mt-1 text-xs text-muted-foreground">{currency.format(item.precoVenda)} por unidade</p>
              </div>
              <p className="text-right text-sm font-semibold">{currency.format(item.precoVenda * item.quantity)}</p>
              <div className="flex items-center gap-1">
                <Button size="icon" variant="outline" className="size-8 shadow-none" onClick={() => changeQuantity(item.id, -1)} aria-label={`Diminuir ${item.nome}`}><Minus className="size-3.5" /></Button>
                <span className="w-9 text-center text-sm font-semibold tabular-nums">{item.quantity}</span>
                <Button size="icon" variant="outline" className="size-8 shadow-none" onClick={() => changeQuantity(item.id, 1)} aria-label={`Aumentar ${item.nome}`}><Plus className="size-3.5" /></Button>
              </div>
              <Button size="icon" variant="ghost" className="ml-auto size-8 text-muted-foreground hover:text-destructive" onClick={() => setCart((current) => current.filter((line) => line.id !== item.id))} aria-label={`Remover ${item.nome}`}><Trash2 className="size-3.5" /></Button>
            </div>
          )) : (
            <div className="flex h-full min-h-48 flex-col items-center justify-center px-5 text-center">
              <ShoppingCart className="mb-3 size-8 text-muted-foreground/60" />
              <p className="text-sm font-medium">Venda sem itens</p>
              <p className="mt-1 max-w-60 text-xs text-muted-foreground">Selecione um produto ao lado para iniciar a venda.</p>
            </div>
          )}
        </div>
        <div className="shrink-0 border-t border-border bg-background p-4">
          {notice && <div className="mb-3 flex items-center gap-2 rounded-md bg-success-soft px-3 py-2 text-xs font-medium text-success-foreground"><CheckCircle2 className="size-4" />{notice}</div>}
          <div className="mb-4 flex items-end justify-between">
            <div><p className="text-xs font-medium uppercase text-muted-foreground">Total da venda</p><p className="mt-1 text-3xl font-bold tracking-normal text-foreground">{currency.format(total)}</p></div>
            <div className="w-40"><Select value={payment} onValueChange={setPayment}><SelectTrigger className="shadow-none"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="dinheiro">Dinheiro</SelectItem><SelectItem value="cartao">Cartao</SelectItem><SelectItem value="pix">PIX</SelectItem></SelectContent></Select></div>
          </div>
          <div className="grid grid-cols-[auto_1fr] gap-2">
            <Button variant="outline" onClick={cancelSale} disabled={!cart.length} className="h-11 shadow-none"><XCircle className="size-4" />Cancelar</Button>
            <Button onClick={finishSale} disabled={!cart.length} className="h-11"><CheckCircle2 className="size-4" />Concluir venda</Button>
          </div>
        </div>
      </section>
    </div>
  );
}
