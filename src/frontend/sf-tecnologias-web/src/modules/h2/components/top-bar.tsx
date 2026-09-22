import { ContactRound, LayoutGrid, LogOut, Package, Tag, ShoppingCart } from "lucide-react";
import { Button } from "@/components/ui/button";
import logoH2 from "/logo-h2.png";

export type ModuleId = "caixa" | "categorias" | "produtos" | "clientes" | "mesas";

const navigation = [
  { id: "caixa" as const, label: "Caixa", shortcut: "F2", icon: ShoppingCart },
  { id: "categorias" as const, label: "Categorias", shortcut: "F6", icon: Tag },
  { id: "produtos" as const, label: "Produtos", shortcut: "F5", icon: Package },
  { id: "clientes" as const, label: "Clientes", shortcut: "F3", icon: ContactRound },
  { id: "mesas" as const, label: "Mesas", shortcut: "F4", icon: LayoutGrid },
];

export function TopBar({
  active,
  onOpen,
  empresaNome = "H2 Conveniência",
  onLogout,
}: {
  active: ModuleId | undefined;
  onOpen: (id: ModuleId) => void;
  empresaNome?: string;
  onLogout?: () => void;
}) {
  return (
    <header className="flex h-16 shrink-0 items-center border-b border-border bg-surface px-4 lg:px-6">
      <div className="flex min-w-0 items-center gap-3 border-r border-border pr-5">
        <img
          src={logoH2}
          alt="H2 Conveniência"
          className="size-9 rounded-md object-contain"
        />
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold text-foreground">{empresaNome}</p>
          <p className="truncate text-xs text-muted-foreground">Sistema por SF Tecnologias</p>
        </div>
      </div>

      <nav className="ml-4 flex h-full items-center gap-1" aria-label="Módulos principais">
        {navigation.map(({ id, label, shortcut, icon: Icon }) => (
          <Button
            key={id}
            variant="ghost"
            onClick={() => onOpen(id)}
            className={`h-10 gap-2 px-3 ${
              active === id
                ? "bg-brand-soft text-primary hover:bg-brand-soft"
                : "text-muted-foreground"
            }`}
          >
            <Icon className="size-4" />
            <span>{label}</span>
            <kbd className="ml-1 hidden rounded border border-border bg-background px-1.5 py-0.5 font-sans text-[10px] font-medium text-muted-foreground lg:inline">
              {shortcut}
            </kbd>
          </Button>
        ))}
      </nav>

      <div className="ml-auto flex items-center gap-3">
        <div className="hidden items-center gap-2 text-xs text-muted-foreground md:flex">
          <span className="size-2 rounded-full bg-success" />
          Operação local
        </div>
        {onLogout && (
          <Button type="button" variant="ghost" className="h-10 gap-2 px-3 text-muted-foreground" onClick={onLogout}>
            <LogOut className="size-4" />
            Sair
          </Button>
        )}
      </div>
    </header>
  );
}

