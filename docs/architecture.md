# Arquitetura

    ADMIN WPF elevado -- TCP/mTLS + prova de senha --> agente SYSTEM na sessão física
                                                                |
    serviço SYSTEM / Session 0 -- token de sessão + Job Object --+
                                                                |
    interface local elevada -- named pipe autenticado -----------+

O serviço não captura na Session 0. Ele duplica seu token SYSTEM, seleciona a sessão física ativa e cria o agente em winsta0\default. Uma thread sem janelas acompanha o desktop de entrada com OpenInputDesktop/SetThreadDesktop e executa GDI/SendInput. Não são criadas janelas próprias como SYSTEM.

Job Object encerra o agente se o supervisor morrer. Troca de sessão física fecha a conexão e relança o agente. Reconexão manual. Encerramento normal pede shutdown, libera entradas e fecha canais; término forçado é fallback.

A saída explícita pela bandeja ou pelo ADMIN primeiro encerra sessões/consultas, pede parada ao SCM e espera até 20 s pelos processos do serviço. Se travarem, desativa temporariamente a recuperação e termina somente caminhos conhecidos desta instalação, conferindo o caminho e encerrando pelo mesmo handle para evitar reutilização de PID. Instalação/atualização/desinstalação também encerram interface, supervisor e agente antes de substituir arquivos. Tailscale e executáveis de outras pastas são preservados. [Operação e verificações](processos-atualizacao.md).

IPC usa pipe local com ACL de SYSTEM/administradores, autenticação do processo servidor e rejeição de clientes de rede. Comandos enumerados e payload de até 64 KiB.

Configuração compartilhada usa trava de arquivo entre processos e substituição atômica. O worker monitora política e rede a cada dois segundos. Revogação, alteração de senha e desabilitação cortam sessões existentes.

Listener usa apenas o IP escolhido; nunca wildcard. Firewall restringe programa, porta, origem e perfil. Rede local requer endereço IPv4 privado ativo.

Captura GDI com blocos alterados de 128 pixels, comprimidos sem perda com Brotli. Cada lote de até 192 KiB aguarda confirmação após a apresentação; a compressão não ocupa a thread de entrada. FPS é limitado e telas idênticas não geram imagem. JPEG permanece para versões antigas. O viewer oferece ajuste à janela ou pixels a 100%. Movimentos do mouse são coalescidos; teclas/cliques conservam a ordem. Resolução da imagem orienta coordenadas. Perda de foco libera entradas enviadas pelo ADMIN.

Fontes: [serviços interativos](https://learn.microsoft.com/windows/win32/services/interactive-services), [CreateProcessAsUser](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw), [SetThreadDesktop](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setthreaddesktop).
Na 1.5.0, acesso simultâneo limita o host a dois ADMINs. Cada conexão possui captura/encoder/fila/ACK próprios; chamadas GDI continuam na thread de desktop e a compressão acontece fora dela. Um coordenador sobre o único SendInput transfere a posse por atividade e libera a entrada do dono anterior. Observadores não adquirem entrada. Saída/revogação atua por sessão; parada, senha/rede e encerramento local atuam sobre todas.

Uma thread separada acompanha Default/Winlogon e observa teclado/mouse físicos com hooks de baixo nível, ignorando flags de injeção. Callbacks não aguardam o coordenador nem bloqueiam hardware; apenas atualizam estado transitório e sinalizam atividade. Indisponibilidade do observador suspende entrada remota coordenada até recuperação. Desabilitar Acesso simultâneo nas configurações restaura o caminho anterior com uma sessão. [Regra e validação](acesso-simultaneo.md).
