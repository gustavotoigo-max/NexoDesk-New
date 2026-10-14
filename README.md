# NexoDesk New

Acesso remoto privado para até cinco PCs Windows x64. Um aplicativo e um instalador para **ADMIN** e **PC REMOTO**.

- Interface **somente como administrador**, inclusive na primeira abertura pelo instalador.
- Internet pelo Tailscale, sem abrir portas no roteador.
- Conexão direta na mesma rede local, sem Tailscale.
- Pareamento por código temporário, certificados individuais e TLS mútuo.
- Senha exigida a cada conexão; salva com proteção do Windows no ADMIN.
- Monitor principal, cursor remoto, mouse, teclado, desconexão e reconexão.
- Aviso de acesso por dez segundos, com botão para fechar sem desconectar.
- Blocos de tela sem perda, fila de imagem limitada e visualização a 100%.
- Ajuste nítido à janela pela GPU, com botão Nitidez para comparar com o ajuste anterior, sem alterar o monitor remoto.
- Atualizações consultadas ao abrir/a cada minuto e pelo botão Atualizar na janela principal, com faixa somente quando há versão nova e limpeza dos processos da instalação anterior.
- Modo estável de transmissão preservado; seletor experimental preparado para uma futura versão.
- Serviço SYSTEM com agente na sessão física, destinado ao acesso antes do login e a aplicativos elevados.
- Tema claro/escuro, bandeja, indicador de acesso e logs sem imagens, senhas ou teclas.

**Estado:** implementação e instalador disponíveis. Compilação e testes locais descritos em [validação](docs/validacao.md). Login/UAC, reinício e operação entre dois PCs ainda exigem validação real. Instalador sem assinatura Authenticode.

**Versão 1.4.0**, com ajuste nítido à janela e tela cheia opcional na sessão (Ctrl+Alt+F11). Instalador e SHA-256 disponíveis no [release](https://github.com/gustavotoigo-max/NexoDesk-New/releases/tag/v1.4.0). [Plano da transmissão experimental](docs/plano-transmissao.md).

## Instalar

Baixe o [instalador 1.4.0](https://github.com/gustavotoigo-max/NexoDesk-New/releases/download/v1.4.0/NexoDesk-New-Setup-1.4.0.exe) e execute nos dois computadores. A cópia local fica em **artifacts/NexoDesk-New-Setup-1.4.0.exe**. As versões 1.1.0 e 1.2.1 permanecem nos releases anteriores. O instalador inclui o runtime .NET; não é necessário baixá-lo para executar.

1. No remoto, escolha **PC REMOTO**.
2. Escolha **Tailscale (internet)** ou **Rede local**, selecione o IP quando necessário e aplique.
3. Defina uma senha de 12 a 128 caracteres e habilite o acesso.
4. Gere o código temporário e passe ao responsável pelo ADMIN.
5. No ADMIN, adicione o remoto colando o código e conecte.
6. Informe a senha na primeira conexão; ela fica protegida na conta Windows do ADMIN.

No PC REMOTO, o **X** recolhe a janela para a bandeja. **Sair e encerrar o serviço**, no ícone perto do relógio, encerra a interface, o supervisor e o agente desta instalação; o Tailscale continua conectado. Ao abrir novamente, o serviço é iniciado. A preferência de início com o Windows é mantida. [Processos e atualização](docs/processos-atualizacao.md).

Os campos de pareamento e senha foram ampliados; o olho revela/oculta cada senha. A interface usa seções com separadores e cantos discretos.

[Tutorial](docs/tutorial.md) · [Diagnóstico](docs/troubleshooting.md) · [Desempenho](docs/desempenho.md) · [Publicação](docs/publicacao.md)

## Compilar, testar e gerar instalador

Windows x64, [.NET 10 SDK](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0) e [Inno Setup 6](https://jrsoftware.org/isdl.php). Tailscale é necessário somente para testar acesso pela internet.

    .\scripts\Test-Prerequisites.ps1 -Build
    dotnet build NexoDesk.sln -c Release
    dotnet test NexoDesk.sln -c Release
    .\scripts\Build-Installer.ps1

A geração interrompe se compilação/testes falharem. Saída: instalador e arquivo SHA-256 em artifacts. Prévia: artifacts/ui/remote-light.png e remote-dark.png.

## Estrutura

| Projeto | Responsabilidade |
|---|---|
| src/NexoDesk.App | Interface elevada, catálogo e viewer |
| src/NexoDesk.Shared | Protocolo, TLS, pareamento, senha e sessões |
| src/NexoDesk.Desktop | Captura, entrada, armazenamento e IPC |
| src/NexoDesk.Service | Serviço SYSTEM e agente da sessão física |
| src/NexoDesk.Ui | Componentes e temas Nexotool |
| tests | Testes de núcleo, TLS real, Windows e renderização |
| installer, scripts | Instalação e geração reproduzível |

Configuração/logs: **%ProgramData%\NexoDesk-New**. Certificado: **LocalMachine\My**, chave não exportável restrita a SYSTEM/administradores. Sem migração automática da instalação anterior.

## Repositório e continuidade

Destino: [gustavotoigo-max/NexoDesk-New](https://github.com/gustavotoigo-max/NexoDesk-New). CI compila, testa e publica um release para cada nova versão de Directory.Build.props enviada à main; tags vX.Y.Z também são aceitas. O atualizador verifica origem e SHA-256 informado pelo GitHub.

Base reaproveitada: gustavotoigo-max/NexoDesk, commit 36b24ef75375cc0e7bac9820b27e03612eede947. Os dois documentos de requisitos permanecem na raiz. [Decisões atuais](docs/decisoes.md).

Não há transferência de arquivos, áudio, clipboard ou múltiplos monitores nesta entrega. Ctrl+Alt+Del não é sintetizado e políticas de login do Windows não são alteradas.