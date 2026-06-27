# Auditoria Técnica do Backend MulletaFlix

Escopo: backend `MulletaFlix-master`, com foco em desempenho, manutenção, bootstrap, persistência, consultas e cobertura de testes.

## Skills Utilizadas

As skills abaixo foram consideradas para esta auditoria e para o plano de execução. Nem todas são aplicadas em igual peso em cada tarefa.

- `codex-fable5`: orientar a leitura por evidência, verificação e fechamento com rastreabilidade.
- `code-review-and-quality`: revisar correção, arquitetura, segurança, legibilidade e performance.
- `code-review-excellence`: estruturar achados com severidade, impacto e recomendação.
- `dotnet-backend`: orientar padrões ASP.NET Core, EF Core, async, caching e background services.
- `dotnet-architect`: avaliar limites de arquitetura, coesão de módulos e risco sistêmico.
- `dotnet-backend-patterns`: validar padrões de API, data access, DI, cache e resiliência.
- `code-simplifier`: reduzir complexidade sem mudar comportamento.
- `backend-development-feature-development`: organizar trabalho em sprints, entregáveis e validação.
- `caveman`: usado apenas para compressão de comunicação interna quando útil.
- `cavecrew`: útil para delegação enxuta em investigação ou revisão pontual.

Skill não aplicada:

- `product-design audit`: não foi aplicada porque este trabalho não é auditoria de fluxo, UI ou experiência visual.

## Leitura Base

Arquivos e sinais usados como base desta auditoria:

- `[Jellyfin.Server.Implementations/Item/BaseItemRepository.cs](../Jellyfin.Server.Implementations/Item/BaseItemRepository.cs)`
- `[Jellyfin.Server.Implementations/Item/BaseItemRepository.Querying.cs](../Jellyfin.Server.Implementations/Item/BaseItemRepository.Querying.cs)`
- `[Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs](../Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs)`
- `[Jellyfin.Server/Helpers/MariaDbProcessManager.cs](../Jellyfin.Server/Helpers/MariaDbProcessManager.cs)`
- `[Jellyfin.Server.Implementations/Users/UserManager.cs](../Jellyfin.Server.Implementations/Users/UserManager.cs)`
- `[tests/Jellyfin.Server.Implementations.Tests/Item/ItemPersistenceServiceTests.cs](../tests/Jellyfin.Server.Implementations.Tests/Item/ItemPersistenceServiceTests.cs)`
- `[tests/Jellyfin.Server.Implementations.Tests/Item/NextUpQueryOptimizationTests.cs](../tests/Jellyfin.Server.Implementations.Tests/Item/NextUpQueryOptimizationTests.cs)`
- `[tests/Jellyfin.Server.Implementations.Tests/Item/BaseItemRepositoryTests.cs](../tests/Jellyfin.Server.Implementations.Tests/Item/BaseItemRepositoryTests.cs)`
- `[tests/Jellyfin.Server.Implementations.Tests/Users/UserManagerTests.cs](../tests/Jellyfin.Server.Implementations.Tests/Users/UserManagerTests.cs)`
- `[tests/Jellyfin.Server.Implementations.Tests/Users/UserManagerNormalizedUsernameTests.cs](../tests/Jellyfin.Server.Implementations.Tests/Users/UserManagerNormalizedUsernameTests.cs)`

## Resumo Executivo

O backend já tem uma base funcional, mas está concentrado em poucos módulos muito grandes e com bastante mistura entre orquestração, persistência e regras de negócio.

Os principais riscos encontrados são:

1. `ItemPersistenceService` concentra escrita, deduplicação, remapeamento relacional e limpeza de órfãos no mesmo fluxo síncrono.
2. `BaseItemRepository` materializa consulta cedo demais em pontos quentes de listagem e "latest/next up".
3. `MediaInfoResolver`, `MetadataService` e os caminhos de `ProviderManager` ainda fazem muita descoberta, leitura e download de metadata em sequência, o que penaliza reconhecimento de mídia e refresh.
4. `MariaDbProcessManager` faz bootstrap bloqueante com polling síncrono e inicialização de esquema dentro do caminho de startup.
5. `UserManager` combina inicialização de esquema, resolução de provedores e operações de usuário numa classe muito extensa.

## Achados Priorizados

### P0

- `ItemPersistenceService` mistura save/delete/update, deduplicação de `UserData`, atualização de `AncestorIds`, `LinkedChildren`, `ItemValues` e limpeza de dependências em um único método grande.
- O método `UpdateOrInsertItemsCore` faz vários `ToList`, `GroupBy`, `Distinct`, `First` e consultas repetidas dentro de loops, o que cria custo algorítmico desnecessário em bibliotecas grandes.
- Há `GetAwaiter().GetResult()` em fluxo de persistência, o que aumenta risco de bloqueio de thread e dificulta escalabilidade sob carga.

### P1

- `BaseItemRepository.Querying.cs` usa `AsEnumerable()` em caminhos de consulta que deveriam permanecer server-side por mais tempo.
- Há materialização repetida em `GetLatestItemList`, `GetLatestMusicAlbums` e `GetRecentlyAddedItemIds`, com potencial de ampliar latência e consumo de memória quando a biblioteca cresce.
- O pipeline de reconhecimento de mídia e metadata faz muito trabalho por item, sem foco explícito em reduzir I/O, batching e reutilização de resultados entre `MediaInfoResolver`, `MetadataService` e providers.
- `UserManager` faz inicialização síncrona via `EnsureSchemaCreatedAsync().GetAwaiter().GetResult()` em métodos públicos que podem ser chamados com frequência.

### P2

- `MariaDbProcessManager` mantém bootstrap de MariaDB embutido no processo principal e usa polling bloqueante para aguardar porta.
- A lógica de inicialização de schemas está acoplada à disponibilidade do servidor e não tem contrato assíncrono claro.
- A classe `UserManager` tem 1358 linhas e concentra responsabilidades demais para manutenção saudável.

## Recomendações Técnicas

1. Separar o pipeline de escrita de itens em serviços menores:
   - resolução de valores
   - relação de ancestors
   - relação de linked children
   - remoção de órfãos
   - persistência final

2. Manter filtros e ordenações no lado do banco pelo maior tempo possível:
   - remover `AsEnumerable()` precoce
   - projetar somente colunas necessárias
   - usar `AsNoTracking()` onde a consulta é leitura pura
   - validar se algum `GroupBy` precisa mesmo sair do SQL

3. Trocar bootstrap bloqueante por inicialização assíncrona e isolada:
   - startup da base não deve travar o host se o DB externo já estiver pronto
   - polling deve virar espera com cancelamento
   - criação de schema deve ser idempotente e testável por unidade

4. Reduzir responsabilidade do `UserManager`:
   - separar resolução de provedor
   - separar schema bootstrap
   - separar fluxo de senha/autenticação
   - criar helpers testáveis para seleção de provider

5. Cobrir os caminhos mais caros com testes focados:
   - duplicidade de `UserData` em delete em lote
   - `AsEnumerable()` e projeções de `NextUp`
   - bootstrap de MariaDB com DB já existente
   - seleção de provider inválido e fallback

## Plano Por Sprint

### Sprint 0 - Baseline e Instrumentação

Objetivo: medir antes de mexer.

Tarefas:

1. Identificar endpoints e jobs mais caros.
2. Medir tempo de query, uso de memória e tempo de startup.
3. Registrar benchmark mínimo para item list, next up, save item e bootstrap.

Skills:

- `codex-fable5`
- `code-review-and-quality`
- `dotnet-architect`
- `dotnet-backend`

Prioridade: P0.

Ganho esperado:

- Não entrega ganho direto de produção.
- Reduz risco de regressão e define linha de base para os sprints seguintes.

### Sprint 1 - Consultas Quentes

Objetivo: reduzir latência e memória em listagens, reconhecimento de mídia e "latest/next up".

Tarefas:

1. Revisar `BaseItemRepository.Querying.cs` para adiar materialização.
2. Reescrever projeções para manter lógica no banco quando possível.
3. Revisar `NextUpService` e pontos de `GroupBy`/`ToList` que podem ser empurrados para consulta mais barata.
4. Revisar `MediaInfoResolver` para reduzir leituras de diretório, `GetFilePaths` e probing desnecessário.
5. Revisar `MetadataService` e `ProviderManager` para diminuir materialização precoce, repetir menos downloads e evitar refreshs redundantes.
6. Adicionar testes para regressão de paginação, ordenação e descoberta de mídia.

Skills:

- `dotnet-backend`
- `dotnet-backend-patterns`
- `dotnet-architect`
- `code-simplifier`
- `code-review-excellence`

Prioridade: P0.

Ganho esperado:

- Latência p95 de listagens: melhora estimada de 15% a 35% em bibliotecas grandes.
- Consumo de memória: redução estimada de 10% a 25% em queries com muitos itens.
- Reconhecimento de mídia: melhora estimada de 10% a 30% em bibliotecas com muitos arquivos externos e streams auxiliares.
- Download de metadata: melhora estimada de 15% a 40% quando há muitos providers ou imagens remotas.

### Sprint 2 - Persistência e Escrita

Objetivo: reduzir custo de save/delete, diminuir risco de lock contention e acelerar persistência de metadata.

Tarefas:

1. Quebrar `ItemPersistenceService.UpdateOrInsertItemsCore` em etapas menores.
2. Trocar buscas repetidas por dicionários e estruturas indexadas.
3. Reduzir chamadas síncronas sobre async.
4. Revisar `DeleteItem` para diminuir `Any/Contains/ToList` repetidos em coleções grandes.
5. Revisar o caminho de `SaveItemAsync`/`UpdateToRepositoryAsync` para reduzir regravações desnecessárias de metadata e imagens.
6. Reforçar cobertura do `ItemPersistenceServiceTests`.

Skills:

- `dotnet-backend`
- `dotnet-backend-patterns`
- `code-simplifier`
- `code-review-and-quality`
- `code-review-excellence`

Prioridade: P0.

Ganho esperado:

- Tempo de save em lotes: melhora estimada de 20% a 40% em cargas com muitos relacionamentos.
- Menos bloqueio de thread e menor chance de travamento sob carga concorrente.
- Refresh de metadata: melhora estimada de 10% a 25% por reduzir regravações e chamadas desnecessárias.

### Sprint 3 - Startup e Bootstrap

Objetivo: melhorar tempo de boot e reduzir acoplamento operacional.

Tarefas:

1. Remover polling bloqueante de `MariaDbProcessManager`.
2. Separar inicialização de processo e criação de schema.
3. Tornar o bootstrap de DB externo e local mais explícito.
4. Revisar `UserManager` para inicialização não bloqueante quando possível.
5. Cobrir boot com DB pré-existente e schema ausente.

Skills:

- `dotnet-architect`
- `dotnet-backend`
- `dotnet-backend-patterns`
- `backend-development-feature-development`
- `code-review-and-quality`

Prioridade: P1.

Ganho esperado:

- Startup: melhora estimada de 10% a 30% no caminho com MariaDB local/embutido.
- Menor risco de bloqueio e falha intermitente no boot.

### Sprint 4 - Manutenibilidade Estrutural

Objetivo: reduzir tamanho das classes e facilitar evolução.

Tarefas:

1. Extrair helpers de `UserManager`.
2. Separar responsabilidades de autenticação, reset de senha e inicialização.
3. Revisar classes grandes de mídia e provider com o mesmo padrão.
4. Padronizar nomes, contratos e retornos para facilitar leitura.
5. Remover lógica morta, comentários obsoletos e duplicação.

Skills:

- `code-simplifier`
- `dotnet-architect`
- `code-review-excellence`
- `code-review-and-quality`
- `cavecrew`

Prioridade: P1.

Ganho esperado:

- Ganho direto de performance: baixo, 0% a 10%.
- Ganho forte em manutenção, revisão e velocidade de entrega futura.

## Ordem Recomendada De Execução

1. Sprint 0
2. Sprint 1
3. Sprint 2
4. Sprint 3
5. Sprint 4

## Testes E Verificação Recomendados

- `dotnet test` focado em `Jellyfin.Server.Implementations.Tests`.
- Casos prioritários:
  - `ItemPersistenceServiceTests`
  - `NextUpQueryOptimizationTests`
  - `BaseItemRepositoryTests`
  - `UserManagerTests`
  - `UserManagerNormalizedUsernameTests`
- Benchmark ou medição local para:
  - listagem paginada
  - latest/next up
  - save/delete em lote
  - startup com MariaDB embutido e com DB já existente

## Risco Residual

Mesmo com as correções acima, o backend ainda vai carregar herança de compatibilidade e paths legados. Isso significa que:

- parte do custo de consulta pode continuar por causa de dados antigos;
- parte da complexidade estrutural vem de compatibilidade com schema e comportamento legado;
- alguns ganhos de performance só aparecem com dados reais e biblioteca grande.

Para evitar autoengano, cada sprint deve terminar com medição antes/depois.
