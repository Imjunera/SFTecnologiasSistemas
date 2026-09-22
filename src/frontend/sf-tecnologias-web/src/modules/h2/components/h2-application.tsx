import { useCallback, useEffect, useState } from "react";
import { ContactRound, LayoutGrid, Package, Tag, ShoppingCart, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { TopBar, type ModuleId } from "./top-bar";
import { CheckoutModule } from "./checkout-module";
import { ProductsModule } from "./products-module";
import { CategoriesModule } from "./categories-module";
import { CustomersModule } from "./customers-module";
import { TablesModule } from "./tables-module";

const moduleInfo = {
  caixa: { title: "Caixa", icon: ShoppingCart },
  categorias: { title: "Categorias", icon: Tag },
  produtos: { title: "Produtos", icon: Package },
  clientes: { title: "Clientes", icon: ContactRound },
  mesas: { title: "Mesas", icon: LayoutGrid },
};

export function H2Application({
  empresaNome = "H2 Conveniência",
  usuarioNome,
  onLogout,
}: {
  empresaNome?: string;
  usuarioNome?: string;
  onLogout?: () => void;
}) {
  const [openModules, setOpenModules] = useState<ModuleId[]>([]);
  const [active, setActive] = useState<ModuleId>();
  const [dirty, setDirty] = useState<Record<ModuleId, boolean>>({
    caixa: false,
    categorias: false,
    produtos: false,
    clientes: false,
    mesas: false,
  });
  const [mesaIdAberta, setMesaIdAberta] = useState<number | null>(null);

  const setCheckoutDirty = useCallback((value: boolean) => {
    setDirty((current) => (current.caixa === value ? current : { ...current, caixa: value }));
  }, []);

  const setCategoriesDirty = useCallback((value: boolean) => {
    setDirty((current) => (current.categorias === value ? current : { ...current, categorias: value }));
  }, []);

  const setProductsDirty = useCallback((value: boolean) => {
    setDirty((current) => (current.produtos === value ? current : { ...current, produtos: value }));
  }, []);

  const setCustomersDirty = useCallback((value: boolean) => {
    setDirty((current) => (current.clientes === value ? current : { ...current, clientes: value }));
  }, []);

  const setTablesDirty = useCallback((value: boolean) => {
    setDirty((current) => (current.mesas === value ? current : { ...current, mesas: value }));
  }, []);

  const openModule = useCallback((id: ModuleId) => {
    setOpenModules((current) => (current.includes(id) ? current : [...current, id]));
    setActive(id);
  }, []);

  const handleAbrirCaixaDaMesa = useCallback((mesaId: number, _mesaNumero: number) => {
    setMesaIdAberta(mesaId);
    openModule("caixa");
  }, [openModule]);

  const closeModule = useCallback(
    (id: ModuleId) => {
      if (dirty[id] && !window.confirm("Existem alterações em andamento. Deseja fechar mesmo assim?")) {
        return;
      }
      setOpenModules((current) => {
        const next = current.filter((item) => item !== id);
        setActive((currentActive) => (currentActive === id ? next.at(-1) : currentActive));
        return next;
      });
      setDirty((current) => ({ ...current, [id]: false }));
      if (id === "caixa") setMesaIdAberta(null);
    },
    [dirty],
  );

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (
        event.key === "F2" ||
        event.key === "F3" ||
        event.key === "F4" ||
        event.key === "F5" ||
        event.key === "F6"
      ) {
        event.preventDefault();
      }
      if (event.key === "F2") openModule("caixa");
      if (event.key === "F6") openModule("categorias");
      if (event.key === "F5") openModule("produtos");
      if (event.key === "F3") openModule("clientes");
      if (event.key === "F4") openModule("mesas");
      if (event.key === "Escape" && active) closeModule(active);
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [active, closeModule, openModule]);

  return (
    <div className="flex h-full min-h-[600px] flex-col overflow-hidden bg-workspace text-foreground">
      <TopBar active={active} onOpen={openModule} empresaNome={empresaNome} onLogout={onLogout} />
      <div className="flex min-h-0 flex-1 flex-col p-3 lg:p-4">
        {openModules.length > 0 && (
          <div className="flex h-10 shrink-0 items-end gap-1 overflow-x-auto px-1">
            {openModules.map((id) => {
              const { title, icon: Icon } = moduleInfo[id];
              return (
                <div
                  key={id}
                  className={`flex h-9 min-w-36 items-center gap-2 rounded-t-md border border-b-0 px-3 text-xs font-medium ${
                    active === id
                      ? "border-border bg-surface text-foreground"
                      : "border-transparent bg-muted text-muted-foreground"
                  }`}
                >
                  <Button
                    variant="ghost"
                    className="h-auto flex-1 justify-start gap-2 p-0 hover:bg-transparent"
                    onClick={() => setActive(id)}
                  >
                    <Icon className="size-3.5" />
                    {title}
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    className="size-6"
                    onClick={() => closeModule(id)}
                    aria-label={`Fechar ${title}`}
                  >
                    <X className="size-3.5" />
                  </Button>
                </div>
              );
            })}
          </div>
        )}
        <div
          className={`relative min-h-0 flex-1 ${
            openModules.length
              ? "rounded-md rounded-tl-none border border-border bg-surface shadow-window"
              : ""
          }`}
        >
          {!active && (
            <div className="flex h-full flex-col items-center justify-center text-center">
              <div className="mb-4 flex size-12 items-center justify-center rounded-md border border-border bg-surface">
                <ShoppingCart className="size-5 text-muted-foreground" />
              </div>
              <h1 className="text-base font-semibold">{empresaNome}</h1>
              <p className="mt-1 text-sm text-muted-foreground">
                Selecione um módulo na barra superior para começar.
              </p>
              <p className="mt-4 text-xs text-muted-foreground">
                F2 Caixa · F6 Categorias · F5 Produtos · F3 Clientes · F4 Mesas
              </p>
              {usuarioNome && (
                <p className="mt-2 text-xs text-muted-foreground">
                  Operador: <span className="font-medium text-foreground">{usuarioNome}</span>
                </p>
              )}
            </div>
          )}
          {openModules.map((id) => (
            <section
              key={id}
              aria-label={moduleInfo[id].title}
              className={`absolute inset-0 overflow-hidden rounded-[5px] ${
                active === id ? "block" : "hidden"
              }`}
            >
              {id === "caixa" && <CheckoutModule onDirtyChange={setCheckoutDirty} mesaIdInicial={mesaIdAberta} />}
              {id === "categorias" && <CategoriesModule onDirtyChange={setCategoriesDirty} />}
              {id === "produtos" && <ProductsModule onDirtyChange={setProductsDirty} />}
              {id === "clientes" && <CustomersModule onDirtyChange={setCustomersDirty} />}
              {id === "mesas" && <TablesModule onDirtyChange={setTablesDirty} onAbrirCaixa={handleAbrirCaixaDaMesa} />}
            </section>
          ))}
        </div>
      </div>
      <footer className="flex h-7 shrink-0 items-center justify-between border-t border-border bg-surface px-4 text-[11px] text-muted-foreground">
        <span>{empresaNome} — SF Tecnologias</span>
        <span>{usuarioNome ? `Usuário: ${usuarioNome}` : "Ambiente Autenticado"}</span>
      </footer>
    </div>
  );
}
