# Status verificável — 0.1.0-dev

## IMPLEMENTADO

- inventário, seleção sincronizada, detalhes e mapa proporcional;
- fila e operações allow-listed de disco/partição/volume;
- `SafetyService`, confirmação reforçada, revalidação e logs;
- branding, About, publish x64, instalador, CI e integração VHDX opt-in.

## TESTADO

- build Debug e suíte xUnit;
- abertura real sem elevação e inventário do Windows registrado em log;
- build/publish Release e instalador: registrar no relatório final da branch.

## VALIDADO

- regras puras de segurança, fila, parsing, classificação e identidade por testes automatizados;
- inventário somente leitura em Windows local.

## PENDENTE

- integração destrutiva VHDX em sessão administrativa com Hyper-V;
- instalação/desinstalação silenciosa após disponibilidade do Inno Setup;
- matriz física Windows 10/11, DPI e USB descartável;
- clonagem, recuperação avançada e telemetria SMART proprietária.

Nenhuma função pendente é apresentada na interface como suporte disponível.
