# Contrato de Instalação — SF Tecnologias

## 1. Visão Geral

O SF Tecnologias é composto por três componentes instaláveis:

- **Desktop** (Electron) — Interface do usuário
- **API** (ASP.NET Core) — Backend / persistência
- **Updater** (C#/.NET) — Atualizador independente

A separação entre **binários** (substituíveis) e **dados** (preservados) é fundamental para permitir atualizações seguras sem perda de dados.

## 2. Estrutura de Diretórios

### 2.1 Binários (substituíveis pelo updater)

```
%ProgramFiles%\SF Tecnologias\
├── SF Tecnologias BETA.exe              ← Executável do Desktop
├── SF.Updater.exe                       ← Atualizador independente
├── chrome_100_percent.pak               ← Recursos Electron
├── chrome_200_percent.pak
├── d3dcompiler_47.dll
├── ffmpeg.dll
├── icudtl.dat
├── locales\                             ← Localizações Electron
├── resources.pak
├── resources/
│   ├── api/
│   │   ├── SFTecnologiasApi.exe         ← API self-contained
│   │   └── appsettings.json             ← Config da API (reference)
│   ├── elevate.exe
│   └── renderer/
│       ├── index.html                   ← Frontend build
│       ├── assets\                      ← CSS/JS do frontend
│       └── ... (imagens, ícones)
├── snapshot_blob.bin
├── v8_context_snapshot.bin
├── vk_swiftshader_icd.json
├── vulkan-1.dll
└── Desinstalar.bat                      ← Gerado pelo instalador
```

**Regra**: Toda esta pasta pode ser substituída por atualização. Nenhum arquivo aqui é irreversível.
**Exceção**: `SF.Updater.exe` não é substituído enquanto estiver em execução (o updater se auto-atualiza separadamente).

### 2.2 Dados (preservados — NUNCA substituídos pelo updater)

```
%ProgramData%\SF Tecnologias\
├── data\
│   ├── SFTecnologias.db                 ← Banco SQLite (persistente)
│   ├── SFTecnologias.db-shm             ← WAL shared memory (SQLite)
│   └── SFTecnologias.db-wal             ← WAL log (SQLite)
├── config\
│   └── updater-state.json               ← Estado da última atualização
├── backups\
│   ├── desktop-1.0.0\                   ← Backup dos binários do Desktop
│   └── api-1.0.0\                       ← Backup dos binários da API
├── logs\
│   ├── updater-2026-09-21.log           ← Logs do updater
│   └── api\                             ← Logs da API
└── updates\
    └── temp\                            ← Downloads temporários (limpos após update)
```

**Regra**: Nenhum updater deve modificar, mover ou excluir arquivos nesta estrutura (exceto `updates/temp/` e `config/updater-state.json`).

### 2.3 Dados por Usuário (logs do Desktop)

```
%LOCALAPPDATA%\SF Tecnologias BETA\
└── debug.log                            ← Log de debug do Electron
```

**Regra**: Gerenciado pelo Electron. Não é afetado por atualizações.

## 3. Variáveis de Ambiente

A API recebe a localização do banco via variável de ambiente, que sobrescreve o `appsettings.json`:

| Variável                               | Valor                                                            | Fonte                  |
| -------------------------------------- | ---------------------------------------------------------------- | ---------------------- |
| `ASPNETCORE_ENVIRONMENT`               | `Production`                                                     | Desktop ou serviço     |
| `ASPNETCORE_URLS`                      | `http://localhost:5000`                                          | Desktop ou serviço     |
| `DatabaseProvider`                     | `SQLite`                                                         | Desktop ou serviço     |
| `ConnectionStrings__DefaultConnection` | `Data Source=%ProgramData%\SF Tecnologias\data\SFTecnologias.db` | Desktop ou serviço     |
| `Jwt__Secret`                          | Segredo HMAC ≥32 chars (gerado no install)                       | Serviço (Setup-SF.ps1) |

**Por que variável de ambiente?**

- O `appsettings.json` é empacotado com os binários e seria substituído em atualizações
- A variável de ambiente garante que o caminho do banco seja sempre resolvido em tempo de execução
- Permite que Desktop e Serviço Windows apontem para o mesmo banco

## 4. Permissões

### 4.1 Conta do Serviço Windows

| Propriedade           | Valor                  |
| --------------------- | ---------------------- |
| Nome do serviço       | `SFTecnologiasApi`     |
| Conta de execução     | `LocalSystem` (padrão) |
| Tipo de inicialização | Automático             |

**Permissões necessárias para `LocalSystem`:**

- Leitura em `%ProgramFiles%\SF Tecnologias\` ✅ (LocalSystem tem acesso)
- Escrita em `%ProgramData%\SF Tecnologias\data\` ✅ (LocalSystem tem acesso total)

### 4.2 Conta do Usuário (Desktop)

| Propriedade | Valor                     |
| ----------- | ------------------------- |
| Processo    | `SF Tecnologias BETA.exe` |
| Conta       | Usuário logado            |

**Permissões necessárias:**

- Leitura em `%ProgramFiles%\SF Tecnologias\` ✅ (usuários comuns têm acesso de leitura)
- Escrita em `%ProgramData%\SF Tecnologias\data\` ✅ (usuários comuns têm acesso de escrita no ProgramData)
- Escrita em `%LOCALAPPDATA%\SF Tecnologias BETA\` ✅ (usuário tem acesso total ao seu LocalAppData)

## 5. Separacao Desktop × API × SQLite

### 5.1 Desktop (Electron)

- **NÃO** acessa SQLite diretamente
- **TODA** comunicação com dados é feita via HTTP para a API (`http://localhost:5000`)
- O Desktop spawna a API como processo filho (quando não está no serviço)
- O Desktop NÃO lê nem escreve no banco de dados

### 5.2 API (ASP.NET Core)

- **ÚNICA** camada que acessa SQLite
- Lê configuração de `appsettings.json` (básico) + variáveis de ambiente (override)
- Cria o banco automaticamente via `EnsureCreatedAsync()` na primeira execução
- Roda como processo filho do Desktop OU como serviço Windows

### 5.3 Updater (SF.Updater.exe)

- **NUNCA** acessa SQLite diretamente
- Substitui apenas arquivos em `%ProgramFiles%\SF Tecnologias\`
- Nunca modifica `%ProgramData%\SF Tecnologias\data\`
- Cria backup dos binários antes de substituir em `%ProgramData%\SF Tecnologias\backups\`
- Registra estado em `%ProgramData%\SF Tecnologias\config\updater-state.json`
- Registra logs em `%ProgramData%\SF Tecnologias\logs\updater-*.log`
- Limpa downloads temporários em `%ProgramData%\SF Tecnologias\updates\temp\`

#### Estado da Atualização (Máquina de Estados)

```
Idle → Downloading → Downloaded → Validating → Validated
→ StoppingServices → ServicesStopped → BackingUp → BackupCompleted
→ UpdatingDesktop → DesktopUpdated → UpdatingApi → ApiUpdated
→ StartingServices → ServicesStarted → HealthCheckPassed → Completed

QUALQUER ESTADO → Failed → (Rollback se aplicável) → Idle
```

#### Arquivos que o Updater PODE modificar

| Caminho                                                  | Ação                                      |
| -------------------------------------------------------- | ----------------------------------------- |
| `%ProgramFiles%\SF Tecnologias\*`                        | Substituir (exceto SF.Updater.exe em uso) |
| `%ProgramData%\SF Tecnologias\config\updater-state.json` | Criar/atualizar                           |
| `%ProgramData%\SF Tecnologias\backups\*`                 | Criar (backup antes de update)            |
| `%ProgramData%\SF Tecnologias\logs\updater-*.log`        | Criar                                     |
| `%ProgramData%\SF Tecnologias\updates\temp\*`            | Criar/deletar (temporário)                |

#### Arquivos que o Updater NUNCA modifica

| Caminho                                                 | Motivo           |
| ------------------------------------------------------- | ---------------- |
| `%ProgramData%\SF Tecnologias\data\*`                   | Dados do usuário |
| `%LOCALAPPDATA%\SF Tecnologias BETA\*`                  | Logs do Desktop  |
| `%ProgramFiles%\SF Tecnologias\SF.Updater.exe` (em uso) | Próprio updater  |

## 6. Arquivos Substituíveis vs Preservados

### Substituíveis (pelo updater)

| Arquivo                              | Motivo                           |
| ------------------------------------ | -------------------------------- |
| `SF Tecnologias BETA.exe`            | Binário do Desktop               |
| `resources/api/SFTecnologiasApi.exe` | Binário da API                   |
| `resources/api/appsettings.json`     | Config da API (refém do binário) |
| `resources/renderer/*`               | Frontend build                   |
| `*.dll`, `*.pak`, `*.bin`            | Recursos Electron                |

### Preservados (NUNCA pelo updater)

| Arquivo                                                  | Motivo                    |
| -------------------------------------------------------- | ------------------------- |
| `%ProgramData%\SF Tecnologias\data\SFTecnologias.db`     | Banco de dados do usuário |
| `%ProgramData%\SF Tecnologias\data\SFTecnologias.db-wal` | WAL do SQLite             |
| `%ProgramData%\SF Tecnologias\data\SFTecnologias.db-shm` | Shared memory do SQLite   |
| `%LOCALAPPDATA%\SF Tecnologias BETA\debug.log`           | Log do Desktop            |

### Condicionais (preservados se existirem)

| Arquivo                                       | Quando preservar |
| --------------------------------------------- | ---------------- |
| `%ProgramData%\SF Tecnologias\data\backups\*` | Sempre preservar |
| `%ProgramData%\SF Tecnologias\data\logs\*`    | Sempre preservar |

## 7. Fluxo de Atualização

```
1. Desktop detecta nova versão (via GitHub Release API)
2. Desktop notifica usuário e solicita atualização
3. Desktop encerra
4. SF.Updater.exe inicia (recebe URL do manifesto via argumento)
5. Updater valida manifesto (SHA-256, versão, compatibilidade)
6. Updater verifica espaço em disco e permissões
7. Updater baixa pacotes para %ProgramData%\SF Tecnologias\updates\temp\
8. Updater valida SHA-256 de cada pacote
9. Updater cria backup dos binários atuais em %ProgramData%\SF Tecnologias\backups\
10. Updater para o serviço API (se registrado) e confirma parada
11. Updater substitui arquivos em %ProgramFiles%\SF Tecnologias\
12. Updater registra estado "Completed"
13. Updater inicia o serviço API
14. Updater aguarda health check da API
15. Updater inicia o Desktop
16. Desktop valida conectividade com API
17. Usuário retoma operação normal
```

#### Fluxo de Falha

```
1. Updater detecta falha em qualquer etapa
2. Updater registra estado "Failed" com detalhes
3. Se backup existe e atualização parcial → Rollback
4. Se API estava rodando → reinicia API
5. Se Desktop estava disponível → reinicia Desktop
6. Updater registra log detalhado do erro
7. Usuário é notificado com instruções de recuperação
```

## 8. Compatibilidade com Instalações Existentes

### Instalação atual (pré-contrato)

- Banco em: relativo ao CWD ou `%LOCALAPPDATA%\SF Tecnologias\SFTecnologias.db`
- API spawna com `ConnectionStrings__DefaultConnection` apontando para `%LOCALAPPDATA%`

### Migração para o contrato

- O `main.js` atualiza para usar `%ProgramData%\SF Tecnologias\data\`
- Usuários existentes terão o banco criado em novo local na primeira execução pós-atualização
- **RISCO**: Dados existentes no local antigo não são migrados automaticamente
- **MITIGAÇÃO**: O Checkpoint 3 deve implementar migração ou detecção de banco existente

## 9. Validação

- [x] Desktop spawna API com variáveis de ambiente corretas
- [x] API cria banco em `%ProgramData%\SF Tecnologias\data\`
- [x] Serviço Windows registrado com variáveis de ambiente
- [x] Serviço Windows tem permissão de escrita em `%ProgramData%`
- [x] Desktop funciona normalmente (não acessa SQLite diretamente)
- [x] `Setup-SF.ps1` cria estrutura de dados correta
- [x] `Setup-SF.ps1` preserva dados em atualização
- [x] `Desinstalar.bat` preserva dados por padrão
- [x] JWT strict (assinatura obrigatória) + `Jwt__Secret` via env em Production
- [x] DevelopmentSeeder roda apenas em Development
- [x] Headers `X-Tenant-Id`/`X-User-Id` removidos (tenant só via JWT)
- [x] Pacote desktop flatten (zip root = layout do InstallDir)
- [x] `build-release.ps1` aponta para `src\updater\SF.Updater\SF.Updater.csproj` (falha alto se ausente)
- [x] `generate-manifest.ps1` exige GitHubOwner/GitHubRepo (URLs absolutas)
- [ ] `SF.Updater.exe` instalado em `%ProgramFiles%\SF Tecnologias\` (requer install real com admin)
- [x] `SF.Updater.exe` pode ser executado independentemente (self-contained, falha limpa sem manifesto)
- [x] Updater cria backup antes de atualizar
- [x] Updater preserva `%ProgramData%` durante atualização (DataDir separado do InstallDir; dados SQLite/empresa/usuario OK no E2E)
- [x] Updater controla serviço Windows corretamente (sc.exe stop/start + timeout/force)
- [x] Updater valida SHA-256 dos pacotes
- [x] Updater registra logs detalhados
- [x] Updater permite rollback em caso de falha
- [x] Health check falhou ⇒ rollback (não Completed)
- [x] Estados intermediários gravados (Downloaded/Validated/BackupCompleted/…)
- [x] Desktop inicia SF.Updater.exe para atualização (IPC `start-updater` em main.js)
- [x] Fluxo completo de atualização funciona (1.0.0 → 1.0.1)
- [x] Dados preservados após atualização
- [x] Falha durante atualização não corrompe instalação (rollback + estados intermediários)
- [x] Fontes do instalador versionadas em `installer/` (copiadas para `dist/` no build)
- [x] Comparaçao VC++ usa `[version]` (nao string)
- [x] SHA-256 do `vc_redist.x64.exe` verificado no install
- [x] `Test-DotnetRuntime` morto removido
- [x] Typo `Removeratalhes` corrigido
- [x] Desinstalador agenda self-delete (nao remove o proprio diretorio em execucao)
