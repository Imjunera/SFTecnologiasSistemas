import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  CheckCircle2,
  CircleDollarSign,
  Lock,
  Minus,
  PackageSearch,
  Plus,
  Search,
  ShoppingCart,
  Trash2,
  XCircle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { HttpService } from "@/http.service";
import type { Produto } from "@/contracts/produto";
import type { SessaoCaixa } from "@/contracts/caixa";
import type { CriarVendaRequest, ResumoCaixa, Venda } from "@/contracts/venda";
import { currency } from "@/data/mock-data";

type CartLine = Produto & { quantity: number };

interface CheckoutModuleProps {
  onDirtyChange?: (dirty: boolean) => void;
  mesaIdInicial?: number | null;
  mesaNumeroInicial?: number | null;
  /** Aba do caixa está visível (para recarregar produtos quando o operador volta para cá). */
  active?: boolean;
}

export function CheckoutModule({
  onDirtyChange,
  mesaIdInicial,
  mesaNumeroInicial,
  active = true,
}: CheckoutModuleProps) {
  const [produtos, setProdutos] = useState<Produto[]>([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [payment, setPayment] = useState("dinheiro");
  const [notice, setNotice] = useState<string>();
  const [vendaErro, setVendaErro] = useState<string | null>(null);
  const [concluindo, setConcluindo] = useState(false);
  const [sessaoCaixa, setSessaoCaixa] = useState<SessaoCaixa | null>(null);
  const [valorAbertura, setValorAbertura] = useState("0");
  const [abrirCaixaLoading, setAbrirCaixaLoading] = useState(false);
  const [fechandoCaixa, setFechandoCaixa] = useState(false);
  const [resumo, setResumo] = useState<ResumoCaixa | null>(null);
  const [valorFechamento, setValorFechamento] = useState("");
  const [fechamentoErro, setFechamentoErro] = useState<string | null>(null);
  const [fecharLoading, setFecharLoading] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    onDirtyChange?.(cart.length > 0);
  }, [cart.length, onDirtyChange]);

  useEffect(() => {
    searchRef.current?.focus();
  }, []);

  const carregarSessao = useCallback(async () => {
    try {
      const res = await HttpService.get<SessaoCaixa>("/api/caixa/sessao-atual");
      setSessaoCaixa(res.success && res.data ? res.data : null);
    } catch {
      // sem sessão aberta
    }
  }, []);

  const carregarProdutos = useCallback(async () => {
    setLoading(true);
    setApiError(null);
    try {
      const res = await HttpService.get<Produto[]>("/api/produtos?apenasAtivos=true");
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
  }, []);

  // Produtos podem ter sido cadastrados em outra aba: recarrega cada vez que a aba fica visível.
  useEffect(() => {
    if (!active) return;
    void carregarProdutos();
    void carregarSessao();
  }, [active, carregarProdutos, carregarSessao]);

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
        const errorMessage = res.error || "Erro ao abrir caixa.";
        setApiError(errorMessage);
      }
    } catch (err: any) {
      setApiError(err?.message || "Erro ao abrir caixa.");
    } finally {
      setAbrirCaixaLoading(false);
    }
  }

  async function abrirFechamento() {
    setFecharLoading(true);
    setFechamentoErro(null);
    const res = await HttpService.get<ResumoCaixa>("/api/caixa/resumo");
    if (res.success && res.data) {
      setResumo(res.data);
      setValorFechamento(res.data.valorEsperado.toFixed(2).replace(".", ","));
      setFechandoCaixa(true);
    } else {
      setFechamentoErro(res.error || "Nao foi possivel carregar o resumo do caixa.");
    }
    setFecharLoading(false);
  }

  async function confirmarFechamento() {
    if (!sessaoCaixa) return;

    const valor = parseFloat(valorFechamento.replace(",", "."));
    if (isNaN(valor) || valor < 0) {
      setFechamentoErro("Informe um valor de fechamento valido (maior ou igual a zero).");
      return;
    }

    setFecharLoading(true);
    setFechamentoErro(null);
    try {
      const res = await HttpService.post<SessaoCaixa>(`/api/caixa/fechar/${sessaoCaixa.id}`, {
        valorFechamento: valor,
      });
      if (res.success) {
        setFechandoCaixa(false);
        setResumo(null);
        setSessaoCaixa(null);
        setCart([]);
        setNotice(`Caixa encerrado — ${currency.format(valor)}`);
      } else {
        setFechamentoErro(res.error || "Erro ao encerrar o caixa.");
      }
    } catch (err: any) {
      setFechamentoErro(err?.message || "Erro de conexao com o servidor.");
    } finally {
      setFecharLoading(false);
    }
  }

  const filtered = useMemo(
    () =>
      produtos.filter((p) => `${p.nome} ${p.codigo} Geral`.toLowerCase().includes(query.toLowerCase())),
    [produtos, query],
  );
  const total = useMemo(
    () => cart.reduce((sum, item) => sum + item.precoVenda * item.quantity, 0),
    [cart],
  );

  function addProduct(produto: Produto) {
    setVendaErro(null);
    setCart((current) => {
      const existing = current.find((item) => item.id === produto.id);
      if (existing)
        return current.map((item) =>
          item.id === produto.id ? { ...item, quantity: item.quantity + 1 } : item,
        );
      return [...current, { ...produto, quantity: 1 }];
    });
    setNotice(`${produto.nome} adicionado`);
  }

  function changeQuantity(id: number, change: number) {
    setCart((current) =>
      current
        .map((item) =>
          item.id === id ? { ...item, quantity: Math.max(0, item.quantity + change) } : item,
        )
        .filter((item) => item.quantity > 0),
    );
  }

  function cancelSale() {
    if (cart.length === 0 || window.confirm("Cancelar a venda atual e remover todos os itens?")) {
      setCart([]);
      setVendaErro(null);
      setNotice("Venda cancelada");
      searchRef.current?.focus();
    }
  }

  /**
   * Registra a venda de verdade (API -> SQLite). O carrinho só é limpo quando a API
   * confirma: em caso de erro o operador não perde o que já foi lançado.
   */
  async function finishSale() {
    if (!cart.length || concluindo) return;

    setConcluindo(true);
    setVendaErro(null);
    try {
      const payload: CriarVendaRequest = {
        sessaoCaixaId: sessaoCaixa?.id ?? null,
        formaPagamento: payment,
        itens: cart.map((line) => ({ produtoId: line.id, quantidade: line.quantity })),
      };

      const res = await HttpService.post<Venda>("/api/vendas", payload);
      if (res.success && res.data) {
        setCart([]);
        setNotice(
          `Venda de ${currency.format(res.data.valorTotal)} concluida com sucesso (${res.data.quantidadeItens} itens)`,
        );
        await carregarSessao();
        searchRef.current?.focus();
      } else {
        setVendaErro(res.error || "Erro ao registrar a venda. Tente novamente.");
      }
    } catch (err: any) {
      setVendaErro(err?.message || "Falha na comunicacao com o servidor.");
    } finally {
      setConcluindo(false);
    }
  }

  if (!sessaoCaixa) {
    return (
      <div className="flex h-full items-center justify-center bg-background p-4">
        <div className="w-full max-w-sm rounded-md border border-border bg-surface p-6 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold">Abrir Caixa</h2>
          {mesaIdInicial && (
            <p className="mb-3 text-xs text-muted-foreground">
              Mesa vinculada:{" "}
              <span className="font-medium text-foreground">
                {mesaNumeroInicial ? `Mesa ${String(mesaNumeroInicial).padStart(2, "0")}` : `#${mesaIdInicial}`}
              </span>
            </p>
          )}
          <p className="mb-4 text-xs text-muted-foreground">
            Abra uma sessao de caixa para iniciar as vendas.
          </p>
          <label className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground">
            Valor inicial (R$)
          </label>
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
    <>
      <div className="grid h-full min-h-0 grid-cols-[minmax(0,1fr)_minmax(360px,42%)] bg-background max-[900px]:grid-cols-1 max-[900px]:overflow-auto">
        <section className="flex min-h-0 flex-col border-r border-border max-[900px]:min-h-[520px] max-[900px]:border-r-0 max-[900px]:border-b">
          <div className="border-b border-border bg-surface p-4">
            <label
              htmlFor="product-search"
              className="mb-2 block text-xs font-semibold uppercase text-muted-foreground"
            >
              Buscar produto
            </label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 size-5 -translate-y-1/2 text-muted-foreground" />
              <Input
                ref={searchRef}
                id="product-search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Digite o nome ou o codigo"
                className="h-11 bg-background pl-11 text-base shadow-none"
              />
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
                  <Button
                    key={produto.id}
                    variant="outline"
                    onClick={() => addProduct(produto)}
                    className="h-auto min-h-24 items-start justify-between whitespace-normal rounded-md p-3 text-left shadow-none"
                  >
                    <span className="flex h-full min-w-0 flex-col items-start">
                      <span className="line-clamp-2 text-sm font-semibold leading-snug">
                        {produto.nome}
                      </span>
                      <span className="mt-1 text-xs font-normal text-muted-foreground">Geral · — un.</span>
                      <span className="mt-auto pt-3 text-base font-bold text-primary">
                        {currency.format(produto.precoVenda)}
                      </span>
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
            <div className="flex items-center gap-2">
              <ShoppingCart className="size-4 text-primary" />
              <h2 className="text-sm font-semibold">Itens da venda</h2>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">
                {cart.reduce((sum, item) => sum + item.quantity, 0)} itens
              </span>
              <Button
                size="sm"
                variant="outline"
                className="h-8 gap-1.5 shadow-none"
                onClick={abrirFechamento}
                disabled={fecharLoading}
                title="Encerrar o expediente"
              >
                <Lock className="size-3.5" />
                {fecharLoading ? "Carregando..." : "Fechar caixa"}
              </Button>
            </div>
          </div>
          <div className="min-h-0 flex-1 overflow-auto">
            {cart.length ? (
              cart.map((item) => (
                <div
                  key={item.id}
                  className="grid grid-cols-[1fr_auto] gap-3 border-b border-border p-4"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{item.nome}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {currency.format(item.precoVenda)} por unidade
                    </p>
                  </div>
                  <p className="text-right text-sm font-semibold">
                    {currency.format(item.precoVenda * item.quantity)}
                  </p>
                  <div className="flex items-center gap-1">
                    <Button
                      size="icon"
                      variant="outline"
                      className="size-8 shadow-none"
                      onClick={() => changeQuantity(item.id, -1)}
                      aria-label={`Diminuir ${item.nome}`}
                    >
                      <Minus className="size-3.5" />
                    </Button>
                    <span className="w-9 text-center text-sm font-semibold tabular-nums">
                      {item.quantity}
                    </span>
                    <Button
                      size="icon"
                      variant="outline"
                      className="size-8 shadow-none"
                      onClick={() => changeQuantity(item.id, 1)}
                      aria-label={`Aumentar ${item.nome}`}
                    >
                      <Plus className="size-3.5" />
                    </Button>
                  </div>
                  <Button
                    size="icon"
                    variant="ghost"
                    className="ml-auto size-8 text-muted-foreground hover:text-destructive"
                    onClick={() =>
                      setCart((current) => current.filter((line) => line.id !== item.id))
                    }
                    aria-label={`Remover ${item.nome}`}
                  >
                    <Trash2 className="size-3.5" />
                  </Button>
                </div>
              ))
            ) : (
              <div className="flex h-full min-h-48 flex-col items-center justify-center px-5 text-center">
                <ShoppingCart className="mb-3 size-8 text-muted-foreground/60" />
                <p className="text-sm font-medium">Venda sem itens</p>
                <p className="mt-1 max-w-60 text-xs text-muted-foreground">
                  Selecione um produto ao lado para iniciar a venda.
                </p>
              </div>
            )}
          </div>
          <div className="shrink-0 border-t border-border bg-background p-4">
            {vendaErro && (
              <div className="mb-3 rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs font-medium text-destructive">
                {vendaErro}
              </div>
            )}
            {notice && !vendaErro && (
              <div className="mb-3 flex items-center gap-2 rounded-md bg-success-soft px-3 py-2 text-xs font-medium text-success-foreground">
                <CheckCircle2 className="size-4" />
                {notice}
              </div>
            )}
            <div className="mb-4 flex items-end justify-between">
              <div>
                <p className="text-xs font-medium uppercase text-muted-foreground">Total da venda</p>
                <p className="mt-1 text-3xl font-bold tracking-normal text-foreground">
                  {currency.format(total)}
                </p>
                <p className="mt-1 text-[11px] text-muted-foreground">
                  Caixa de {sessaoCaixa.usuarioNome}
                  {sessaoCaixa.mesaNumero ? ` · Mesa ${String(sessaoCaixa.mesaNumero).padStart(2, "0")}` : ""}
                  {sessaoCaixa.quantidadeVendas > 0
                    ? ` · ${sessaoCaixa.quantidadeVendas} vendas (${currency.format(sessaoCaixa.totalVendas)})`
                    : ""}
                </p>
              </div>
              <div className="w-40">
                <Select value={payment} onValueChange={setPayment}>
                  <SelectTrigger className="shadow-none">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="dinheiro">Dinheiro</SelectItem>
                    <SelectItem value="cartao">Cartao</SelectItem>
                    <SelectItem value="pix">PIX</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid grid-cols-[auto_1fr] gap-2">
              <Button
                variant="outline"
                onClick={cancelSale}
                disabled={!cart.length}
                className="h-11 shadow-none"
              >
                <XCircle className="size-4" />
                Cancelar
              </Button>
              <Button onClick={finishSale} disabled={!cart.length || concluindo} className="h-11">
                <CheckCircle2 className="size-4" />
                {concluindo ? "Registrando..." : "Concluir venda"}
              </Button>
            </div>
          </div>
        </section>
      </div>

      {fechandoCaixa && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm">
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Fechar caixa"
            className="w-full max-w-md rounded-md border border-border bg-surface p-6 shadow-window"
          >
            <div className="flex items-center gap-2">
              <CircleDollarSign className="size-5 text-primary" />
              <h2 className="text-base font-semibold">Fechamento de caixa</h2>
            </div>

            {fechamentoErro && (
              <p className="mt-3 rounded border border-destructive/40 bg-destructive/10 p-2 text-xs text-destructive">
                {fechamentoErro}
              </p>
            )}

            {resumo && (
              <dl className="mt-4 space-y-2 text-sm">
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Valor inicial</dt>
                  <dd className="font-medium">{currency.format(resumo.valorInicial)}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Vendas ({resumo.quantidadeVendas})</dt>
                  <dd className="font-medium">{currency.format(resumo.totalVendas)}</dd>
                </div>
                {resumo.porFormaPagamento.map((forma) => (
                  <div key={forma.formaPagamento} className="flex justify-between pl-3 text-xs">
                    <dt className="text-muted-foreground capitalize">
                      {forma.formaPagamento} ({forma.quantidade})
                    </dt>
                    <dd className="font-medium">{currency.format(forma.total)}</dd>
                  </div>
                ))}
                <div className="flex justify-between border-t border-border pt-3">
                  <dt className="font-semibold">Valor esperado</dt>
                  <dd className="font-bold text-primary">{currency.format(resumo.valorEsperado)}</dd>
                </div>
              </dl>
            )}

            <div className="mt-5">
              <label
                htmlFor="valor-fechamento"
                className="mb-1.5 block text-xs font-semibold uppercase text-muted-foreground"
              >
                Valor contado em caixa (R$)
              </label>
              <Input
                id="valor-fechamento"
                type="number"
                step="0.01"
                min="0"
                value={valorFechamento}
                onChange={(e) => setValorFechamento(e.target.value)}
                className="shadow-none"
              />
              <p className="mt-1 text-[11px] text-muted-foreground">
                Encerrar o expediente devolve a mesa para livre e bloqueia novas vendas nesta sessão.
              </p>
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <Button
                variant="outline"
                className="shadow-none"
                onClick={() => {
                  setFechandoCaixa(false);
                  setFechamentoErro(null);
                }}
                disabled={fecharLoading}
              >
                Cancelar
              </Button>
              <Button onClick={confirmarFechamento} disabled={fecharLoading}>
                {fecharLoading ? "Encerrando..." : "Confirmar fechamento"}
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
