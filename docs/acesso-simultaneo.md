# Acesso simultâneo — 1.5.0

## Regra aprovada

Até dois ADMINs autenticados podem ver a mesma tela física. Uma nova ação de mouse/teclado assume o controle remoto, sem desconectar o outro participante. O outro recupera o controle ao produzir uma nova ação. Não há desktops independentes nem alteração da resolução física.

- Movimento, clique, rolagem ou nova tecla podem transferir o controle.
- Na transferência, teclas e botões injetados pelo ADMIN anterior são liberados. Repetições de teclas mantidas, solturas atrasadas e arrastes antigos não devem recuperar o controle; solte e pressione novamente para reiniciar um arraste.
- Quem conecta em **Ver a tela** continua apenas observando.
- Atividade física transfere a indicação para **Pessoa no PC REMOTO**. Um ADMIN pode recuperar com nova atividade. O teclado/mouse físicos nunca são bloqueados; modificadores mantidos fisicamente continuam sujeitos ao estado único do Windows. Não se promete isolamento absoluto quando várias pessoas atuam ao mesmo tempo.
- A janela de sessão indica quem controla. O remoto mostra os nomes conectados e permite encerrar todos os acessos. Remover um ADMIN autorizado revoga suas sessões.

## Configuração e compatibilidade

Atualize o remoto e os dois ADMINs. Cada ADMIN usa seu certificado/pareamento individual; a senha é a do mesmo remoto. Gere um código temporário para cada novo pareamento. Todos precisam estar na mesma rede Tailscale com permissão de comunicação, ou na rede local configurada.

**Configurações do PC REMOTO → Acesso simultâneo** fica habilitado por padrão. Desmarcar restaura a reserva de sessão única e o caminho anterior de captura/entrada. A mudança encerra as sessões existentes. O motor **Estável** continua sendo GDI/blocos Brotli sem perda; esta entrega não adiciona H.264 ou monitor virtual.

A capacidade opcional `activity-control-v1` negocia a indicação de posse; clientes antigos não recebem mensagens desconhecidas. Eles ainda podem conectar, mas não mostram quem controla. Para usar/testar o recurso completo, use 1.5.0 em todos os PCs. Uma terceira conexão é recusada.

## Implementação e validação

Cada sessão tem seu próprio histórico de imagem, sequência, fila e confirmação. Um ADMIN lento ou desconectado não reinicia a imagem do outro. A captura continua serializada na thread do desktop, com compressão separada por participante; dois participantes aumentam o trabalho de captura/compressão.

Um coordenador serializa entrada e rastreia teclas/botões por sessão. Um monitor nativo observa atividade física, ignora eventos injetados e acompanha o desktop de entrada. Os callbacks apenas sinalizam a transferência; não esperam pelo injetor. Se o monitor estiver indisponível, a entrada remota coordenada aguarda sua recuperação, com problema informado na interface. O modo anterior continua selecionável.

Testes automatizados cobrem TLS real com duas sessões, transferência/solturas, atividade local simulada, um pixel alterado em ambas, participante lento, saída independente, limite de dois, senha inválida, observação e encerramento de todos. A instalação dos hooks no desktop real e a interação física/login/UAC ainda exigem validação em campo.

## Fontes e distinção

A regra acima é a escolha do usuário para o NexoDesk. A documentação pública da [barra do TeamViewer](https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/remote-session-toolbar/) descreve opções de entrada remota, mas não confirma esse algoritmo exato de arbitragem.

As APIs e limites do observador seguem a Microsoft: [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc), [flags de teclado](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) e [flags de mouse](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct).
