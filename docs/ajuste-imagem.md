# Ajuste da imagem em janela — 1.3.0

## Uso

Desmarque **100%** e mantenha **Nitidez** ligada na barra da sessão. A imagem se ajusta automaticamente à área útil da janela e ao DPI do ADMIN; o monitor físico remoto mantém sua resolução. O rodapé informa o ajuste efetivo.

Desligar Nitidez usa o filtro Fant anterior. **Configurações > Imagem em janela** e o botão da sessão salvam a preferência para novas sessões. 100% sempre apresenta os pixels originais, sem filtros. O novo motor experimental permanece indisponível.

## Implementação

- Bitmap original e captura/transmissão GDI/Brotli conservados. O novo processamento ocorre somente na apresentação do ADMIN.
- Direct2D Scale em HighQualityCubic, seguido de Sharpen com intensidade 0,35 e limiar 0,15. São parâmetros iniciais moderados, sujeitos à avaliação visual.
- Vortice.Windows 3.8.3 reutiliza as APIs nativas do Windows. D3D9Ex e D3D11 usam a mesma GPU, identificada por LUID, e compartilham a textura de saída com um único D3DImage do WPF. A produção não lê a saída de volta para a CPU.
- Carga completa somente ao iniciar/recriar a imagem ou reativar o filtro; depois, cada bloco recebido é copiado e invalida a região de entrada dos efeitos. O byte reservado de BGR32 vira alpha opaco apenas no buffer do filtro, preservando os pixels do presenter original.
- Renderização coalescida no compositor, com TryLock sem espera. Todos os blocos chegam à imagem acumulada antes da confirmação, embora repinturas possam ser reunidas. Não há fila de quadros filtrados.
- Tamanho de saída e posição são calculados em pixels físicos. Clique e cursor usam exatamente a mesma área, incluindo faixas vazias e rolagem em 100%.
- Falha de GPU/efeito retoma o bitmap original e informa o fallback no rodapé/log. Minimização/perda do front buffer usa a imagem original; quando ele retorna, a superfície é reassociada. Desligar/ligar Nitidez permite nova tentativa.
- Dependências e avisos MIT incluídos no instalador em ThirdPartyNotices.txt. Não instala driver nem muda resolução, rede ou serviço de captura.

## Comparação e verificações

217 testes aprovados na 1.3.1: incluem efeitos Direct2D reais em WARP (renderizador por CPU para testes), cores/alpha, alteração de um pixel sem recriar a base, troca de dimensões, invalidação, dados inválidos, configuração antiga, DPI e fallback. O caminho original é renderizado em bitmap sem abrir janela nativa.

A ferramenta abaixo gera o mesmo quadro sintético com texto, valores monetários, cores e linhas, nas reduções de 50%, 66,7% e 75%. Exporta Fant, cúbico e cúbico com nitidez, além de testar a textura compartilhada na GPU local:

```powershell
dotnet run --project tools/NexoDesk.ScaleComparison/NexoDesk.ScaleComparison.csproj -c Release -- artifacts/scale-comparison
```

Abra artifacts/scale-comparison/index.html e compare em 100% de zoom. O relatório está em comparison.json. A inspeção local encontrou maior contraste das letras/linhas com o filtro; isso é avaliação visual desse quadro, não garantia para qualquer texto.

Duas medições locais de envio de um bloco 128×128 e submissão de desenho para saída 1280×720 ficaram em cerca de 0,32–0,40 ms em média. Ela **não mede conclusão da GPU, compositor WPF, RTT ou latência entre PCs**. WARP com leitura de diagnóstico ficou aproximadamente entre 12 e 28 ms; leitura GPU→CPU existe somente nessa ferramenta/testes. Não usar esses números para prometer FPS do acesso remoto.

## Aceitação nos PCs

Comparar Nitidez ligada/desligada em texto pequeno, planilhas, pontos/vírgulas, fundos claros/escuros e movimento. Verificar clique/cursor após redimensionar, DPI 125%/150%, monitor secundário, minimizar/restaurar, tela cheia e 100%. Se houver halos ou custo excessivo, desligar Nitidez conserva o acesso anterior.

A resolução maior continua sendo reduzida para caber: não há igualdade geral a 100%. Monitor virtual/resolução dinâmica remota permanecem em pesquisa.

## Correção de teclado — 1.3.1

O envio de teclas exige foco na imagem. O ScrollViewer do WPF tenta focar a si mesmo ao receber o clique esquerdo; um botão de ajuste também recebe foco ao ser acionado. A 1.3.1 impede o contêiner de focar, marca o clique remoto como tratado e devolve foco à imagem após Nitidez/100%. A roda enviada ao remoto não rola simultaneamente o viewport local.

Um teste de evento roteado reproduz o caminho desprotegido e confirma que o clique protegido não chega ao contêiner, sem abrir janela nativa. O transporte de teclado permanece preservado. Confirmação de digitação em sessão real ainda depende do teste nos PCs. [Implementação oficial do ScrollViewer](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/ScrollViewer.cs).

## Fontes

- [Scale de alta qualidade](https://learn.microsoft.com/en-us/windows/win32/direct2d/high-quality-scale) e [Sharpen](https://learn.microsoft.com/en-us/windows/win32/direct2d/sharpen-effect).
- [WPF/Direct3D9: compartilhamento e flags de criação](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/performance-considerations-for-direct3d9-and-wpf-interoperability).
- [OpenSharedResource e Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-opensharedresource).
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), APIs incluídas sob licença MIT.
