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

## Exibição da tela

- **100%** preserva um pixel remoto por pixel local; imagens maiores exigem rolagem.
- Desmarcar **100%** ajusta a imagem à janela, com redução quando necessário.
- **Tela cheia**, no rodapé da sessão, usa o monitor atual sem mudar a resolução do PC REMOTO.
- **Sair da tela cheia** ou **Ctrl+Alt+F11** restaura a janela; esse atalho é tratado localmente. F11 sozinho continua disponível no remoto.
- Entrar/sair mantém a escolha de 100%. Alt+Tab continua disponível para trocar de aplicativo no ADMIN.
- Para nitidez em janela e preservação da tela física, consulte a [pesquisa de resolução](pesquisa-resolucao.md). Nenhum driver ou mudança automática de resolução foi aplicado nesta entrega.

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

O aviso de acesso aparece por dez segundos. **Fechar aviso** apenas fecha a faixa; **Encerrar acesso** encerra a conexão. Depois, janela remota e bandeja continuam indicando a sessão. Sem interface, o Windows apresenta um aviso de dez segundos: **OK** fecha e **Cancelar** encerra. O fluxo sem interface ainda exige teste real.

## Atualizar e preparar testes de transmissão

- A consulta aos releases estáveis do GitHub ocorre ao abrir e a cada quatro horas.
- Na janela principal, **Atualizar** consulta novamente e, havendo uma versão nova verificável, baixa e inicia a instalação. As conexões são encerradas durante a atualização.
- Em **Configurações > Transmissão de tela**, **Estável — atual, sem perda** preserva o motor existente.
- **Experimental — em preparação** permanece indisponível até a implementação do novo motor. A transmissão será escolhida no PC REMOTO.
- O instalador local 1.2.1 permite testar esta entrega. O release público 1.1.0 continua disponível enquanto a nova versão não for publicada.