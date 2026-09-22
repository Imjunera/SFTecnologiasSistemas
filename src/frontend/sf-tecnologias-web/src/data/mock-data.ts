export type Product = {
  id: number;
  name: string;
  category: string;
  price: number;
  stock: number;
};

export type Customer = {
  id: number;
  name: string;
  phone: string;
  email: string;
  lastPurchase: string;
};

export type TableStatus = "Livre" | "Ocupada" | "Reservada";

export type StoreTable = {
  id: number;
  status: TableStatus;
  guests: number;
  openedAt?: string;
  total?: number;
};

export const products: Product[] = [
  { id: 1, name: "Coca-Cola Lata 350ml", category: "Bebidas", price: 6.5, stock: 28 },
  { id: 2, name: "Água Mineral 500ml", category: "Bebidas", price: 3.5, stock: 42 },
  { id: 3, name: "Suco de Laranja 300ml", category: "Bebidas", price: 7.9, stock: 16 },
  { id: 4, name: "Energético 269ml", category: "Bebidas", price: 10.9, stock: 12 },
  { id: 5, name: "Pão de Queijo", category: "Lanches", price: 5.5, stock: 18 },
  { id: 6, name: "Coxinha de Frango", category: "Lanches", price: 8.5, stock: 10 },
  { id: 7, name: "Sanduíche Natural", category: "Lanches", price: 13.9, stock: 8 },
  { id: 8, name: "Batata Chips 90g", category: "Salgadinhos", price: 9.9, stock: 15 },
  { id: 9, name: "Amendoim Torrado 100g", category: "Salgadinhos", price: 6.9, stock: 21 },
  { id: 10, name: "Chocolate ao Leite 90g", category: "Doces", price: 8.9, stock: 19 },
  { id: 11, name: "Paçoca Unidade", category: "Doces", price: 1.5, stock: 55 },
  { id: 12, name: "Biscoito Recheado", category: "Mercearia", price: 4.9, stock: 24 },
];

export const initialCustomers: Customer[] = [
  { id: 1, name: "Mariana Alves", phone: "(11) 98765-4321", email: "mariana@email.com", lastPurchase: "Hoje, 14:32" },
  { id: 2, name: "Carlos Eduardo Lima", phone: "(11) 99812-3407", email: "carlos.lima@email.com", lastPurchase: "17/09/2026" },
  { id: 3, name: "Beatriz Nogueira", phone: "(11) 97654-1120", email: "beatriz@email.com", lastPurchase: "15/09/2026" },
  { id: 4, name: "Rafael Martins", phone: "(11) 98821-5594", email: "rafael@email.com", lastPurchase: "12/09/2026" },
  { id: 5, name: "Ana Paula Souza", phone: "(11) 96731-8200", email: "ana.souza@email.com", lastPurchase: "08/09/2026" },
];

export const initialTables: StoreTable[] = [
  { id: 1, status: "Livre", guests: 0 },
  { id: 2, status: "Ocupada", guests: 2, openedAt: "18:42", total: 38.8 },
  { id: 3, status: "Livre", guests: 0 },
  { id: 4, status: "Reservada", guests: 4 },
  { id: 5, status: "Ocupada", guests: 3, openedAt: "19:10", total: 67.4 },
  { id: 6, status: "Livre", guests: 0 },
  { id: 7, status: "Livre", guests: 0 },
  { id: 8, status: "Reservada", guests: 2 },
  { id: 9, status: "Livre", guests: 0 },
  { id: 10, status: "Ocupada", guests: 1, openedAt: "19:28", total: 14.5 },
  { id: 11, status: "Livre", guests: 0 },
  { id: 12, status: "Livre", guests: 0 },
];

export const currency = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

