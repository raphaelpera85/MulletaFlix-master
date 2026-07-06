# Auditoria Tecnica do Backend MulletaFlix

Escopo: backend `MulletaFlix-master`, com foco em performance, manutencao, bootstrap, persistencia, consultas e cobertura de testes.

## Padrao De Codificacao

- Toda documentacao e todo codigo devem ser mantidos em `utf-8`.
- O repositório ja reforca isso em [`.editorconfig`](D:/Users/Raphael/Documents/Projetos/mulletaflix/MulletaFlix-master/.editorconfig) com `charset = utf-8`.
- Novos arquivos de texto, markdown e codigo devem seguir o mesmo padrao.

## Handoff Entre IAs

Se esta conversa ficar sem tokens e for necessario continuar em outra IA, a proxima instancia deve receber este contexto minimo:

- O projeto principal e o backend do `MulletaFlix-master`.
- A sprint atual e `Sprint 5 - Home, detalhe e refresh sob demanda`.
- O que ja foi concluido inclui `UserManager`, `ItemPersistenceService`, `BaseItemRepository.Querying`, `MetadataService`, `ProviderManager`, `MediaInfoResolver`, `FFProbeVideoInfo` e partes de `ProbeProvider`.
- O que ja foi corrigido nesta rodada inclui o erro de `requestIdleCallback` no `imageLoader`, que estava bloqueando a renderizacao da home e da pagina de detalhe.
- O que ainda falta nesta rodada inclui medir o impacto do refresh sob demanda em background, validar no navegador o carregamento de metadados/imagens, revisar a pagina de dispositivos do dashboard sob carga e continuar os cortes de performance em reconhecimento de midia e download de metadata.
- Toda a documentacao e todo o codigo devem continuar em `utf-8`.
- Os testes de providers já foram executados com sucesso e devem ser mantidos como referencia de regressao.
- A cada tarefa concluida, a documentacao deve ser atualizada com:
  - a tarefa realizada;
  - as proximas tarefas da sprint;
  - o status atual da sprint;
  - qualquer risco ou bloqueio novo.
- Esse update de documentacao deve acontecer antes de trocar de IA, para que outra instancia possa continuar sem perder contexto.
- Se houver troca de IA de ida e volta, a documentacao deve servir como fonte unica do estado atual do trabalho.
- Sempre marcar o que foi concluido com `[x]` e o que falta com `[ ]`, para que outra IA saiba exatamente onde continuar.

## Sprint Atual

- Sprint atual: `Sprint 5 - Home, detalhe e refresh sob demanda`
- Status da sprint atual: `em andamento`
- Sprints concluídas: `Sprint 0 - Baseline e instrumentacao`, `Sprint 1 - Consultas quentes e midia`, `Sprint 2 - Persistencia e escrita`, `Sprint 3 - Startup e bootstrap` e `Sprint 4 - Manutenibilidade estrutural`
- Sprints pendentes: `Sprint 5 - Home, detalhe e refresh sob demanda`

## Status Atual

- [x] Auditoria tecnica inicial concluida.
- [x] Otimizacoes de `UserManager`, `ItemPersistenceService`, `BaseItemRepository.Querying`, `MetadataService`, `ProviderManager`, `MediaInfoResolver` e `FFProbeVideoInfo` aplicadas.
- [x] Suíte de testes de providers validada com sucesso.
- [x] Sprint 1 concluída com sucesso (incluindo fechamento de `ProbeProvider.HasChanged` e cortes de performance).
- [x] Sprint 2 concluída com sucesso (refatorações de batching, remoção de queries N+1, otimização de `DeleteItem` e `SaveImagesAsync` no `ItemPersistenceService`).
- [x] Sprint 3 concluída com sucesso (portabilidade e bootstrap do MariaDB assíncrono, remoção de polling de porta bloqueante e fast-path de schema bypass no `UserManager`).
- [x] Sprint 4 concluída com sucesso (refatoração e decomposição de `UserManager.cs` em `UserAuthenticationService.cs` e `PasswordResetService.cs`).
- [x] Auditoria de segurança completa (skill `find-bugs`) executada com sucesso.
  - Resultado: 1 vulnerabilidade corrigida (injeção de parâmetros URL no `OpenLibraryProvider.cs`).
  - Correção: aplicação de `Uri.EscapeDataString` nos parâmetros ISBN e OLID interpolados nas URLs de consulta à API externa do OpenLibrary.
  - Build verde (0 erros) e testes unitários/providers aprovados (4/4 OpenLibrary, 560/560 Implementations).
  - Testes de integração com falhas pré-existentes (401 Unauthorized no `AuthHelper`) não relacionadas à alteração.

- [x] Corrigido o fluxo de detalhe para revalidar metadados e imagens sob demanda antes de montar o DTO em `GetItem`.
- [x] Criado `OnDemandMetadataRefreshPolicy` para centralizar a decisao de refresh por completude e recencia.
- [x] Ajustado `GetLatestMedia` para tentar completar itens incompletos ou desatualizados antes de montar os cards da home.
- [x] Teste unitario da politica de refresh aprovado com 3 casos de regressao.
- [x] Revisao do fluxo de frontend da home e da pagina de detalhe feita; nao apareceu bug obvio de fetch, entao o ajuste principal ficou no backend.
- [x] Corrigido o `GetLatestTvShowItems` para agrupar por `SeriesId`, nao por `SeriesName`, e adicionado indice composto `Type, SeriesId, DateCreated`.
- [x] Adicionado indice em `UserData` para o `NextUp` (`UserId, Played, LastPlayedDate`).
- [x] Evitado leitura de arquivo para `PrimaryImageAspectRatio` quando `ItemImageInfo.Width/Height` ja estao persistidos.
- [ ] Medir o impacto real de latencia do refresh sob demanda na home e no detalhe.
- [ ] Validar no navegador se as artes, overview e metadados reaparecem nas midias recentemente adicionadas.

## Todo Prioritario Por Ganho

1. P0 - Medir a home real com banco grande:
   - comparar `LatestMedia` de filmes e series com `EXPLAIN ANALYZE`;
   - confirmar se o custo saiu de SQL e ficou em DTO/imagem.
   - ganho esperado: muito alto se ainda existir scan ou sort caro.
2. P1 - Fechar `NextUp`:
   - validar o impacto do novo indice em `UserData`;
   - revisar se `GetNextUpSeriesKeys` ainda materializa mais do que precisa.
   - ganho esperado: alto em bibliotecas com muitas series.
3. P2 - Cortar custo de DTO/imagem:
   - revisar `DtoService` e `UpdateImagesAsync` para evitar trabalho repetido em items recentes;
   - medir se o gargalo restante esta em blurhash, dimensoes ou carga de imagem.
   - ganho esperado: medio.
4. P3 - Refino de home no cliente:
   - manter as imagens fora da viewport em lazy load;
   - evitar refetch desnecessario em navegacao entre paginas.
   - ganho esperado: baixo a medio, mas barato de manter.

## Skills utilizadas nesta auditoria

Estas skills foram selecionadas porque cobrem revisao tecnica, arquitetura, simplificacao e execucao orientada a sprint.

- `codex-fable5`: leitura orientada a evidencias, validacao e fechamento rastreavel.
- `code-review-and-quality`: revisao de corretude, arquitetura, seguranca, legibilidade e performance.
- `code-review-excellence`: estruturar achados por severidade, impacto e recomendacao.
- `dotnet-backend`: orientar ASP.NET Core, EF Core, async, cache e background services.
- `dotnet-architect`: avaliar limites de arquitetura, coesao de modulos e risco sistico.
- `dotnet-backend-patterns`: validar padroes de API, data access, DI, cache e resiliencia.
- `code-simplifier`: reduzir complexidade sem mudar comportamento.
- `backend-development-feature-development`: organizar trabalho em sprints, entregaveis e validacao.
- `caveman`: sintetizar e manter foco em execucao pragmatica.
- `cavecrew`: delegacao curta e objetiva para investigacao pontual.

## Entregas ja validadas

As alteracoes abaixo ja foram aplicadas e validadas com `dotnet test` no backend:

- [x] `Jellyfin.Server.Implementations/Users/UserManager.cs`
  - lock por username para evitar serializacao indevida entre logins diferentes;
  - lock por user id quando o usuario ja existe.
- [x] `Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs`
  - uso de `Dictionary` e `HashSet` para reduzir buscas repetidas;
  - menor custo em atualizacao de itens com muitos relacionamentos.
- [x] `Jellyfin.Server.Implementations/Item/BaseItemRepository.Querying.cs`
  - hot path de `latest TV shows` reduzido para materializar menos dados;
  - ajuste para evitar falha de compatibilidade com EF Core no filtro recente.
- [x] `MediaBrowser.Providers/Manager/MetadataService.cs`
  - menos enumeracoes repetidas em selecao de providers.
- [x] `MediaBrowser.Providers/Manager/ProviderManager.cs`
  - materializacao e particionamento simplificados no caminho quente.
- [x] `MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs`
  - parse e filtragem dos sidecars em um unico passe.
- [x] `MediaBrowser.Providers/MediaInfo/FFProbeVideoInfo.cs`
  - cache de sidecars reaproveitado e probes de DVD executados em paralelo.
- [x] `MediaBrowser.Providers/MediaInfo/ProbeProvider.cs`
  - comparacao de sidecars reduzida com helper mais barato.
- [x] Testes de regressao adicionados e aprovados para esses caminhos.

## Achados priorizados

### P0

1. `ItemPersistenceService` concentra save, delete, deduplicacao, relacoes e limpeza em um fluxo grande.
2. `UpdateOrInsertItemsCore` ainda precisa manter o padrao de lookup O(1) em todas as estruturas internas.
3. O pipeline de reconhecimento de midia e download de metadata ainda faz trabalho demais por item e precisa de mais batching, cache e menos I/O.
4. `BaseItemRepository` ainda possui trechos com materializacao antecipada em consultas quentes.

### P1

1. `MariaDbProcessManager` faz bootstrap com polling bloqueante.
2. `UserManager` ainda concentra responsabilidades demais para manutencao sustentavel.
3. Os caminhos de refresh e provider ainda podem evitar downloads repetidos e refreshs redundantes.

### P2

1. Classes grandes precisam de extracao gradual de helpers.
2. Rotinas auxiliares de consulta e bootstrap precisam de contratos mais claros.

## Plano tecnico por sprint

### Sprint 0 - Baseline e instrumentacao

Objetivo: medir antes de otimizar.

Tarefas:

- [x] Identificar endpoints e jobs mais caros.
- [x] Medir tempo de query, memoria e startup.
- [x] Registrar baseline para listagem, next up, save item e bootstrap.

Concluido:

- [x] Sprint 0 fechado como baseline documental e de medicao.

Pendente:

- [ ] Repetir as mediciones apos as proximas sprints para comparar ganhos reais.

Skills:

- `codex-fable5`
- `code-review-and-quality`
- `code-review-excellence`
- `dotnet-architect`
- `dotnet-backend`

Prioridade: P0.

Ganho esperado:

- Nao entrega ganho direto de producao.
- Reduz risco de regressao e define linha de base.

### Sprint 1 - Consultas quentes e midia

Objetivo: reduzir latencia e memoria em listagens, reconhecimento de midia e metadata.

Tarefas:

- [x] Revisar `BaseItemRepository.Querying.cs` para adiar materializacao.
- [x] Manter filtros e ordenacao no banco pelo maior tempo possivel.
- [x] Revisar `MediaInfoResolver` para reduzir probes e leituras de caminho.
- [x] Revisar `MetadataService` e `ProviderManager` para diminuir downloads repetidos.
- [x] Adicionar testes para regressao de paginacao, ordenacao e descoberta de midia.
- [x] Reduzir tempo de download de metadata com cache por item e deduplicacao de requests.
- [x] Reduzir o custo de reconhecimento de midia com menos probes redundantes.

Concluido:

- [x] `MediaInfoResolver`, `MetadataService`, `ProviderManager`, `FFProbeVideoInfo` e `ProbeProvider` receberam o primeiro corte de performance.
- [x] Testes focados de providers fecharam em verde.
- [x] `ProbeProvider.HasChanged` recebeu o corte seguro e foi validado com teste dedicado.

Pendente:

- [x] Reduzir ainda mais o scan de sidecars em refresh de video e audio (resolvido e consolidado com o corte de performance principal da Sprint 1).

Proxima tarefa:

- [x] Iniciar a Sprint 2 - Persistencia e escrita (concluída!).

Skills:

- `dotnet-backend`
- `dotnet-backend-patterns`
- `dotnet-architect`
- `code-simplifier`
- `code-review-excellence`
- `backend-development-feature-development`

Prioridade: P0.

Ganho esperado:

- Latencia p95 de listagens: melhora estimada de 15% a 35%.
- Consumo de memoria: reducao estimada de 10% a 25%.
- Reconhecimento de midia: melhora estimada de 10% a 30%.
- Download de metadata: melhora estimada de 15% a 40%.

### Sprint 2 - Persistencia e escrita

Objetivo: reduzir custo de save/delete e melhorar persistencia em lote.

Tarefas:

- [x] Quebrar `UpdateOrInsertItemsCore` em etapas menores.
- [x] Trocar buscas repetidas por dicionarios e estruturas indexadas.
- [x] Reduzir chamadas sincrono-sobre-async.
- [x] Revisar `DeleteItem` para diminuir `Any`, `Contains` e `ToList` repetidos.
- [x] Reduzir regravacao desnecessaria de metadata e imagens.
- [x] Reforcar cobertura de `ItemPersistenceServiceTests`.

Concluido:

- [x] Refatorações e batching do `ItemPersistenceService` aplicados e validados com testes (remoção de queries N+1, otimização de `DeleteItem` e `UpdateOrInsertItemsCore`, e conversão de `.SaveChangesAsync().GetAwaiter().GetResult()` em `SaveChanges()`).
- [x] Implementação de verificação de alterações para Providers, Imagens (em `SaveImagesAsync` e `SaveBaseItemEntities`) e LockedFields, eliminando regravações redundantes no banco de dados.

Pendente:

- [ ] Nenhuma (Sprint 2 totalmente concluída).

Skills:

- `dotnet-backend`
- `dotnet-backend-patterns`
- `code-simplifier`
- `code-review-and-quality`
- `code-review-excellence`

Prioridade: P0.

Ganho esperado:

- Tempo de save em lotes: melhora estimada de 20% a 40%.
- Menos lock contention e menor chance de travamento sob carga concorrente.
- Refresh de metadata: melhora estimada de 10% a 25%.

### Sprint 3 - Startup e bootstrap

Objetivo: melhorar boot e reduzir acoplamento operacional.

Tarefas:

- [x] Remover polling bloqueante de `MariaDbProcessManager`.
- [x] Separar inicializacao de processo e criacao de schema.
- [x] Tornar bootstrap de DB externo e local mais explicito.
- [x] Revisar `UserManager` para nao bloquear caminhos frequentes.
- [x] Cobrir boot com DB pre-existente e schema ausente.

Concluido:

- [x] Conversão da inicialização do MariaDB portátil para fluxos totalmente assíncronos (`StartMariaDbAsync`, `InitializeDatabaseSchemasAsync`, `WaitForPortAsync`) e await real.
- [x] Otimização da concorrência de logins de usuários inexistentes via hashing MD5 de username para a chave de lock, evitando serialização de requests.
- [x] Implementação do fast-path bypass de validação de schemas em consultas síncronas (`GetUsers`, `GetUsersIds`, `GetUserById`, `GetUserByName`) no `UserManager`.
- [x] Validação completa de toda a suíte de testes (560/560 testes aprovados, incluindo os testes de concorrência de autenticação que anteriormente expiravam).

Pendente:

- [ ] Nenhuma (Sprint 3 totalmente concluída).

Skills:

- `dotnet-architect`
- `dotnet-backend`
- `dotnet-backend-patterns`
- `backend-development-feature-development`
- `code-review-and-quality`
- `code-review-excellence`

Prioridade: P1.

Ganho esperado:

- Startup: melhora estimada de 10% a 30%.
- Menor risco de falha intermitente no boot.

### Sprint 4 - Manutenibilidade estrutural

Objetivo: reduzir tamanho de classes e facilitar evolucao.

- [x] Extrair helpers de `UserManager`.
- [x] Separar autenticacao, reset de senha e inicializacao.
- [ ] Revisar classes grandes de midia e provider.
- [x] Padronizar nomes, contratos e retornos.
- [x] Remover logica morta, comentarios obsoletos e duplicacao.

Concluido:

- [x] Extração de `UserAuthenticationService` contendo a lógica de validação de licenças, login local/provedores e restrição de acesso.
- [x] Extração de `PasswordResetService` contendo a lógica de esquecimento de senha e verificação de PIN de redefinição.
- [x] Refatoração de `UserManager.cs` delegando responsabilidades a esses novos serviços internos, reduzindo o arquivo original.
- [x] Compilação e testes validados com sucesso (560/560 testes passando).

Pendente:

- [ ] Revisar classes grandes de midia e provider (planejado para sprints futuras de manutenção de código).

Skills:

- `code-simplifier`
- `dotnet-architect`
- `code-review-excellence`
- `code-review-and-quality`
- `cavecrew`
- `caveman`

Prioridade: P2.

Ganho esperado:

- Ganho direto de performance: baixo, 0% a 10%.
- Ganho forte em manutencao e velocidade de entrega futura.

### Sprint 5 - Home, detalhe e refresh sob demanda

Objetivo: corrigir a exibicao de midias recentes, metadados e imagens sem perder o controle de performance.

Tarefas:

- [x] Auditar o fluxo da home e da pagina de detalhe no backend e no frontend.
- [x] Centralizar a decisao de refresh sob demanda em `OnDemandMetadataRefreshPolicy`.
- [x] Revalidar metadados e imagens no `GetItem` antes de montar o DTO.
- [x] Revalidar itens incompletos ou desatualizados no `GetLatestMedia`.
- [x] Criar teste unitario da politica de refresh.
- [x] Enfileirar o refresh sob demanda em background com `MulletaFlixJobQueue` para remover custo da request.
- [x] Reduzir o custo do `GetLatestTvShowItems` trocando o carregamento final de entidades para `AsSplitQuery` por ids materializados.

Concluido:

- [x] O backend agora tenta completar itens sem overview ou imagem primaria antes de responder o detalhe.
- [x] O fluxo de cards recentes tambem ganhou refresh sob demanda para melhorar a chance de mostrar arte e metadados atualizados.
- [x] A validacao do frontend nao mostrou bug obvio no fetch; o problema principal ficou concentrado no backend.
- [x] O refresh sob demanda saiu da request path e passou a ser processado em background com deduplicacao curta por item.
- [x] Corrigido o bug de `requestIdleCallback` em `imageLoader`, que interrompia `lazyChildren()` e impedia home e detalhe de terminar de renderizar.
- [x] Blindado o `reload()` da pagina de detalhe com `finally` para liberar o loading mesmo se alguma etapa falhar.
- [x] Otimizado o carrossel da home para pedir imagens menos pesadas em listas `overflow`, reduzindo o custo de download e decode das thumbs.
- [x] Corrigido o carregamento da pagina de dispositivos do dashboard para nao bloquear a renderizacao em `useUsersDetails`.
- [x] Otimizado o caminho de series recentemente adicionadas no backend (`GetLatestTvShowItems`) para evitar `SingleQuery` com multiplas colecoes e reduzir a latencia da home.
- [x] Ajustado o frontend da home para `tvshows` pedir `Backdrop` e voltar a priorizar `Thumb`, reduzindo fallback lento de imagem nas series recentemente adicionadas.

Proximas tarefas:

- [ ] Validar no servidor instalado se a secao de series recentemente adicionadas voltou ao mesmo patamar de filmes.
- [ ] Medir a diferenca real de tempo da home com e sem varredura/refresh de metadata em andamento.
- [x] O backend de devices passou a tolerar referencia de usuario ausente sem derrubar a lista inteira.
- [x] Teste de regressao `DeviceManagerTests` aprovado.
- [x] Build de producao do frontend validado com sucesso.
- [x] Corrigido o carregamento da pagina `Atividade` do dashboard para nao bloquear a tabela em `useUsersDetails`.
- [x] Build de producao do frontend validado novamente apos o ajuste de `Atividade`.
- [x] Corrigida a pagina `NFO` do dashboard para nao depender de usuarios como bloqueio de carregamento.
- [x] Corrigida a persistencia de `BaseItemProvider`, `LockedFields`, `Images` e `TrailerTypes` para nao anexar o grafo completo com entidades duplicadas durante refresh de metadados.
- [x] Teste de regressao `ItemPersistenceServiceTests` aprovado.
- [x] A gravacao de `ItemValues` passou a usar `INSERT IGNORE` para eliminar conflitos recorrentes de indice unico durante refresh concorrente.
- [x] Validacao da frente `ItemPersistenceService` aprovada em `Jellyfin.Server.Implementations.Tests`.

Pendente:

- [ ] Medir o impacto de tempo de resposta da home e do detalhe com o refresh novo.
- [ ] Validar a experiencia final no navegador com midias novas, metadados e imagens.
- [ ] Medir o impacto do ajuste da pagina de dispositivos apos remover a dependencia de usuarios.
- [ ] Medir o impacto do ajuste da pagina de `Atividade` apos remover a dependencia de usuarios.
- [ ] Validar se a pagina `NFO` segue funcional mesmo quando a carga de usuarios falha.
- [ ] Confirmar nos logs que o refresh de metadados nao volta a acusar `BaseItemProvider` duplicado nem repetir erro ao persistir `TrailerTypes`.
- [ ] Confirmar em stage que o erro `Duplicate entry '3-Sky Atlantic' for key 'IX_ItemValues_Type_Value'` nao volta a aparecer sob carga.
- [ ] Medir o impacto da troca para `INSERT IGNORE` na taxa de sucesso do refresh concorrente.
- [ ] Se a home continuar vazia, instrumentar `itemsContainer.refreshItems()` para registrar falhas de query e diferenciar erro de API de lista realmente vazia.
- [ ] Fechar a proxima sprint apenas depois de confirmar no navegador que `Minha midia`, `recentemente adicionadas` e a tela de detalhe carregam sem erro de console.

Skills:

- `dotnet-backend`
- `dotnet-architect`
- `dotnet-backend-patterns`
- `code-review-and-quality`
- `code-review-excellence`
- `backend-development-feature-development`
- `caveman`
- `cavecrew`

Prioridade: P0.

Ganho esperado:

- Melhor chance de carregar arte, overview e metadados nas primeiras respostas.
- Reducao de chamados de suporte por item sem imagem ou sem metadata na tela.
- Impacto de performance agora tende a ser menor na request e maior no processamento em segundo plano.
- A fila existente foi reutilizada para evitar duplicar infraestrutura de background.
- A pagina de dispositivos deve aparecer mais cedo no dashboard e continuar funcional mesmo com usuarios antigos removidos do banco.

## Ordem recomendada

1. Sprint 0
2. Sprint 1
3. Sprint 2
4. Sprint 3
5. Sprint 4
6. Sprint 5

## Prioridade consolidada

1. `P0`: consultas quentes, reconhecimento de midia, download de metadata, refresh sob demanda da home/detalhe e persistencia em lote.
2. `P1`: startup, bootstrap e reducao de bloqueios.
3. `P2`: refatoracao estrutural e simplificacao de classes grandes.

## Validacao recomendada

- `dotnet test` focado em `Jellyfin.Server.Implementations.Tests`.
- Casos prioritarios:
  - `ItemPersistenceServiceTests`
  - `NextUpQueryOptimizationTests`
  - `BaseItemRepositoryTests`
  - `BaseItemRepositoryLatestTvShowTests`
  - `UserManagerTests`
  - `UserManagerNormalizedUsernameTests`
  - `UserManagerAuthenticationLockTests`
- Benchmark local para:
  - listagem paginada
  - latest/next up
  - save/delete em lote
  - startup com MariaDB embutido e com DB ja existente
  - reconhecimento de midia
  - download de metadata

## Risco residual

Mesmo com as correcoes aplicadas, parte do custo pode continuar por causa de dados legados, schema historico e compatibilidade com comportamento antigo.

Para evitar falso ganho, cada sprint deve fechar com medicao antes/depois.
