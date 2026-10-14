# Plano de implementação da transmissão experimental

## Decisões confirmadas

- Preservar o motor da 1.1.0: GDI, blocos BGR32 de até 128×128, Brotli, confirmação por lote e cursor separado.
- Manter **Estável** como padrão, inclusive em configurações antigas.
- Nesta entrega 1.2.0, preparar seletor e plano. **O novo motor será implementado depois**; Experimental permanece indisponível.
- Continuar com C#/.NET 10, WPF, serviço/agente e transporte privado existente.
- Base técnica: [pesquisa completa](pesquisa-teamviewer.md). As escolhas futuras não dependem de reproduzir detalhes não documentados do TeamViewer.

## Prioridade complementar: nitidez em janela

O usuário confirmou que alguém usa o monitor físico do PC REMOTO simultaneamente. Preservar sua resolução e área de trabalho. A [pesquisa de resolução](pesquisa-resolucao.md) compara escala, monitor virtual e sessão separada.

Para compartilhar a mesma tela física, comparar primeiro escala cúbica Direct2D e nitidez moderada com o Fant atual, sem prometer igualdade a 100%. Antes de implementar adaptação automática, decidir se a área extra de um monitor virtual atende ao uso pretendido. Ela não é espelho idêntico da tela física nem cria foco/teclado independentes. Prototipar captura de monitor selecionado e pixels 1:1 sem alterar o Estável; só então avaliar driver e ciclo de resolução. A 1.2.1 inclui apenas tela cheia opcional.

## Proteção da versão funcional

A 1.2.0 conserva os caminhos de captura e entrega atuais. O instalador 1.1.0 tem nome próprio e não será sobrescrito. O campo de configuração de transmissão é opcional: omissão seleciona Estável; solicitação de Experimental nesta entrega também resolve para Estável.

Na implementação futura:

1. Criar um ponto explícito de seleção de motor no agente, conservando a implementação estável.
2. Negociar recursos novos por sessão; nenhum recurso experimental deve ser exigido de clientes antigos.
3. Alterar o modo nas configurações do PC REMOTO, com efeito na próxima conexão. A sessão corrente mantém o motor com que foi iniciada.
4. Habilitar Experimental apenas quando o primeiro motor funcional e seus testes estiverem prontos.
5. Informar modo efetivo e fallback no diagnóstico; não indicar experimental quando a sessão usa Estável.
6. Persistir a escolha e permitir voltar a Estável pela mesma configuração.
7. Manter acesso, pareamento, senha, Tailscale e serviço independentes da escolha de transmissão.

Um novo motor indisponível não deve impedir o início do aplicativo nem desativar o modo estável.

## Etapa 1 — medição e comparação

**Entrega:** instrumentação e procedimento reproduzível, sem alterar o desempenho funcional do Estável.

- Medir captura, comparação, compressão, envio, espera por ACK, descompressão e aplicação.
- Registrar bytes, número de lotes, idade da imagem, CPU/GPU, FPS de capturas completas e RTT.
- Usar relógio monotônico nos tempos locais. Não subtrair timestamps de PCs diferentes como se estivessem sincronizados.
- Comparar LAN e Tailscale, identificando conexão direta ou relay.
- Repetir tarefas nos mesmos PCs, resolução, DPI, escala e qualidade: texto pequeno/colorido, planilha, rolagem, janela arrastada, vídeo e tela parada.
- Registrar distribuição dos tempos, não apenas médias; usar vídeo de referência quando necessário para atraso visual.

**Aceitação:** relatório permite atribuir atraso às etapas e reproduzir a linha de base. Metas numéricas de qualidade/latência serão definidas após essa medição.

## Etapa 2 — entrega experimental com fila limitada

**Entrega:** primeira opção Experimental funcional, mantendo a captura GDI e os pixels sem perda.

- Substituir a espera serial experimental por janela pequena de lotes/bytes em trânsito.
- Preservar ordem e referências; usar confirmações e limites de idade/ocupação.
- Coalescer apenas quando for possível reconstruir o estado correto. Nunca esquecer deltas pendentes.
- Manter entrada/cursor responsivos e estabelecer timeout de confirmação.
- Reinicializar a base em reconexão, troca de desktop ou resolução.
- Implementar esse caminho separadamente; não mudar a política do Estável.
- A versão que entrega essa etapa habilita o seletor. A escolha deve ter efeito na próxima sessão.

**Testes:** múltiplos lotes com RTT controlado; ACK atrasado/duplicado/incorreto; timeout; desconexão; um pixel persistente; resolução; reconstrução exata; não crescimento ilimitado da fila.

**Aceitação:** integridade dos pixels igual à base, fila limitada e ganho demonstrado nos cenários em que o ACK serial é gargalo. Sem melhora medida, manter a etapa em avaliação.

## Etapa 3 — captura DXGI

**Entrega:** captura experimental por Desktop Duplication, inicialmente mantendo o envio sem perda.

- Avaliar Vortice.Direct3D11 e Vortice.DXGI com versões coerentes.
- Capturar texturas e regiões modificadas/movidas; aplicar movimentações antes dos pixels novos.
- Lidar com agrupamento de regiões e acumulação enquanto o consumidor está ocupado.
- Minimizar cópias GPU→CPU; evitar capturar/comparar tudo quando os metadados forem suficientes.
- Tratar GPU híbrida, resolução/rotação, perda de acesso e recriação de recursos.
- Validar bloqueio, login e UAC no agente SYSTEM da sessão física.
- Usar GDI como fallback explícito quando a captura acelerada não estiver disponível.

**Aceitação:** estado reconstruído correto, compatibilidade dos PCs alvo e benefício mensurável de CPU/latência. Falha de DXGI deve permitir retorno ao caminho funcional.

## Etapa 4 — H.264 por hardware

**Entrega:** vídeo experimental via Media Foundation/Vortice, complementar ao envio sem perda.

- Enumerar e identificar encoder/decoder efetivos. Não presumir hardware pela escolha de H.264.
- Negociar dimensões, perfil e formato de cor; avaliar conversão e buffers.
- Configurar baixa latência, limites de banda, fila e estratégia de quadros independentes.
- Descartar capturas antes da codificação quando necessário; não descartar referências codificadas arbitrariamente.
- Preparar apresentação WPF de vídeo sem cópias evitáveis; continuar enviando cursor separadamente.
- Preservar o modo sem perda para texto e para restauração de nitidez.
- Começar alternando a tela completa, com geração/modo/base explícitos. Composição simultânea por região fica para depois.

**Testes:** texto colorido pequeno, movimento, retorno ao estado parado, encoder indisponível, CPU/GPU diferentes, banda/perda/RTT, troca de resolução, reconexão e bloqueio.

**Aceitação:** movimento melhora sem deixar pixels antigos ou impedir convergência da imagem parada para o modo sem perda. Não prometer igualdade byte a byte durante vídeo com perda.

## Etapa 5 — adaptação e preparação de release

- Selecionar modo por frequência/área de mudança, banda, tempo de processamento e idade da fila.
- Definir histerese e parâmetros a partir dos dados.
- Impedir vídeo antigo de sobrescrever uma restauração sem perda.
- Conferir licenças e dependências realmente distribuídas.
- Executar regressão de acesso e testes de ambos os motores.
- Gerar instalador separado, notas de versão e matriz de comparação.
- Manter Estável como padrão até aceitação em duas máquinas e decisão posterior explícita.

## Versionamento e execução futura

Cada entrega funcional aumenta Directory.Build.props e recebe docs/releases/X.Y.Z.md, instalador e SHA-256 próprios. Pré-releases de laboratório não devem ser instalados pelo atualizador estável. Publicação somente após o trabalho ser testado e autorizado; não sobrescrever assets de releases existentes.

Iniciar a implementação em branch de trabalho, por exemplo codex/transmissao-experimental. A criação da branch e as etapas acima ficam para a próxima tarefa de implementação.

Checklist atual:

- [x] Pesquisa salva.
- [x] Modo atual preservado.
- [x] Configuração/seletor preparados, sem ativar motor inexistente.
- [x] Aviso temporário e atualização manual na janela principal.
- [ ] Medição entre dois PCs.
- [ ] Motor experimental de fila limitada.
- [ ] DXGI com fallback.
- [ ] H.264 e apresentação.
- [ ] Adaptação, matriz de testes e aceitação.

Pendências de campo: PCs/GPU/resoluções para a comparação, testes reais de login/UAC e definição de metas após a linha de base. Nenhuma dessas etapas foi apresentada como concluída.
