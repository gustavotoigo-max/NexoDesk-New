# Arquitetura

    ADMIN WPF elevado -- TCP/mTLS + prova de senha --> agente SYSTEM na sessão física
                                                                |
    serviço SYSTEM / Session 0 -- token de sessão + Job Object --+
                                                                |
    interface local elevada -- named pipe autenticado -----------+

O serviço não captura na Session 0. Ele duplica seu token SYSTEM, seleciona a sessão física ativa e cria o agente em winsta0\default. Uma thread sem janelas acompanha o desktop de entrada com OpenInputDesktop/SetThreadDesktop e executa GDI/SendInput. Não são criadas janelas próprias como SYSTEM.

Job Object encerra o agente se o supervisor morrer. Troca de sessão física fecha a conexão e relança o agente. Reconexão manual. Encerramento normal pede shutdown, libera entradas e fecha canais; término forçado é fallback.

IPC usa pipe local com ACL de SYSTEM/administradores, autenticação do processo servidor e rejeição de clientes de rede. Comandos enumerados e payload de até 64 KiB.

Configuração compartilhada usa trava de arquivo entre processos e substituição atômica. O worker monitora política e rede a cada dois segundos. Revogação, alteração de senha e desabilitação cortam sessões existentes.

Listener usa apenas o IP escolhido; nunca wildcard. Firewall restringe programa, porta, origem e perfil. Rede local requer endereço IPv4 privado ativo.

Captura GDI com blocos alterados de 128 pixels, comprimidos sem perda com Brotli. Cada lote de até 192 KiB aguarda confirmação após a apresentação; a compressão não ocupa a thread de entrada. FPS é limitado e telas idênticas não geram imagem. JPEG permanece para versões antigas. O viewer oferece ajuste à janela ou pixels a 100%. Movimentos do mouse são coalescidos; teclas/cliques conservam a ordem. Resolução da imagem orienta coordenadas. Perda de foco libera entradas enviadas pelo ADMIN.

Fontes: [serviços interativos](https://learn.microsoft.com/windows/win32/services/interactive-services), [CreateProcessAsUser](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw), [SetThreadDesktop](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setthreaddesktop).