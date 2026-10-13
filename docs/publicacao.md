# Versões e releases

Cada versão X.Y.Z de Directory.Build.props corresponde a um release vX.Y.Z, com instalador e SHA-256.

1. Faça a alteração completa, aumente a versão de Directory.Build.props e escreva docs/releases/X.Y.Z.md.
2. Gere e valide com scripts/Build-Installer.ps1.
3. Envie o código à main. O workflow compila, testa, empacota e publica a nova versão automaticamente.
4. Confira Actions e o release. O atualizador consulta o último release estável do repositório.

Tags vX.Y.Z e execução manual do workflow também são aceitas. A tag deve coincidir com a versão do projeto. PRs validam sem publicar. Releases existentes não recebem substituição de arquivos; alterações do produto exigem uma nova versão.

Publicação manual do instalador local, após enviar o commit:

    .\scripts\Publish-Release.ps1

É necessário GitHub CLI autenticado com acesso de escrita ao repositório. Link de instalação, caso ausente: https://cli.github.com/. O script verifica o SHA-256, confere a tag e aponta o release ao commit. Não muda automaticamente o código nem faz commits.

## Atualização no aplicativo

A consulta ocorre ao abrir, a cada quatro horas e pelo botão em Configurações > Atualização. Falha de internet não é tratada como prova de versão atual.

Ao clicar em atualizar, o aplicativo baixa e confere o instalador. Inicia a instalação elevada com PID antigo, sai sem pedir nova confirmação da sessão e encerra conexões. O instalador espera a saída; se houver travamento, confere o caminho e termina somente a interface dessa instalação. Também encontra a interface antiga quando o pedido veio da 1.0, que não envia PID. Depois substitui os arquivos, reinicia o serviço e abre a interface nova. O Windows não é reiniciado automaticamente.

Atualização depende de permissão administrativa e de um release íntegro publicado no GitHub. Código e SHA-256 são verificados; assinatura Authenticode ainda não está configurada.