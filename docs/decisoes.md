# Decisões e estado

- Nome do software confirmado pelo usuário: NexoDesk New.

- O resumo consolidado orienta os requisitos evoluídos após o plano inicial.
- Solicitação atual: interface somente como administrador.
- Usuário confirmou Tailscale pela internet e conexão direta na mesma rede local.
- C#/.NET 10 e WPF; programa único com dois papéis e instalador autossuficiente.
- Conexão escolhida explicitamente. Com vários IPs locais, o responsável seleciona a interface.
- Sem relay externo, encaminhamento de portas ou escuta pública.
- ADMIN pareado e senha a cada conexão; habilitação explícita no remoto, sem pergunta Permitir por sessão.
- PBKDF2-SHA256, 600.000 iterações, sal de 32 bytes. Prova HMAC ligada ao nonce, certificados e modo.
- Identidade/configuração compartilhada em LocalMachine/ProgramData, com ACL para SYSTEM/administradores.
- Serviço SYSTEM supervisiona agente sem interface na sessão física. A UI elevada permanece separada.
- GDI e envio diferencial sem perda em 1.1.0 preservados como modo Estável na 1.2.0; JPEG mantido por negociação com 1.0.
- Usuário escolheu preparar o seletor/plano nesta entrega; implementar o novo motor depois. Experimental fica indisponível até existir implementação validada.
- Aviso de acesso por dez segundos, com fechamento manual sem encerrar a conexão; estado e encerramento permanecem na janela/bandeja.
- Consulta de releases ao abrir/a cada quatro horas; botão Atualizar permanente na janela principal, com instalação iniciada pelo usuário.
- Usuário confirmou uso simultâneo do monitor físico do PC REMOTO: sua resolução deve ser preservada. Monitor virtual está em pesquisa, sem implementação/instalação nesta entrega.
- Tela cheia opcional na 1.2.1, mantendo a escolha de 100% e restaurando a janela ao sair. Usuário confirmou que funcionou perfeitamente no seu teste.
- Autorização permanente do usuário: a cada nova versão, enviar o código ao GitHub e publicar o release com instalador e SHA-256 após as verificações necessárias, sem pedir nova confirmação de publicação. Conferir conclusão do workflow e disponibilidade no atualizador.
- Ajuste nítido local autorizado e implementado na 1.3.0: redução cúbica Direct2D e Sharpen moderado, apresentação GPU compartilhada com WPF, retorno ao ajuste anterior por botão/configuração ou falha de GPU. Não muda a resolução física; 100% não aplica filtros. Aceitação visual nos PCs ainda pendente.
- Usuário relatou teclado sem funcionamento após a 1.3.0. Correção de foco na 1.3.1: cliques remotos não focam o contêiner; botões Nitidez/100% devolvem foco à imagem. Confirmação de digitação no PC do usuário pendente.
- Plano da evolução em docs/plano-transmissao.md: instrumentação, envio limitado, DXGI e H.264, mantendo o Estável como padrão.
- Instalação separada da antiga: novo AppId, Program Files\NexoDesk New e ProgramData\NexoDesk-New.
- Não há migração silenciosa de certificados, senhas ou pareamentos.
- Configuração permanece desabilitada até definir senha e habilitar acesso.
- Código temporário pareia uma vez; endereço e certificado ficam no catálogo para conexões posteriores.

Usuário autorizou publicar código e release 1.1.0 e, depois, as melhorias acumuladas no release 1.2.1 no GitHub. A 1.2.0 foi uma entrega local intermediária; a publicação atual reúne suas melhorias na 1.2.1. Cada nova versão enviada à main deve gerar um release. Testes de aceitação em duas máquinas permanecem pendentes. Consulte [validação](validacao.md) antes de depender do serviço em login/UAC/reinício.