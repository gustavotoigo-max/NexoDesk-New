# Segurança

Acesso exige certificado ADMIN pareado, pin do remoto, política habilitada e senha correta. Estar no Tailscale/rede local não autoriza controle. Fora da janela temporária de pareamento, certificados desconhecidos são recusados no TLS.

Pareamento PBKDF2/HMAC vincula ambos os certificados e consome o código uma vez. Senha usa PBKDF2-SHA256, 600.000 iterações e sal de 32 bytes. O remoto guarda verificador, sem senha original. HMAC-SHA256 liga nonce novo, certificados e modo Visualizar/Controlar. Comparação constante; comprimento e custo da derivação são limitados antes de processar.

Senha salva no ADMIN: DPAPI CurrentUser. Outra conta precisa informá-la novamente. Verificador remoto é material sensível e fica no diretório protegido.

Interface exige administrador no manifesto e no token. Serviço/agente rodam como SYSTEM. ProgramData, logs, atualizações e chave ECDSA não exportável têm ACL de SYSTEM/administradores. Identidade ausente numa instalação configurada falha fechada.

Binários instalados em Program Files\NexoDesk New. Pipe local autentica processo SYSTEM, bloqueia usuários não elevados e recusa rede. Job Object evita agente órfão.

Firewall Tailscale restringe o executável do agente, TCP 47115 e faixas Tailscale. Firewall local restringe mesma porta/programa, LocalSubnet e perfil Privado. Listener não usa wildcard. Há uma sessão por remoto, limite de negociações/tentativas, timeouts e validação antes de alocar.

Logs incluem horário UTC, identidade e resultado; nunca tela, teclas ou senha. Interface mostra faixa persistente e permite corte pela bandeja. Sem interface, há aviso Windows com opção de encerramento.

## Recuperação e atualizações

Remova o ADMIN no remoto para revogar e cortar sessão. Altere senha para exigir nova credencial. Desabilite acesso para impedir sessões. Em comprometimento, revogue também o dispositivo/grants no Tailscale e preserve logs.

Atualizador aceita Releases do repositório novo, URL HTTPS da publicação e digest SHA-256. Confere conteúdo antes de elevar o instalador, em pasta protegida. Isso verifica integridade/origem no GitHub e não substitui Authenticode. Certificado de assinatura não foi provisionado.

Fontes: [SendInput/UIPI](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput), [digest GitHub](https://docs.github.com/en/rest/releases/assets), [conexões Tailscale](https://tailscale.com/docs/reference/connection-types).