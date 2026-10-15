# Janela de acesso — 1.7.1

A sessão abre maximizada como janela normal, usando a área de trabalho disponível acima/ao lado da barra do Windows. Não fica Topmost e não entra em tela cheia automaticamente. O monitor remoto mantém sua resolução.

## Imagem e controles

- **100%** vem marcado: largura/altura continuam em pixels físicos originais, considerando o DPI do ADMIN; não há redução nem reforço de nitidez nessa apresentação.
- As antigas faixas de título/acento/rodapé (36 + 2 + 34 unidades lógicas) foram removidas somente do viewer. As outras janelas conservam sua moldura. O espaço recuperado depende do DPI.
- Os controles semitransparentes ocupam uma sobreposição no topo, sem reservar linha na área da imagem. Após 2,5 segundos sem mouse sobre eles, recolhem para **NexoDesk ▾**. Passar o mouse na aba, clicar ou usar **Ctrl+Alt+Home** mostra os controles. O botão **▴** recolhe imediatamente.
- **100%**, **Nitidez**, **Info**, **Principal**, **Tela cheia**, minimizar, maximizar/restaurar e **×** permanecem disponíveis. A alça à esquerda arrasta a janela; duplo clique maximiza/restaura.
- O painel Nitidez mantém intensidade 0–100%, restauração de 35% e preferência salva. Fechar painéis devolve o foco à imagem; entrada não é enviada ao remoto enquanto um painel está aberto.
- A imagem embaixo dos controles permanece íntegra. A área ocupada pelos botões recebe cliques locais; recolha-os para liberar essa área. Os controles não mudam a escala nem os pixels remotos.

## Barra remota e deslocamento em 100%

Na 1.7.0, a imagem em tamanho real iniciava no topo e a barra do Windows remoto podia ficar fora da área visível. A 1.7.1 inicia pela base, mantendo essa barra acessível sem reduzir a imagem. Voltar de tela cheia/ajuste à janela ou mudar a resolução também prioriza a base. Ao redimensionar uma janela que já estava na base, ela permanece alinhada à base.

Aproxime o mouse da borda superior, inferior ou lateral para deslocar a imagem suavemente até essa região. O movimento fica limitado ao conteúdo e para fora da imagem, ao desativar a janela, durante teclas/arrastes e nos painéis. Apenas a câmera local se move; o timer não envia comandos ao remoto nem assume controle de outro ADMIN.

Nesse modo, barras de rolagem não consomem mais largura/altura. **Info → Mover imagem nas bordas (100%)** vem marcado em cada sessão; desmarque para voltar à rolagem manual. A base continua acessível manualmente. O ajuste à janela e a resolução do PC REMOTO permanecem intactos.

## Informações

O botão **Info** mostra dono do controle, modo, canal TLS, latência/FPS/taxa, versão do agente, identificador da sessão, dimensões e ajuste atual. A janela principal ganhou **Sessões ativas**, com os detalhes da conexão e **Voltar ao acesso**. Somente conexões ativas permanecem nessa lista.

## Limites físicos e verificação

Retirar as barras recupera espaço, mas não garante que uma imagem remota maior caiba em 100% na área disponível. Se a altura remota superar a altura local após descontar a barra do Windows, é necessário deslocar a área visível para preservar os pixels originais; a versão 1.7.1 faz isso pelas bordas ou por barras manuais opcionais. Desmarcar 100% continua oferecendo ajuste à janela. Não há recorte silencioso da barra remota nem alteração da resolução.

A maximização usa o comportamento normal do Windows/WindowChrome, com caption de altura zero para não interceptar os primeiros pixels remotos como área de arraste. A barra do Windows segue sua própria configuração, inclusive ocultação automática. O modo Tela cheia explícito continua usando o monitor inteiro; Ctrl+Alt+F11 retorna ao modo anterior.

Testes sem janela nativa verificam moldura sem barras, maximização normal, caption zero, 100% inicial, dimensão da imagem intacta ao recolher controles e renderização nos dois temas. Os testes anteriores de pixels/teclado/streaming permanecem aprovados. Confirmar em campo: barra do Windows nos monitores reais, auto-ocultação, DPI misto, arraste, minimizar/restaurar, painéis, foco/atalhos e tela cheia. Não foram iniciadas sessões remotas nem instalados serviços para esses testes.
