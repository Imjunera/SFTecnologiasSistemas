# ADR-0001 — Autenticação para distribuição de releases privados

**Status:** aceito (mecanismo atual); GitHub App registrado como alvo arquitetural quando houver backend compartilhado.
**Data:** 2026-10-03
**Contexto:** seção 19 do contrato de arquitetura (SF Tecnologias — plataforma, sistemas distribuídos e atualização).

## Problema

O desktop precisa baixar pacotes de sistemas empresariais de **repositórios privados** (GitHub Releases) sem que nenhum segredo permanente seja embutido no `.exe` (§19 proíbe PAT permanente, senha, client secret, private key etc. dentro do executável).

## Avaliação dos mecanismos (documentação atual do GitHub)

| Critério | Fine-grained PAT (atual) | GitHub App + installation tokens |
|---|---|---|
| Validade | configurável (minutos a ~1 ano) | **1 hora** (`POST /app/installations/{id}/access_tokens`) |
| Escopo | por repositório + permissões granulares (Contents: read) | por instalação; cada token pode restringir `repositories` e `permissions` **por requisição** |
| Renovação | manual (expira e recria) | automática (novo POST com JWT do App; SDKs renovam sozinhos) |
| Revogação | revogar o PAT | revogar/remover a instalação do App |
| Segredo no cliente | o próprio token (fora do `.exe`, em `release-auth.json` no DataRoot ou `SF_RELEASE_TOKEN`) | **a private key do App** — se mintar tokens no cliente, pior que PAT; só é seguro se a chave ficar num **broker servidor** |
| Formato (2026) | `github_pat_…` | rollout do formato stateless `ghs_APPID_JWT` desde 27/04/2026 |
| Endpoints de release asset | suportados | suportados (`rest/releases/assets`) |

## Decisão

1. **Hoje (cliente puro, sem servidor):** manter **fine-grained PAT de leitura**, escopo de *um* repositório de releases, permissão mínima (`Contents: read-only`), entregue FORA do `.exe` via `%ProgramData%\SF Tecnologias\configuration\release-auth.json` (ou env `SF_RELEASE_TOKEN`). É a melhor opção quando não existe backend: segredo de escopo mínimo, revogável, fora do binário. Implementação atual: `src/desktop/systems/release-source.js`.
2. **Alvo (quando existir infraestrutura compartilhada — candidato: Supabase):** um **broker** seguro (edge function/serviço) guarda a private key do **GitHub App** instalada apenas nos repositórios de releases e devolve **installation tokens de 1h, read-only, escopo por repositório solicitado**. O desktop nunca guarda segredo permanente: recebe o token de curta duração, usa e descarta. Renovação e revogação ficam no GitHub; exposição fica limitada a 1 hora.
3. **Proibido:** qualquer variante que embuta PAT/client secret/private key no `.exe`, ou que distribua a private key do App para máquinas de clientes.

## Consequências

- Distribuição privada funciona hoje com o mecanismo 1; a troca para o mecanismo 2 é **transparente** para o resto da plataforma (só `release-source.js`/downloader mudam a obtenção do header `Authorization`).
- O catálogo/manifesto não muda; apenas a autenticação da consulta de release.
- Revisitar este ADR quando a decisão sobre Supabase (§24) for tomada.
