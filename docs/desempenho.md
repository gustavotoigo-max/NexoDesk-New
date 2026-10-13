# Desempenho e diagnóstico

A 1.1.0 envia pixels BGR32 sem perda apenas nos blocos de 128×128 que mudaram. Cada lote tem até 64 blocos e 192 KiB. O ADMIN confirma após a apresentação; o REMOTO não acumula imagens para entregar depois. Compressão ocorre fora da thread de entrada. Uma tela parada não precisa consumir vídeo continuamente.

## Como comparar

1. Instale 1.1.0 em ambos os PCs e confirme a versão no rodapé.
2. Use 100% para verificar texto sem redução; use ajuste à janela quando preferir ver a tela inteira.
3. Observe latência, FPS e Mbit/s no cabeçalho. Mbit/s mede payload do aplicativo, sem overhead de TLS/WireGuard. FPS conta capturas concluídas, não lotes.
4. Compare a mesma tarefa na rede local e no Tailscale, com a mesma resolução.
5. Para a rota da empresa, execute:

       & 'C:\Program Files\Tailscale\tailscale.exe' ping --c=3 --timeout=2s ENDERECO_DO_REMOTO

   “via IP:porta” indica rota direta; “via DERP(...)” indica servidor intermediário. O campo Relay de status --json, isoladamente, não prova que a conexão está passando pelo relay.

Teste desta entrega: LAB-18 respondeu por rota direta em aproximadamente 25 ms. Isso mede a rota Tailscale naquele momento, não o atraso completo da captura até a apresentação.

## Limites

Qualidade de texto deixou de depender de JPEG quando ambos os lados usam 1.1.0. A imagem reduzida à janela ainda sofre escala; 100% preserva os pixels. Upload, perda de pacotes, resolução, CPU e rota física continuam afetando atraso. A primeira imagem e mudanças grandes precisam transmitir mais dados.

Brotli por blocos não substitui H.264/AV1 acelerados. Vídeo, jogos e imagens inteiramente em movimento podem ter FPS baixo ou alto uso de banda, sobretudo em 4K. Captura permanece GDI, com um monitor e imagem SDR de 8 bits.

No benchmark sintético, trocar uma linha de texto em 1920×1080 exigiu 1.375 bytes, comparado a 257.007 bytes de JPEG da tela inteira (99,46% menos payload). Os pixels reconstruídos foram comparados com o original. Resultado em artifacts/performance-synthetic.json; não é benchmark entre PCs nem garantia para outras imagens.

## Tailscale e TeamViewer

Tailscale tenta conexão direta e usa DERP quando isso não é possível. DERP costuma ter desempenho inferior. Trocar somente o plano não é garantia de ganho de velocidade; os planos se diferenciam principalmente em gestão, segurança e suporte.

Se DERP for frequente, prefira corrigir conectividade direta. Peer Relays permitem usar infraestrutura própria e estão disponíveis inclusive em plano gratuito; exigem um nó adequado e configuração. Nenhum relay foi configurado nesta entrega.

TeamViewer também escolhe entre conexões diretas e servidores intermediários. Sua infraestrutura é parte do desempenho; as otimizações do software de captura/transmissão/apresentação também importam. A rota direta observada favorece corrigir o aplicativo antes de atribuir este caso ao plano Tailscale.

Fontes primárias:
- [Desempenho Tailscale](https://tailscale.com/docs/reference/troubleshooting/poor-performance-tailnet).
- [Tipos de conexão](https://tailscale.com/docs/reference/connection-types).
- [Peer Relays em todos os planos](https://tailscale.com/blog/peer-relays-beta).
- [Planos](https://tailscale.com/pricing).
- [Roteamento TeamViewer](https://www.teamviewer.com/en-cis/insights/teamviewer-remote-access-not-working/).