# Validação

## Executado localmente

- Compilação Release sem erros/avisos; 190 testes aprovados (139 unitários, 30 de integração e 21 Windows).
- Framing, mensagens, estados, certificados e configuração.
- TLS mútuo real em loopback, pareamento, streaming e sessão única.
- Senha correta/incorreta/ausente e prova vinculada a nonce/certificados/modo.
- Rejeição de fator de trabalho excessivo e payloads IPC inválidos/truncados.
- Falha de entrada libera a vaga; rejeição não solta entradas de outra sessão.
- Rede local privada e rejeição de IP público/wildcard.
- DPAPI, manifesto requireAdministrator e estruturas Win32 x64.
- Renderização de temas claro/escuro sem abrir acesso remoto.
- Reconstrução sem perda, detecção de um pixel, lotes limitados, descompressão com tamanho exato e confirmação por índice.
- Entrada durante apresentação bloqueada em TLS real; primeira imagem entregue após criação do consumidor.
- Estados de consulta do atualizador, origem/hash de release e parâmetros de encerramento da versão antiga.
- Benchmark sintético de texto: 257.007 bytes em JPEG completo versus 1.375 bytes em blocos sem perda.
- Publicações autossuficientes, manifestos administrativos da interface/serviço, configuração de elevação do instalador e SHA-256 verificados.

TRX em tests/*/TestResults. Log final em artifacts/build.log.

## Aceitação ainda pendente

O ambiente de execução do desenvolvimento não tem token elevado do Windows. Não foram instalados serviços, certificados de máquina ou regras de firewall no PC do usuário.

- [ ] Instalar em dois PCs e confirmar serviço SYSTEM e ACLs.
- [ ] Conectar entre redes pelo Tailscale; testar queda/retorno.
- [ ] Conectar na rede local sem Tailscale, com perfil Privado.
- [ ] Confirmar recusa com perfil Público, senha errada e certificado desconhecido.
- [ ] Reiniciar e conectar antes do login.
- [ ] Bloquear/desbloquear, atravessar UAC e controlar aplicativos elevados.
- [ ] Confirmar indicador persistente, aviso sem UI e corte local.
- [ ] Trocar resolução/DPI; validar clique/arraste/scroll/teclado ABNT2.
- [ ] Parar/crashar serviço e conferir ausência de agente órfão.
- [ ] Atualizar/desinstalar e conferir remoção de serviço/tarefa/firewall.
- [ ] Validar troca rápida de usuário e sessão física.

GDI/blocos Brotli sem perda implementados; JPEG permanece para negociação com versões antigas. DXGI/H.264 e medições em redes reais não foram executados. Conteúdo protegido pode não ser capturado. Sem Ctrl+Alt+Del, áudio, arquivos, clipboard ou múltiplos monitores.

Instalador local sem assinatura; publicação da 1.1.0 acompanhada em Actions/Releases.