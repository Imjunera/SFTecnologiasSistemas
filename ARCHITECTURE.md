# Arquitetura do SF Tecnologias

## Organização da Solução

A solução está organizada da seguinte forma:

- `/src`: Contém o código-fonte da aplicação.
  - `/backend`: Projetos do backend em ASP.NET Core.
    - `/SF.Tecnologias.Api`: Projeto de API ASP.NET Core.
    - `/SF.Tecnologias.Application`: Camada de aplicação (casos de uso, DTOs, interfaces).
    - `/SF.Tecnologias.Domain`: Camada de domínio (entidades, valor objetos, interfaces de repositório).
    - `/SF.Tecnologias.Infrastructure`: Camada de infraestrutura (implementações de repositório, configuração de banco de dados, etc.).
  - `/frontend`: Aplicação frontend React com Vite e TypeScript.
    - `/sf-tecnologias-web`: Projeto React.
  - `/desktop`: Aplicação desktop Electron.
- `/tests`: Projetos de teste.
  - `/SF.Tecnologias.Infrastructure.Tests`: Testes da camada de infraestrutura.

## Separação de Responsabilidades

- **Domínio**: Contém as regras de negócio puras, entidades e interfaces de repositório. Não depende de nenhuma outra camada.
- **Aplicação**: Contém os casos de uso, orquestra o domínio e a infraestrutura. Dependente apenas do domínio.
- **Infraestrutura**: Contém detalhes de implementação como acesso a banco de dados, arquivos, serviços externos. Dependente do domínio e da aplicação.
- **API**: Pontos de entrada HTTP, controle, dependente da aplicação e da infraestrutura.

## Tecnologias Utilizadas

- Backend: ASP.NET Core 8.0, C# 12
- Frontend: React 19, Vite 8, TypeScript 6
- Desktop: Electron 44
- Banco de dados: PostgreSQL (local para desenvolvimento)
- ORM: Entity Framework Core 8 com Npgsql
- Testes: xUnit

## Estratégia de Testes

- Testes unitários para domínio, aplicação e infraestrutura.
- Testes de integração para a API (a serem implementados em fases posteriores).
- Testes end-to-end para o desktop e frontend (a serem implementados em fases posteriores).

## Decisões Arquiteturais

- A API é construida com minimal APIs e Swagger para documentação.
- O frontend é um aplicativo de página única (SPA) React com Vite para build rápido.
- O Electron carrega o frontend diretamente (em desenvolvimento do servidor Vite, em produção dos arquivos construídos).
- A autenticação será implementada com JWT na API (fase 03).
- O isolamento de dados entre empresas será feito por meio de um identificador de empresa em todas as tabelas e filtros de consulta globalmente aplicados (fase 03).

## Dependências Adicionadas

- Backend:
  - Swashbuckle.AspNetCore (Swagger)
  - Microsoft.EntityFrameworkCore
  - Npgsql.EntityFrameworkCore.PostgreSQL
- Frontend:
  - react
  - react-dom
  - @vitejs/plugin-react
  - typescript
  - vite
- Desktop:
  - electron

## Resultados da Validação

- Build do backend: sucesso
- Testes do backend: sucesso (1 teste passando)
- Build do frontend: sucesso
- Build do desktop: sucesso (dependências instaladas, script start disponível)

## Próximos Passos

- Fase 02: Fundação da API (injeção de dependências, configuração por ambiente, logging, tratamento de erros, health check, CORS, configuração inicial do PostgreSQL com EF Core e Npgsql).