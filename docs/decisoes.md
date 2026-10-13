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
- GDI e envio diferencial sem perda em 1.1.0; JPEG mantido por negociação com 1.0. DXGI/H.264 continuam como evolução futura.
- Instalação separada da antiga: novo AppId, Program Files\NexoDesk New e ProgramData\NexoDesk-New.
- Não há migração silenciosa de certificados, senhas ou pareamentos.
- Configuração permanece desabilitada até definir senha e habilitar acesso.
- Código temporário pareia uma vez; endereço e certificado ficam no catálogo para conexões posteriores.

Usuário autorizou publicar código e release 1.1.0 no GitHub. Cada nova versão enviada à main deve gerar um release. Testes de aceitação em duas máquinas permanecem pendentes. Consulte [validação](validacao.md) antes de depender do serviço em login/UAC/reinício.