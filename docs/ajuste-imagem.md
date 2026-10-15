# Ajuste da imagem em janela — 1.6.0

## Uso

Desmarque o botão **100%** para ajustar à janela. Clique em **Nitidez** e mova a barra de **0–100%**, com prévia imediata. **Concluir**, Esc ou clicar fora fecha o painel; o foco de teclado volta à imagem. O monitor remoto mantém sua resolução.

- **0% de nitidez:** redução cúbica sem reforço adicional; não desliga o redimensionamento.
- **35%:** padrão anterior. **Restaurar padrão (35%)** também reativa a melhoria.
- **100% de nitidez:** máximo do filtro, sem promessa de fidelidade/legibilidade perfeitas. Prefira o menor valor que deixe as letras confortáveis, evitando contornos artificiais.
- Desmarcar **Melhorar nitidez ao ajustar à janela** usa Fant, o ajuste original.
- O botão **100%** da barra significa tamanho real e preserva os pixels originais, sem filtros, independentemente da intensidade escolhida.

**Configurações → Imagem em janela** usa a mesma preferência. Todas as sessões abertas do ADMIN atualizam juntas. A prévia não grava a cada movimento: há gravação após 300 ms de pausa e ao fechar o painel, a sessão, as configurações ou sair/atualizar. Configurações antigas recebem 35% sem mudar sua escolha de melhoria ligada/desligada.

O rodapé informa GPU, intensidade e escala efetiva quando o filtro está ativo. Escala inferior a 100% indica que mais pixels remotos estão sendo acomodados na área útil local. DPI e barras da janela fazem diferença mesmo com monitores de mesma resolução. Não foi confirmada a causa exata da diferença percebida em relação ao TeamViewer.

## Implementação

- Bitmap original e captura/transmissão GDI/Brotli conservados. O novo processamento ocorre somente na apresentação do ADMIN.
- Direct2D Scale em HighQualityCubic, seguido de Sharpen com intensidade ajustável de 0 a 1 (padrão 0,35) e limiar 0,15. Alterar a intensidade reutiliza texturas e pixels recebidos; não reinicia a conexão/captura.
- Vortice.Windows 3.8.3 reutiliza as APIs nativas do Windows. D3D9Ex e D3D11 usam a mesma GPU, identificada por LUID, e compartilham a textura de saída com um único D3DImage do WPF. A produção não lê a saída de volta para a CPU.
- Carga completa somente ao iniciar/recriar a imagem ou reativar o filtro; depois, cada bloco recebido é copiado e invalida a região de entrada dos efeitos. O byte reservado de BGR32 vira alpha opaco apenas no buffer do filtro, preservando os pixels do presenter original.
- Renderização coalescida no compositor, com TryLock sem espera. Todos os blocos chegam à imagem acumulada antes da confirmação, embora repinturas possam ser reunidas. Não há fila de quadros filtrados.
- Tamanho de saída e posição são calculados em pixels físicos. Clique e cursor usam exatamente a mesma área, incluindo faixas vazias e rolagem em 100%.
- Falha de GPU/efeito retoma o bitmap original e informa o fallback no rodapé/log. Minimização/perda do front buffer usa a imagem original; quando ele retorna, a superfície é reassociada. Desmarcar/marcar a melhoria permite nova tentativa; mover a intensidade não repete uma falha gráfica a cada passo.
- Dependências e avisos MIT incluídos no instalador em ThirdPartyNotices.txt. Não instala driver nem muda resolução, rede ou serviço de captura.

## Comparação e verificações

217 testes aprovados na 1.3.1: incluem efeitos Direct2D reais em WARP (renderizador por CPU para testes), cores/alpha, alteração de um pixel sem recriar a base, troca de dimensões, invalidação, dados inválidos, configuração antiga, DPI e fallback. O caminho original é renderizado em bitmap sem abrir janela nativa.

A ferramenta abaixo gera o mesmo quadro sintético com texto, valores monetários, cores e linhas, nas reduções de 50%, 66,7%, 75% e 90%. Exporta Fant, cúbico e cúbico com nitidez, além de testar a textura compartilhada na GPU local:

```powershell
dotnet run --project tools/NexoDesk.ScaleComparison/NexoDesk.ScaleComparison.csproj -c Release -- artifacts/scale-comparison-1.6.0
```

Abra artifacts/scale-comparison-1.6.0/index.html em 100% de zoom. Compare intensidades 0, 20, 35, 50, 65, 80 e 100. Para usar uma captura PNG local, acrescente o caminho como segundo argumento após a pasta de saída; ela é processada localmente.

O comparison.json inclui reconstrução dos blocos antes dos filtros (erro RGB zero e SSIM 1 no quadro sintético), contraste médio de bordas, SSIM de luminância em blocos 8×8 e porcentagem de pixels que excedem o intervalo local da referência. Alpha é ignorado. Nos tamanhos reduzidos, a referência é cúbico sem reforço no mesmo tamanho; SSIM maior significa semelhança com essa referência, não automaticamente melhor leitura. Exceder o intervalo local pode indicar contorno artificial, mas não é detector definitivo de halos. Nenhuma dessas medidas certifica conforto visual ou equivalência ao TeamViewer.

A ferramenta confirmou apresentação pela GPU local na 1.6.0. Testes nativos em WARP confirmam que 0/35/100 produzem saídas distintas e voltar a 35 reproduz os mesmos pixels, sem alterar a fonte. São 259 testes na solução (160 núcleo, 37 integração, 62 Windows). A validação visual prolongada nos PCs continua pendente.

Duas medições locais de envio de um bloco 128×128 e submissão de desenho para saída 1280×720 ficaram em cerca de 0,32–0,40 ms em média. Ela **não mede conclusão da GPU, compositor WPF, RTT ou latência entre PCs**. WARP com leitura de diagnóstico ficou aproximadamente entre 12 e 28 ms; leitura GPU→CPU existe somente nessa ferramenta/testes. Não usar esses números para prometer FPS do acesso remoto.

## Aceitação nos PCs

Comparar 0/35/50/65/80/100 e melhoria ligada/desligada em texto pequeno, planilhas, pontos/vírgulas, fundos claros/escuros e movimento. Verificar clique/cursor após redimensionar, DPI 125%/150%, monitor secundário, minimizar/restaurar, tela cheia e 100%. Se houver halos ou custo excessivo, desligar Nitidez conserva o acesso anterior.

A resolução maior continua sendo reduzida para caber: não há igualdade geral a 100%. Monitor virtual/resolução dinâmica remota permanecem em pesquisa.

## Correção de teclado — 1.3.1

O envio de teclas exige foco na imagem. O ScrollViewer do WPF tenta focar a si mesmo ao receber o clique esquerdo; um botão de ajuste também recebe foco ao ser acionado. A 1.3.1 impede o contêiner de focar, marca o clique remoto como tratado e devolve foco à imagem após Nitidez/100%. A roda enviada ao remoto não rola simultaneamente o viewport local.

Um teste de evento roteado reproduz o caminho desprotegido e confirma que o clique protegido não chega ao contêiner, sem abrir janela nativa. O transporte de teclado permanece preservado. Usuário confirmou que o problema de teclado foi resolvido após a correção; cenários adicionais de atalhos e troca de foco continuam pendentes. [Implementação oficial do ScrollViewer](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/ScrollViewer.cs).

## Fontes

- [Scale de alta qualidade](https://learn.microsoft.com/en-us/windows/win32/direct2d/high-quality-scale) e [Sharpen](https://learn.microsoft.com/en-us/windows/win32/direct2d/sharpen-effect).
- [WPF/Direct3D9: compartilhamento e flags de criação](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/performance-considerations-for-direct3d9-and-wpf-interoperability).
- [OpenSharedResource e Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-opensharedresource).
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), APIs incluídas sob licença MIT.
