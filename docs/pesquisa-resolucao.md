# Pesquisa: resolução automática com nitidez em janela

Pesquisa realizada em 14/10/2026. Referências técnicas verificadas em documentação Microsoft e repositórios dos projetos. Esta entrega inclui tela cheia opcional; não instala drivers nem altera resolução remota.

## 1. Recomendação para o uso confirmado

O usuário prefere trabalhar em janela e confirmou que alguém também usa o monitor físico do PC REMOTO. Portanto, **preservar a resolução física é requisito**.

**Recomendo dois protótipos, conforme o objetivo:**
- Para continuar vendo a mesma tela física, testar primeiro escala Direct2D de alta qualidade com nitidez moderada. Preserva o fluxo atual, embora não garanta igualdade a 100%.
- Para pixels 1:1 sem rolagem, prototipar uma área remota em monitor virtual com a resolução da área útil da janela do ADMIN. Exige aceitar uma tela adicional; não é um espelho idêntico da tela física nem uma sessão independente para dois usuários.

A distinção entre esses objetivos deve orientar a implementação; não tratar uma área virtual extra como substituição silenciosa do compartilhamento da tela física.

Se o requisito for ver **exatamente a tela física inteira** numa janela menor, não há como manter todos os seus pixels em tamanho original: precisamos escolher entre redução, recorte/rolagem ou mais área de exibição. Refinar a escala pode melhorar legibilidade, mas não garantir reprodução igual a 100%.

A pesquisa anterior continua relevante: resolução, captura, codec, fila e apresentação resolvem partes diferentes do problema. Um monitor virtual resolve o tamanho da imagem produzida; a transmissão sem perda preserva seus pixels; DXGI/H.264 e controle de fila podem melhorar custo e movimento.

## 2. O diagnóstico do viewer atual

O NexoDesk recebe a imagem na resolução original. No modo 100%, ajusta dimensões WPF considerando o DPI local, para um pixel capturado ocupar um pixel físico de exibição. No ajuste à janela, usa `Stretch=Uniform` e `BitmapScalingMode=HighQuality`.

A Microsoft documenta que HighQuality corresponde a Fant, um filtro de redimensionamento. Portanto, o viewer **já usa um filtro de alta qualidade**: não basta ativar essa opção. NearestNeighbor preserva bordas de alguns desenhos ao ampliar, mas reduzir texto pode eliminar traços e gerar irregularidades. Não é uma solução universal de nitidez. [BitmapScalingMode](https://learn.microsoft.com/dotnet/api/system.windows.media.bitmapscalingmode).

A melhora observada em 100% é compatível com perda de nitidez causada pela escala. Não prova, isoladamente, a origem da melhoria de latência; separar renderização, fila e rede na medição.

Exemplo: uma tela de 1920×1080 não cabe integralmente, pixel a pixel, numa área útil de 1000×700. Um codec melhor não aumenta a quantidade de pixels físicos disponíveis.

## 3. Métodos eficazes e seus limites

| Método | Nitidez em janela | Tela física preservada? | Adequação |
|---|---|---|---|
| Escala Direct2D + nitidez moderada | Pode melhorar aparência; há redução de detalhes | Sim | **Primeiro protótipo para a mesma tela física.** |
| 100% com rolagem/recorte | Preserva pixels da captura | Sim | Já existe; não elimina rolagem. |
| Alterar temporariamente resolução física | Produz desktop menor, que pode caber em 100% | Não | Incompatível com a preferência confirmada. |
| Monitor virtual em desktop estendido | Permite renderizar outra área na resolução da janela | Preserva o modo físico, mas muda a topologia | **Melhor candidato para protótipo**, se uma área extra for aceitável. |
| Sessão remota/VM independente | Pode renderizar na resolução do cliente, com foco próprio | Pode preservar sessão local conforme plataforma | Outra arquitetura, com aplicações/sessão próprias. |

### Escala e nitidez por GPU: primeiro protótipo para a mesma tela

Direct2D oferece um efeito de escala com redução cúbica de alta qualidade e um efeito Sharpen nativo no Windows 10. São componentes prontos: não exigem criar um novo codec ou alterar o monitor remoto. A redução cúbica usa um passo de pré-redução quando necessário; depois aplica interpolação cúbica. [Scale effect](https://learn.microsoft.com/en-us/windows/win32/direct2d/high-quality-scale), [Sharpen effect](https://learn.microsoft.com/en-us/windows/win32/direct2d/sharpen-effect).

Proposta: conservar a imagem recebida sem perda, reduzir uma única vez para a área útil e aplicar nitidez moderada apenas quando houver redução. Comparar com Fant atual; desativar filtros em 100%. Ajustar intensidade/limiar com texto, cores, planilhas e linhas finas. Nitidez forte pode criar halos ou falsear bordas; aprovação deve considerar legibilidade, não só contraste.

A integração candidata é Vortice.Direct2D1/Direct3D11, com apresentação WPF apropriada. Evitar copiar o quadro inteiro GPU→CPU a cada repintura; medir custo da atualização por blocos e do filtro. Ainda não foi integrado nem demonstrado ganho. [Bindings Vortice](https://github.com/amerkoleci/Vortice.Windows).

Também foi avaliado NVIDIA Image Scaling, que oferece shaders e licença MIT. Há uma restrição relevante: a configuração pública do NVScaler aceita razão entrada/saída de 0,5 a 1, isto é, ampliação/manutenção, não a redução necessária aqui. O NVSharpen separado poderia ser avaliado após outra redução, mas Direct2D já oferece o componente nativo que precisamos testar primeiro. Não recomendar NVScaler como solução pronta para diminuir a tela. [SDK NVIDIA](https://github.com/NVIDIAGameWorks/NVIDIAImageScaling), [restrição no código](https://github.com/NVIDIAGameWorks/NVIDIAImageScaling/blob/main/NIS/NIS_Config.h).

**Limite:** esses filtros podem preservar melhor a aparência de letras ao reduzir; não recuperam todos os pixels descartados nem equivalem à renderização de aplicações no tamanho correto. Não foi encontrado um filtro que garanta isso para qualquer texto e fator de redução.

### Alteração de resolução física

`EnumDisplaySettings` enumera modos oferecidos pelo dispositivo. `ChangeDisplaySettingsEx` permite testar um modo com `CDS_TEST` e efetivar mudanças. Não cria arbitrariamente qualquer largura/altura num monitor físico. O desktop pode reorganizar janelas, e o monitor pode fazer sua própria escala; não há promessa geral de nitidez para quem está lá. [EnumDisplaySettings](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw), [ChangeDisplaySettingsEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexa).

O TeamViewer distingue escala local de mudança da resolução remota. A publicação oficial de suporte apresenta ambas as opções, mas não prova um algoritmo de ajuste sem perdas para toda combinação de telas. [TeamViewer e monitores 4K](https://community.teamviewer.com/English/discussion/5575/~/English/kb/product-updates).

### Monitor virtual: renderizar no tamanho necessário

O Windows possui o modelo Indirect Display Driver (IDD), com a extensão IddCx, para monitores virtuais e displays remotos. Ele permite que aplicações renderizem para um monitor que não corresponde a uma saída física tradicional. O exemplo Microsoft IddSample é um ponto de partida didático, com tarefas de produção ainda a implementar. [Modelo IDD](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview), [IddSample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/indirect-display-driver-sample/).

Em modo **estendido**, manteríamos o monitor físico existente e acrescentaríamos a área capturada. Não usar duplicação/espelhamento como solução automática: duplicar a mesma superfície não garante duas renderizações independentes em resoluções distintas. As aplicações precisam estar na área virtual para aproveitar a resolução adequada.

Isso evita reduzir a imagem depois de capturada. Ainda exige controlar DPI, origem de captura, posição do mouse e janelas. Acrescentar/remover um monitor pode reorganizar aplicações; preservar o modo físico não significa ausência de qualquer efeito na sessão.

### Limitação decisiva: monitor extra não separa usuários

Uma tela virtual estendida pertence à mesma sessão interativa. Windows compartilha o teclado e transfere o foco entre janelas. Por isso, pessoa local e ADMIN podem disputar teclado, cursor e foco, mesmo com aplicações em monitores distintos. A virtualização do display não resolve essa concorrência. [Entrada de teclado](https://learn.microsoft.com/en-gb/windows/win32/inputdev/about-keyboard-input), [desktop interativo](https://learn.microsoft.com/en-us/windows/desktop/winstation/desktops).

Se a exigência for duas pessoas trabalhando de maneira independente, avaliar sessão/VM separada. Isso não é a sessão física atualmente controlada pelo NexoDesk. A documentação RDS descreve sessões com identificadores e estações interativas próprios; viabilidade depende da edição, configuração e aplicações utilizadas. [Sessões RDS](https://learn.microsoft.com/en-us/windows/win32/termserv/terminal-services-sessions).

## 4. O que já existe pronto

### SudoVDA + exemplo de integração Apollo

SudoVDA é um driver virtual baseado em IddCx. O autor declara MIT e CC0/domínio público para suas alterações, com a licença da base Microsoft a conferir. Sua documentação inclui seleção de GPU, quantidade de monitores e watchdog. Isso evita começar um driver do zero. Assinatura, build distribuível e instalação precisam ser confirmados no artefato escolhido. [Repositório SudoVDA](https://github.com/SudoMaker/SudoVDA).

O Apollo usa SudoVDA para criar um display ajustado à resolução/taxa de quadros do cliente. É uma implementação concreta de resolução nativa por cliente. O projeto completo é GPLv3: não confundir a licença da aplicação com a do driver. Também não presumir que o tamanho do monitor do cliente corresponda à área útil de uma janela de outro aplicativo. [Apollo](https://github.com/ClassicOldSong/Apollo).

O cabeçalho de integração consultado expõe criação com largura/altura/taxa e GUID, remoção e controle do watchdog via `DeviceIoControl`. Isso torna plausível construir interoperabilidade C# para o driver sem importar toda a aplicação. O cabeçalho, por si só, não comprova redimensionamento contínuo de um monitor existente sem remoção. Esse comportamento precisa ser validado. [API consultada](https://github.com/ClassicOldSong/Apollo/blob/master/third-party/sudovda/sudovda.h), [integração Windows](https://github.com/ClassicOldSong/Apollo/blob/master/src/platform/windows/virtual_display.cpp).

**Avaliação:** primeiro candidato para uma prova de criação na resolução do viewport. Não instalar no projeto atual antes de testar continuidade, assinatura, licenças e compatibilidade nos PCs alvo.

### VirtualDrivers/Virtual-Display-Driver

O projeto oferece monitores virtuais e resoluções personalizadas; seu README identifica suporte a builds assinados. Configurações de resolução usam arquivo XML. Isso não comprova que toda largura/altura possa ser alterada instantaneamente sem atualizar/reiniciar recursos. O código declara licença MIT. [Projeto](https://github.com/VirtualDrivers/Virtual-Display-Driver), [licença](https://github.com/VirtualDrivers/Virtual-Display-Driver/blob/master/LICENSE), [releases](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases).

**Avaliação:** segunda opção para protótipo, particularmente se modos configurados forem suficientes. Conferir a automação disponível na versão escolhida, sem tratar a existência de um driver assinado como integração já pronta com NexoDesk.

### RDP com resolução dinâmica

O protocolo Microsoft Display Control permite que o cliente envie uma nova configuração de monitores/resolução. O servidor valida e reconfigura a sessão quando possível. É um modelo eficaz de adaptar a renderização à janela; não é uma chamada que possamos ligar ao fluxo atual TCP/Brotli sem implementar essa arquitetura. [MS-RDPEDISP](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-rdpedisp/bdc90b21-4b14-43bc-9c03-b7fecbfc6a1f).

Avaliar RDP se decidirmos usar outra sessão/ambiente. Não pressupor que preserva o acesso simultâneo à sessão física nos Windows atualmente utilizados.

## 5. Proposta de prova de conceito

Primeiro, comparar Fant atual, redução cúbica Direct2D e redução cúbica com Sharpen moderado, nos mesmos frames e dimensões. Conservar o bitmap original como referência e confirmar ausência de filtro em 100%. Medir tempo de apresentação e legibilidade, incluindo ponto decimal e texto colorido. Esse protótipo altera somente o viewer e continua vendo a tela física.

A implementação do monitor virtual permanece futura. Proposta de engenharia:

1. **Confirmar o fluxo de trabalho:** mesma tela física compartilhada ou aplicações numa área extra. Monitor virtual serve à segunda opção.
2. **Prototipar display estendido:** preservar resolução, escala e monitor principal físicos. Registrar topologia antes/depois.
3. **Enviar tamanho do viewport em pixels físicos:** usar a área de imagem disponível, não a dimensão externa da janela nem a resolução total do monitor ADMIN. Considerar DPI local.
4. **Escolher driver e criação:** avaliar SudoVDA e VDD; confirmar assinatura/licença/distribuição. Criar apenas o monitor necessário, com identidade estável quando apropriado.
5. **Selecionar captura:** o código atual captura o monitor principal; acrescentar escolha do monitor e origem da área virtual. Não tornar o virtual principal silenciosamente.
6. **Corrigir entrada:** converter coordenadas para o monitor selecionado, inclusive origens negativas. O `SendInput` atual e os limites da área virtual precisam ser revistos.
7. **Exibir 1:1:** preservar transmissão sem perda e ajustar apresentação pelo DPI. Tratar legibilidade/tamanho de fonte separadamente de nitidez.
8. **Redimensionar com controle:** acumular eventos enquanto o usuário arrasta a janela e enviar uma solicitação ao estabilizar. Se o driver exigir recriar o monitor, validar que não redistribui janelas físicas inesperadamente; não assumir continuidade.
9. **Trocar base/geração:** aguardar a resolução real confirmada, atualizar captura e iniciar imagem completa. Rejeitar pixels de uma configuração antiga.
10. **Cuidar da sessão:** desconexão/crash deve liberar recursos e impedir monitores órfãos. Definir o destino das aplicações que estavam na área virtual.
11. **Conservar Estável:** recurso opcional, com retorno ao acesso físico atual. Não habilitar Experimental antes de implementação funcional.

Não definir prazos/limiares de redimensionamento como se fossem resultados de benchmark. Começar com uma resolução na conexão; adaptar durante redimensionamento só depois de validar continuidade.

## 6. Testes para demonstrar eficácia

- Mesmo bitmap renderizado no virtual e recebido: comparação dos pixels em transmissão sem perda.
- Janela ADMIN em diferentes tamanhos e DPI 100%, 125%, 150%; monitor secundário à esquerda.
- Texto pequeno/colorido, linhas de um pixel, cursor de texto e planilha.
- Resolução física, monitor principal e aplicações locais antes/durante/depois.
- Movimentação e entrada na área virtual; disputa de foco com a pessoa local.
- Mudança de tamanho, reconexão, queda do agente e remoção de monitor.
- Bloqueio/UAC: não presumir que o desktop seguro aparece no monitor virtual.
- Tempos de renderização, captura, envio e aplicação; CPU/GPU e banda.

**Aceitação:** pixels exibidos 1:1 na área virtual, modo físico preservado e ciclo de sessão previsível. Independência de usuários não é um critério que um simples monitor extra possa satisfazer.

## 7. O que foi entregue nesta versão

A 1.2.1 acrescenta tela cheia opcional, por botão no rodapé ou Ctrl+Alt+F11, com saída disponível e restauração da janela. Mantém a escolha 100%, não muda a resolução remota e não torna a janela sempre no topo. F11 sozinho continua disponível para aplicativos remotos.

A implementação segue o procedimento Win32 de escolher o monitor, obter seus limites e posicionar a janela; preserva a posição nativa para restauração. [Posicionamento em múltiplos monitores](https://learn.microsoft.com/en-us/windows/win32/gdi/positioning-objects-on-multiple-display-monitors).

Não foram instalados drivers, criado monitor virtual ou aplicado ajuste automático de resolução. O relatório registra uma solução candidata e seus requisitos, sem apresentar o protótipo como concluído.
