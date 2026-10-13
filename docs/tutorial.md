# Primeira conexão

## Instalar

1. Execute NexoDesk-New-Setup-1.1.0.exe como administrador nos dois PCs.
2. Abra pelo atalho, aceite a elevação e escolha papel/nome.
3. O serviço NexoDeskRemote é registrado como SYSTEM. A senha solicitada pelo NexoDesk New é própria do acesso remoto; não é a senha da conta Windows.

## Internet com Tailscale

1. Confira se os dois PCs pertencem à mesma rede Tailscale.
2. No remoto, selecione **Tailscale (internet)** e aplique.
3. Restrinja no painel Tailscale os ADMINs que podem alcançar TCP 47115 no remoto.

Se necessário: [Tailscale para Windows](https://tailscale.com/download/windows).

## Rede local sem Tailscale

1. No remoto, selecione **Rede local** e o IP privado da interface utilizada.
2. Aplique e configure essa rede como **Privada** no Windows.
3. O ADMIN precisa alcançar esse IP na mesma rede. Isolamento de clientes Wi-Fi pode impedir a conexão.

Firewall limita o perfil Privado e a sub-rede local. São aceitos IPv4 privados 10/8, 172.16/12 e 192.168/16. O aplicativo não configura portas no roteador.

## Parear e acessar

1. No remoto, defina uma senha de 12 a 128 caracteres.
2. Ligue **Habilitar acesso dos ADMINs pareados**.
3. Gere o código e passe ao ADMIN. Expira em dez minutos e funciona uma vez.
4. No ADMIN, **Adicionar PC REMOTO**, cole o código e confirme.
5. Escolha **Controlar** ou **Visualizar**, conecte e informe a senha.
6. Após conexão aceita, a senha é salva com DPAPI na conta Windows atual.

Trocar a senha corta a sessão existente. Após recusa, a senha salva é limpa e a próxima tentativa pede a nova senha. Trocar de rede muda o endereço: refaça a inclusão pelo código do novo endereço.

## Encerrar e revogar

- Desconectar no ADMIN encerra a sessão.
- Encerrar acesso no indicador, na bandeja ou na janela remota corta a sessão atual.
- Desabilitar acesso no remoto impede novas conexões e encerra a atual.
- Remover um ADMIN revoga o certificado e exige novo pareamento.
- Fechar a janela remota mantém a bandeja.
- Sair pelo menu para o serviço nesta execução; ao reiniciar, ele volta se a inicialização automática estiver ligada.

## Reinício e tela bloqueada

Mantenha a inicialização com Windows habilitada: serviço automático antes do login e tarefa da interface elevada no logon da conta administrativa que configurou a opção.

O agente acompanha Default/Winlogon na sessão física. Valide o fluxo nos PCs reais antes de depender dele. Ctrl+Alt+Del não é sintetizado; se uma política exige essa sequência, o login precisa de validação específica.

A faixa de acesso permanece enquanto a interface está aberta. Sem interface numa sessão de usuário, o Windows apresenta aviso com **Cancelar** para encerrar. Esses fluxos de serviço ainda aguardam teste real.