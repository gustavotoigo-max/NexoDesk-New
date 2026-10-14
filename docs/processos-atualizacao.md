# Processos e atualização — 1.4.0

## Processos usados

| Processo | Papel | Encerramento |
| --- | --- | --- |
| NexoDesk New.exe | Interface elevada, ADMIN ou PC REMOTO | Saída explícita/atualização |
| service/NexoDesk.Service.exe | Supervisor SYSTEM na Session 0 | Parada pelo SCM |
| service/NexoDesk.Service.exe --worker | Captura e entrada na sessão física | Shutdown e Job Object do supervisor |
| sc.exe / schtasks.exe | Comandos curtos de configuração | Espera limitada a 15 s; comando terminado se travar |
| Instalador NexoDesk-New-Setup-*.exe | Substituir a versão | Continua até concluir; relança a interface elevada |
| Tailscale | Rede privada independente | Permanece conectado |

O **X** do PC REMOTO recolhe a janela para a bandeja. Para parar o programa inteiro, escolha **Sair e encerrar o serviço**. No ADMIN, fechar encerra também as sessões e os componentes da instalação. A opção de iniciar com o Windows não é apagada; reabrir o aplicativo ou o próximo início configurado volta a iniciar o serviço.

A interface cancela consultas e fecha sessões antes da parada. Espera até 20 s pelos processos do serviço. Somente em caso de travamento suspende recuperação automática, encerra os executáveis conferidos desta instalação e restaura a política de recuperação. Outros processos com o mesmo nome, em outras pastas, são preservados. Uma falha na limpeza é informada em vez de encerrar silenciosamente deixando componentes ativos.

O instalador para o serviço e suspende recuperação antes de examinar os PIDs. Espera a saída, confere caminho completo no mesmo handle e termina processos antigos que não saem. Uma nova passagem detecta agentes criados durante a parada. Não usa taskkill por nome global. O desinstalador segue a mesma regra. Somente depois disso os arquivos são substituídos; o serviço e a interface da nova versão são iniciados.

## Atualizações com pouco tráfego

Ao abrir e a cada minuto, uma requisição HTTP **HEAD** consulta o redirecionamento de `releases/latest`: recebe cabeçalhos, sem baixar HTML ou instalador. Na consulta real da entrega, GitHub retornou HTTP 302 e Content-Length 0. Cabeçalhos/TLS ainda consomem tráfego; não é tráfego zero.

Se a versão é a mesma ou anterior, a API de releases não é consultada. Para uma versão superior, a API valida o instalador, origem e SHA-256. Esses metadados ficam em cache por 15 minutos para o mesmo tag; mudança de tag invalida o cache imediatamente. Falhas são tentadas na próxima consulta e não são anunciadas como atualização válida. Consultas simultâneas são impedidas.

A faixa só aparece com atualização validada. O botão **Atualizar** no rodapé antecipa a verificação; o resultado fica no tooltip e em Configurações → Atualização. Na faixa, **Atualizar** consulta novamente e inicia download/instalação; instalação automática sem ação do usuário permanece desabilitada.

Senha revelada existe somente no diálogo. Ao ocultar, o campo em texto é apagado; fechar limpa os dois campos. Senhas continuam sendo protegidas pelo Windows, sem logs ou configuração em texto.

## Verificar nos PCs

1. Fazer uma primeira instalação normal, aceitar UAC e marcar Abrir agora: primeira configuração deve abrir elevada sem erro.
2. Sem nova versão, conferir faixa oculta. Publicar versão superior e aguardar até um minuto, ou clicar Atualizar; faixa deve aparecer.
3. No remoto, usar Sair e encerrar o serviço. Conferir no Gerenciador que interface/supervisor/agente saíram e Tailscale permanece.
4. Reabrir e confirmar acesso; atualizar com a interface recolhida e depois com uma sessão aberta. Após instalar, devem existir somente componentes da nova versão.
5. Conferir pareamento e senhas nos temas claro/escuro e em escala Windows 100%, 125% e 150%; alternar o olho durante a edição.

Testes locais usam processo descartável para reproduzir um órfão e verificar isolamento por caminho; não param o serviço instalado no PC de desenvolvimento. Instalação, SCM real, saída da bandeja e atualização de duas máquinas exigem confirmação no ambiente do usuário.

Fontes: [Inno Setup: Run/runascurrentuser](https://jrsoftware.org/ishelp/topic_runsection.htm), [GitHub: último release](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases).
