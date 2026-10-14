# Validação

## Executado localmente

- Compilação Release sem erros/avisos; 227 testes aprovados na 1.4.1 (140 unitários, 30 de integração e 57 Windows).
- Framing, mensagens, estados, certificados e configuração.
- TLS mútuo real em loopback, pareamento, streaming e sessão única.
- Senha correta/incorreta/ausente e prova vinculada a nonce/certificados/modo.
- Rejeição de fator de trabalho excessivo e payloads IPC inválidos/truncados.
- Falha de entrada libera a vaga; rejeição não solta entradas de outra sessão.
- Rede local privada e rejeição de IP público/wildcard.
- DPAPI, manifesto requireAdministrator e estruturas Win32 x64.
- Renderização de temas claro/escuro e prévia das configurações sem abrir acesso remoto. A prévia é gerada sem janela nativa; temporizador e botões do aviso ainda exigem teste na sessão interativa real. Tentativas de validar o aviso com janela/loop de mensagens travaram no ambiente de teste; não repetir esse método fora de uma sessão interativa.
- Template real da moldura: título e rodapé preservados no modo normal e ocultos em tela cheia. O usuário confirmou funcionamento perfeito da tela cheia no seu teste; não especificou cobertura de atalhos, restauração, DPI misto ou monitores secundários.
- Reconstrução sem perda, detecção de um pixel, lotes limitados, descompressão com tamanho exato e confirmação por índice.
- Entrada durante apresentação bloqueada em TLS real; primeira imagem entregue após criação do consumidor.
- Estados de consulta do atualizador, origem/hash de release, rejeição de drafts/pré-releases, consulta manual antes da instalação, cliques repetidos, cancelamento e parâmetros de encerramento da versão antiga.
- Consulta HEAD real do último release: HTTP 302 sem corpo. Regressões: versão atual dispensa API, metadados em cache, invalidação por tag e falhas sem falso aviso.
- Processo descartável no caminho do serviço: término do órfão e preservação de outro executável com o mesmo nome em outra pasta. Teste não encerra serviço instalado real.
- Diálogos de pareamento/senha renderizados nos dois temas; largura/altura dos campos, revelação/edição/ocultação/limpeza de senha e faixa oculta/visível verificados sem HWND.
- Benchmark sintético de texto: 257.007 bytes em JPEG completo versus 1.375 bytes em blocos sem perda.
- Regressão de foco: evento roteado real do WPF reproduz a tentativa do ScrollViewer de tomar foco, e o clique protegido impede que chegue ao contêiner. Validação automatizada sem janela nativa; usuário confirmou que o teclado voltou a funcionar no cenário testado.
- Efeitos Direct2D nativos em WARP: cor/alpha opacos, alteração de um pixel, troca de resolução, invalidação de cache e buffers inválidos.
- Ajuste com DPI 100%, 125% e 150%, mapeamento do mouse, atualização do bitmap original e fallback por falha de GPU.
- Comparação visual sintética de Fant/cúbico/cúbico com nitidez em 50%, 66,7% e 75%; superfície D3D9Ex/D3D11 compartilhada funcionando na GPU local. [Detalhes e procedimento](ajuste-imagem.md).
- Publicações autossuficientes, manifestos administrativos da interface/serviço, configuração de elevação do instalador e SHA-256 verificados.

CI inicialmente encontrou uma corrida no teste de mouse/teclado: fechar o TLS logo após enviar não confirma processamento remoto. O fixture agora sinaliza a entrada recebida antes do encerramento, mantendo a verificação de ordem e liberação de teclas; código de sessão/transmissão preservado.

TRX em tests/*/TestResults. Log final da entrega em artifacts/build-1.4.1.log.

## Confirmado pelo usuário

- [x] Problema de teclado resolvido após a correção de foco, conforme relato do usuário.

- [x] Tela cheia da 1.2.1 funcionando perfeitamente no cenário testado, conforme relato do usuário.

## Aceitação ainda pendente

O ambiente de execução do desenvolvimento não tem token elevado do Windows. Não foram instalados serviços, certificados de máquina ou regras de firewall no PC do usuário.

- [ ] ADMIN: confirmar ícone na bandeja sem conexão, em modo Ver a tela/Controlar, em mais de um remoto, após queda/desconexão e com janela de sessão encerrada ainda aberta. Conferir duplo clique, Encerrar todas as conexões e Sair; ícone deve ser removido na saída.
- [ ] Confirmar primeira abertura elevada pelo instalador sem erro.
- [ ] Confirmar consulta a cada minuto e atualização manual; faixa somente para versão superior.
- [ ] Sair pela bandeja e conferir ausência de interface/supervisor/agente, mantendo Tailscale. Reabrir e repetir após atualização com sessão ativa.
- [ ] Conferir campos de senha/pareamento e olhos em DPI 100%, 125% e 150%.
- [ ] Instalar em dois PCs e confirmar serviço SYSTEM e ACLs.
- [ ] Conectar entre redes pelo Tailscale; testar queda/retorno.
- [ ] Conectar na rede local sem Tailscale, com perfil Privado.
- [ ] Confirmar recusa com perfil Público, senha errada e certificado desconhecido.
- [ ] Reiniciar e conectar antes do login.
- [ ] Bloquear/desbloquear, atravessar UAC e controlar aplicativos elevados.
- [ ] Confirmar aviso de dez segundos, Fechar aviso sem desconectar, estado na bandeja, aviso sem UI e corte local.
- [ ] Cobertura adicional de teclado: alternar Nitidez/100%, entrar/sair de tela cheia, Alt+Tab, teclas de navegação e atalhos no remoto.
- [ ] Comparar ajuste nítido/anterior em texto, planilhas e movimento nos PCs; redimensionar, mudar DPI/monitor, minimizar/restaurar e alternar 100%.
- [ ] Cobertura adicional de tela cheia: restauração da janela, Alt+Tab, Ctrl+Alt+F11 e F11 remoto, DPI misto e monitor secundário.
- [ ] Trocar resolução/DPI; validar clique/arraste/scroll/teclado ABNT2.
- [ ] Parar/crashar serviço e conferir ausência de agente órfão.
- [ ] Atualizar/desinstalar e conferir remoção de serviço/tarefa/firewall.
- [ ] Validar troca rápida de usuário e sessão física.

GDI/blocos Brotli sem perda implementados; JPEG permanece para negociação com versões antigas. DXGI/H.264 e medições em redes reais não foram executados. Conteúdo protegido pode não ser capturado. Sem Ctrl+Alt+Del, áudio, arquivos, clipboard ou múltiplos monitores.

Instalador 1.4.1 sem assinatura, com runtime e SHA-256 verificados; publicação no GitHub pelo workflow de compilação, testes e release. Instaladores 1.1.0, 1.2.0 e 1.2.1 preservados. Novo motor experimental não implementado nesta entrega.