# RELAT�RIO DA FASE 06 � �REA PRINCIPAL AP�S LOGIN

## Objetivo executado

Criar a estrutura principal da aplica��o ap�s autentica��o.

## Arquivos criados

- Nenhum arquivo totalmente novo (modifica��es em arquivos existentes).

## Arquivos modificados

- src/desktop/main.js (atualizado para suporte a inst�ncia �nica e gerenciamento de janela)
- src/frontend/sf-tecnologias-web/src/App.tsx (atualizado para exibir informa��es do usu�rio, barra superior e logout)
- src/backend/SF.Tecnologias.Application/Services/AuthService.cs (mantido, mas verificado que est� corrigido ap�s fase 5)

## Depend�ncias adicionadas

- Nenhuma nova depend�ncia al�m das j� existentes.

## Decis�es t�cnicas

- **Electron**: Implementado bloqueio de inst�ncia �nica usando `app.requestSingleInstanceLock()` para evitar janelas duplicadas. Quando uma segunda inst�ncia � iniciada, ela foca a janela existente.
- **Electron**: Mantido o comportamento padr�o de encerramento (sair quando todas as janelas fecharem, exceto no macOS).
- **Frontend**: O estado da aplica��o agora inclui informa��es do usu�rio (nome, ID da empresa, nome da empresa) obtidas a partir do resposta de login. A interface exibe uma barra superior com o nome da empresa e do usu�rio, al�m de um bot�o de sair.
- **Frontend**: A �rea de conte�do permanece inicialmente vazia, conforme especifica��o, com espa�o para futuros m�dulos.
- **Frontend**: A prote��o de rota � feita por renderiza��o condicional: se n�o houver token, mostra a tela de login; caso contr�rio, mostra a �rea autenticada.
- **Seguran�a**: O token JWT continua armazenado apenas no processo principal do Electron (n�o exposto ao frontend), acess�vel apenas atrav�s da API segura do pr�-carregamento.

## Regras de neg�cio implementadas

- Login com EMPRESA_ID e senha (conforme fase 5).
- Hash seguro de senhas (BCrypt).
- JWT pr�prio com expira��o configur�vel.
- Autoriza��o por permiss�es (via claims no token).
- Gerenciamento seguro de sess�o (token armazenado apenas no processo principal).
- Logout (remo��o do token e limpeza das informa��es do usu�rio).

## Build executado e resultado

- Backend: `dotnet build` ? sucesso (2 avisos).
- Backend testes: `dotnet test` ? 1 teste passando, 0 falhas.
- Frontend: `npm run build` ? sucesso (16 m�dulos transformados).
- Electron: Verifica��o de disponibilidade do Electron via `npx electron --version` ? sucesso (v44.4.3).

## Testes executados e resultado

- Backend: dotnet test ? 1 teste passando, 0 falhas.
- Frontend: Nenhum teste configurado (valida��o manual de inicializa��o e comunica��o).
- Desktop: Nenhum teste configurado (valida��o manual de inicializa��o de inst�ncia �nica e comportamento de janela).

## Falhas encontradas

- Nenhuma cr�tica.
- Aviso de depend�ncia vulner�vel (System.IdentityModel.Tokens.Jwt) mantido conforme fase anterior.

## Pend�ncias

- Implementar mecanismo de decodifica��o do token ou endpoint de usu�rio para recuperar informa��es do usu�rio ap�s atualiza��o da p�gina (atualmente, as informa��es do usu�rio s�o perdidas ao recarregar a aplica��o).
- Adicionar tratamento de reconex�o e timeout mais sofisticado nas requisi��es HTTP.
- Melhorar armazenamento de token (por exemplo, usar armazenamento seguro ou refresh token) conforme requisitos de seguran�a.
- Adicionar testes integrados para o fluxo de login, autentica��o e prote��o de rotas.
- Implementar estilo visual conforme identidade visual SF Tecnologias (cores, logo gen�rico) na tela de login e na �rea autenticada.

## Instru��es para valida��o local

1. Construir o backend: `dotnet build`
2. Testar o backend: `dotnet test`
3. Construir o frontend: `npm run build --prefix src/frontend/sf-tecnologias-web`
4. Iniciar a API de backend: `dotnet run --project src/backend/SF.Tecnologias.Api/SF.Tecnologias.Api.csproj`
5. Iniciar o Electron em modo de produ��o: `set NODE_ENV=production && electron .` (ou usar `npm run prod` dentro de src/desktop)
6. Preencher os campos Empresa ID e Senha (use um usu�rio v�lido cadastrado no banco; senha deve estar hashada com BCrypt).
7. Clicar em Entrar e observar redirecionamento para �rea autenticada com exibi��o do nome da empresa e do usu�rio.
8. Clicar em Sair e observar retorno � tela de login e limpeza do token.
9. Testar inst�ncia �nica: tentar iniciar o Electron duas vezes e verificar que apenas uma janela � exibida e a segunda instancia foca a primeira.
10. Fechar a janela e verificar que o aplicativo � encerrado corretamente (exceto no macOS, onde o aplicativo permanece ativo at� sair explicitamente).

## Pr�xima fase recomendada

Fase 07 � Sistema de M�dulos e Janelas Internas (criar infraestrutura reutiliz�vel de m�dulos e janelas internas, com gerenciador de janelas, registro de m�dulos, permiss�es e navega��o preparada para m�dulos comerciais futuros).
