# Nível hard: implementação e decisões

O nível hard usa `EnterpriseElevatorSystem`. Os coordenadores `ElevatorController`
(easy) e `ElevatorSystem` (medium) mantêm suas APIs e comportamento FIFO. O novo
coordenador reutiliza `Elevator` para movimento, limites e intertravamento das portas.
Isso evita alterar as garantias já verificadas dos níveis anteriores.

## Executar

```powershell
dotnet build ElevatorSystem.sln -m:1
dotnet test ElevatorSystem.sln -m:1
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile -- --hard
dotnet run --project src/ElevatorSystem.Demo --no-launch-profile -- --benchmark
```

`--hard` demonstra solicitações comuns, VIP, carga, manutenção, emergência,
redistribuição e retomada. `--benchmark` compara FIFO e LOOK com 128 solicitações
concorrentes, os mesmos andares e timestamps determinísticos.

Exemplo de uso da biblioteca:

```csharp
var system = new EnterpriseElevatorSystem(EnterpriseFleetFactory.CreateDefault());
var access = new AccessProfile(new[] { 1, 10, 20 }, isVip: true);
var request = new EnterpriseRequest(new Request(1, 20), access);
system.SubmitRequest(request);
await system.ProcessRequestsAsync();
var trip = system.Trips.Single(t => t.Id == request.Trip.Id);
var metrics = system.GetAnalytics();
```

`BalanceLoad()` atribui sem movimentar. `ProcessTick()` permite avançar a simulação
passo a passo para inspecionar embarques e acionar os controles operacionais.

## Organização e responsabilidades

| Arquivo/área | Responsabilidade |
| --- | --- |
| `Domain/EnterpriseModels.cs` | Configuração imutável, tipos, permissões, solicitações e snapshots |
| `Application/EnterpriseElevatorSystem.cs` | Admissão, despacho, ciclo de vida, operação e observações atômicas |
| `Application/Scheduling/IStopSchedulingStrategy.cs` | Contrato de roteamento e implementações LOOK/FIFO |
| `Composition/EnterpriseFleetFactory.cs` | Composição da frota padrão |
| `tests/ElevatorSystem.Tests` | Testes xUnit de todos os níveis |

Configuração usa composição: não há três subclasses duplicando movimento e portas.
A política de roteamento é injetável e recebe uma lista somente leitura. O domínio
não depende de console, xUnit ou infraestrutura. O coordenador mantém decisões
coesas sobre a frota; não foram adicionados serviços externos ou interfaces sem uso.

## Tipos, capacidade e restrições

A frota padrão tem três carros, no prédio de 1 a 20:

| ID | Tipo | Andares atendidos | Capacidade |
| --- | --- | --- | --- |
| 0 | Local | 1 a 20 | 1000 kg |
| 1 | Express | 1, 10, 15, 20 | 1000 kg |
| 2 | Freight | 1 a 20 | 3000 kg |

Outras configurações permitem 3–5 carros, IDs distintos e andares/capacidades
próprios. O express passa fisicamente pelos andares intermediários, mas não abre
portas neles. Não existem baldeações automáticas. Freight atende apenas carga;
local/express atendem passageiros. Cada solicitação tem peso positivo.

O despacho reserva capacidade para todas as viagens atribuídas, inclusive antes
do embarque. É uma política conservadora que impede sobrecarga, mas pode reduzir
ocupação em comparação com um planejamento de capacidade por trecho.

Antes de aceitar, a aplicação valida autorização na origem e no destino e verifica
se existe algum carro fisicamente compatível. Pedido impossível é rejeitado sem
mudar contadores. Se um carro compatível existe mas está indisponível ou sem
capacidade, a viagem fica aguardando. Outras solicitações continuam sendo atendidas.

`AccessProfile` representa permissões fornecidas por um chamador confiável; não é
um sistema de login. `IsVip` não concede andares adicionais. Em uma aplicação com
usuários reais, esse perfil viria da autorização no servidor.

## Viagens, despacho e LOOK

Cada viagem conserva o `Request.Id` e percorre:

```text
Waiting -> Assigned -> Onboard -> Completed
              |
              +-> Waiting (redistribuição antes do embarque)
```

O destino só entra no planejamento depois do embarque. Conclusão ocorre ao abrir
as portas no destino; o processamento também fecha as portas antes de terminar.
Não se removem passageiros por posição FIFO: cada transição identifica a viagem.

O despacho primeiro filtra disponibilidade, tipo, andares e capacidade. Depois
simula a política de rota para estimar o tempo até o novo embarque, incluindo
movimento, embarques anteriores, destinos ativados e operações de portas. Desempates
usam quantidade de viagens e ID. A estimativa não prevê chegadas futuras.

LOOK atende paradas compatíveis na direção atual e inverte ao terminar a demanda.
Chamadas de sentido contrário são atendidas no retorno; quando são a única demanda
à frente, o carro chega à mais distante e inverte. Não vai ao limite do prédio sem
necessidade. A estratégia FIFO oferece uma comparação mantendo viagens em sequência.

VIP recebe vantagem de 20 ticks na ordem de despacho. A chave usa tick de chegada
menos essa vantagem, depois timestamp e sequência. Assim, uma viagem comum que já
esperou mais de 20 ticks precede novos VIPs. Isso evita preterição indefinida por
novas chegadas VIP quando há capacidade compatível e o simulador avança. Não é
garantia de atendimento quando todos os carros compatíveis estão indisponíveis.
Viagens já atribuídas não são interrompidas para dar passagem a VIPs.

## Manutenção, emergência e timeout

`OperationalMode` é independente de `ElevatorState`: modo operacional e portas
precisam coexistir. Por isso, o hard representa manutenção em `Mode`, mantendo
o estado físico em `State`; o enum legado permanece compatível.

- `RequestMaintenance(id)`: Normal -> Draining; devolve viagens não embarcadas à
  espera, termina as embarcadas e entra em Maintenance com portas fechadas.
- `EmergencyStop(id)`: bloqueia os próximos passos de movimento e portas;
  redistribui apenas viagens ainda não embarcadas. Passageiros a bordo permanecem
  no mesmo carro. Uma emergência pode interromper a drenagem para manutenção.
- `ResumeService(id)`: retorno explícito de Maintenance/EmergencyStopped para Normal.
  O modo físico de portas é preservado; portas abertas são fechadas antes de mover.
- `CheckTimeouts()`: usa `TimeProvider.GetTimestamp()` para detectar carros com
  trabalho sem progresso por 30 segundos, configurável, e aciona emergência.

O watchdog é verificado antes de cada tick e também pode ser chamado pelo host.
Não há temporizador oculto: se ninguém chama o simulador nem o watchdog, não existe
detecção autônoma. Uma pausa real prolongada com trabalho atribuído conta como falta
de progresso; para testes e simulações controladas, injete `TimeProvider`.
Um callback de estratégia que bloqueia indefinidamente não pode ser interrompido
por esse watchdog: o contrato da estratégia exige execução rápida e sem I/O.

A simulação representa posições inteiras. Emergência acontece entre passos atômicos,
sem modelar frenagem entre andares ou procedimentos físicos de resgate.

## Concorrência, falhas e limites

Um lock da frota protege seleção + atribuição, filas, estados e métricas. A ordem
de locks é frota -> elevador. Nenhum elevador da frota hard é exposto para mutação:
os consumidores recebem snapshots imutáveis. `ProcessTick()` avança cada carro uma
vez; `ProcessRequestsAsync()` serializa chamadas de processamento com semáforo e
cede execução entre ticks. Submissões e controles podem acontecer entre ticks.

O hard usa ticks coordenados determinísticos; o processamento com workers por carro
do medium continua disponível. O hard não cria um worker por carro, pois isso faria
o tempo simulado depender do escalonador do sistema operacional.

Cancelamento preserva tudo que foi confirmado e libera o semáforo. Política inválida
ou que lança exceção não atribui a solicitação atual; operações anteriores permanecem
confirmadas. Políticas customizadas devem ser puras, rápidas e não chamar o coordenador.

Quando nenhum carro consegue avançar, o processamento retorna, mesmo que existam
viagens bloqueadas. Consulte `Pending` e retome após liberar carros. Submissões feitas
depois do último tick exigem nova chamada de processamento, como nos níveis anteriores.

Limites padrão: 10.000 viagens pendentes e 1.000 registros em cada histórico de
eventos, viagens concluídas e amostras de espera. A fila cheia rejeita novas viagens.
Identificadores duplicados são rejeitados enquanto permanecem no histórico; não há
deduplicação durável depois de expirar o histórico nem persistência após reinício.

## Monitoramento e analytics

`Events` entrega uma janela de eventos estruturados, com tick, tipo, ID de carro e
ID de viagem quando aplicável, além de andar, estado e modo capturados na transição.
A demo os imprime. Não são logs persistentes.

`GetAnalytics()` informa:

- Enviadas, concluídas, pendentes e idade da viagem ainda não atribuída mais antiga.
- Espera média de todas as viagens embarcadas e P95 da janela recente de embarques.
- Tempo médio entre embarque e desembarque e throughput em viagens/tick.
- Andares percorridos e soma de car-ticks em movimento, portas, ociosidade e indisponibilidade.
- Quantidade de atribuições e duração média/máxima da seleção e atribuição em ms reais.

Um tick significa um movimento de um andar ou uma operação de porta por carro.
As médias simuladas são em ticks, não segundos reais. Latência de atribuição usa
`Stopwatch` e mede cada atribuição bem-sucedida dentro do lock; não inclui espera
para adquirir o lock nem tempo na fila. O benchmark mede também a latência de
`SubmitRequest`, incluindo contenção, e o tempo total de processamento.

As somas de car-ticks divididas por `Tick * quantidadeDeCarros` permitem calcular
percentuais de utilização. A memória fica limitada pelas configurações e históricos;
não foi realizado profiling de heap nem ensaio prolongado de produção.

## Testes xUnit

O projeto `ElevatorSystem.Checks` foi substituído por `ElevatorSystem.Tests`.
Não existe mais `Main` chamando verificações manualmente. Métodos `[Fact]` e
`[Theory]` são descobertos por `dotnet test` e pelo Test Explorer.

- `FoundationTests`: validação, snapshots, estratégias, atomicidade e 128 chamadores.
- `EasyLevelTests`: movimento, FIFO, portas, pares, logging e concorrência.
- `MediumLevelTests`: prioridade, distribuição, cancelamento, falhas e workers concorrentes.
- `HardLevelTests`: tipos/capacidade, acesso VIP, LOOK, ciclo de vida, manutenção,
  emergência, timeout, envelhecimento, limites, métricas, snapshots e concorrência.

Os testes usam `Assert` do xUnit. O paralelismo entre testes é desabilitado porque
um teste legado captura `Console.Out`, que é global. Os testes de concorrência
continuam criando suas próprias operações simultâneas. A regra xUnit1031 foi
suprimida para os testes herdados que usam gates e threads com timeouts explícitos.
O timeout operacional é testado com relógio falso, sem sleeps reais.

Medições de desempenho são feitas pela demo, sem assert de tempo instável no xUnit.
Os testes verificam também uma carga conhecida na qual LOOK percorre menos andares
que FIFO; isso não afirma superioridade em todas as distribuições de demanda.

### Resultado observado em 17/09/2026

Build Debug local, 128 solicitações, todas concluídas, nenhuma pendente:

| Política | Espera média/P95 (ticks) | Andares percorridos | Atribuição média/máxima (ms) | Submissão máxima (ms) |
| --- | --- | --- | --- | --- |
| FIFO | 1206,62 / 2418 | 2561 | 0,074 / 7,958 | 1,343 |
| LOOK | 327,92 / 706 | 311 | 0,029 / 0,876 | 0,143 |

Execução inicial sem aquecimento; números dependem de JIT, máquina e carga.
As atribuições medidas ficaram abaixo de 100 ms nesse cenário. Isso não comprova
um SLA de ponta a ponta sob todas as cargas. Validação: 40 testes xUnit aprovados.
