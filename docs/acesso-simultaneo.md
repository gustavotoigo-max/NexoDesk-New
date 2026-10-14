# Revisão de acesso simultâneo

## Diagnóstico confirmado

O funcionário agora aparece no console Tailscale e recebeu a mensagem de sessão ocupada do NexoDesk. A tentativa chegou ao agente e passou pela autenticação/checagem de senha antes da decisão de sessão. O bloqueio está no NexoDesk 1.4.0: SessionGate reserva uma única vaga para qualquer modo, inclusive Ver a tela.

## Pontos que precisam mudar juntos

- AgentHost: trocar a referência de sessão única por registro limitado de sessões; reservar/liberar vagas sem corrida e manter a opção de sessão única.
- Imagem: cada ADMIN precisa de seu próprio histórico de blocos, sequência e confirmação. DesktopThread hoje mantém encoder/fila compartilhados; outro consumidor poderia receber somente parte das mudanças. A conexão/desconexão de um participante não pode reiniciar a imagem dos demais.
- Entrada: separar posse de mouse/teclado e liberação de teclas. Hoje ReleaseAll usa um estado compartilhado; sair de uma sessão não pode soltar teclas de outro controlador.
- Serviço: revogar por certificado em todas as sessões correspondentes; desligar acesso, mudar senha/rede ou sair deve encerrar todas. Cada ADMIN conserva autenticação individual por certificado e senha.
- Interface remota: mostrar a lista de ADMINs ativos, modo e quem controla; permitir encerrar um participante ou todos. Aviso inicial continua por dez segundos.
- Compatibilidade: novos recursos precisam ser negociados. Clientes antigos continuam funcionando no modo atual; mensagens novas não podem ser enviadas a eles.

## Comportamento a definir

| Modelo | Comportamento |
| --- | --- |
| Um controla, outro observa — recomendado | Ambos veem a mesma tela; somente o dono do controle injeta entrada. Pedido/transferência de controle aparece nas interfaces. A transferência libera teclas/botões do dono anterior antes de habilitar o próximo. |
| Ambos controlam | Ambos veem a mesma tela e injetam entrada. O remoto tem um único cursor e foco; ações se intercalam. Teclas/botões exigem rastreamento individual para que uma desconexão não prejudique o outro. |

A escolha foi solicitada ao usuário. Nenhum dos modelos deve ser presumido; implementação do comportamento depende dessa definição.

## Verificações necessárias

1. Duas conexões autenticadas e imagem completa em ambas; mudanças de um pixel continuam chegando aos dois.
2. ADMIN lento/sem confirmar imagem não trava o outro; filas/memória permanecem limitadas.
3. Desconectar/remover um ADMIN não encerra a sessão restante nem reinicia sua imagem.
4. Teclas e botões são liberados corretamente na saída e na transferência, conforme o modelo escolhido.
5. Desabilitar acesso, alterar senha/rede, sair, atualizar e parar serviço encerram todas as sessões.
6. Recusar senha/certificado inválido, respeitar limite de participantes e preservar clientes antigos/sessão única.
7. Confirmar teclado, nitidez em janela, 100%, tela cheia e aviso de acesso nas duas máquinas.

O motor Estável permanece como base. Esta revisão não instala monitor virtual e não cria desktops Windows independentes: os participantes acessam a mesma tela física.
