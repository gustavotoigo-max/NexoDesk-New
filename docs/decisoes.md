# Decisões e estado

- Produto: NexoDesk New, C#/.NET 10/WPF, instalador Windows x64 autossuficiente. Interface sempre elevada, inclusive na primeira abertura.
- ADMIN pareado por certificado individual e senha a cada conexão. PBKDF2-SHA256, 600.000 iterações, prova HMAC vinculada a nonce/certificados/modo. Código temporário pareia uma vez; não substitui a senha.
- Tailscale pela internet ou rede local privada com interface escolhida; sem relay próprio, escuta pública ou portas no roteador. Usuário confirmou funcionamento pelo Tailscale.
- Serviço SYSTEM supervisiona agente na sessão física; UI elevada separada. Job Object contém o agente. Configuração em ProgramData/NexoDesk-New e certificado LocalMachine com ACL restrita. Instalação separada da antiga, sem migração silenciosa.
- Preservar o monitor físico, usado simultaneamente por alguém no remoto. Sem alteração de resolução física/monitor virtual nesta entrega.
- Motor Estável: captura GDI, blocos alterados sem perda/Brotli e fila/ACK limitados; JPEG por negociação antiga. Experimental continua indisponível. Plano em docs/plano-transmissao.md; DXGI/H.264 depois.
- Aviso por dez segundos, fechamento manual sem desconectar; estado e encerramento permanecem na janela/bandeja.
- Atualizador consulta leve ao abrir/a cada minuto; faixa apenas para versão nova. Botão Atualizar permite consulta antecipada, instalação iniciada pelo usuário.
- Autorização permanente do usuário: publicar código e release de cada nova versão no GitHub, com instalador/SHA-256 após verificações, sem reconfirmar. Conferir workflow, assets e disponibilidade no atualizador.
- Saída explícita encerra UI/supervisor/agente desta instalação, preservando Tailscale e início no Windows. Instalador/atualizador limpa processos por caminho/PID; mantém segurança contra executáveis homônimos. ADMIN fecha pelo X; X do REMOTO recolhe à bandeja. Ambos têm ícone/indicador de conexão.
- Interface com menos cartões, raios discretos, campos ampliados e senha com olho.
- Tela cheia opcional foi confirmada pelo usuário. Teclado voltou a funcionar após correção de foco da 1.3.1; preservar essa proteção.
- 1.5.0: dois ADMINs veem a mesma tela; atividade nova transfere controle sem desconectar observadores. Atividade física também participa, sem bloquear hardware. Modo anterior de um ADMIN selecionável. Detalhes/limites em docs/acesso-simultaneo.md.
- 1.6.0: nitidez GPU de 0–100%, prévia imediata, padrão anterior 35%, restauração e preferência compartilhada/salva. Zero desliga reforço; botão 100% mantém pixels originais. Ajuste anterior permanece selecionável. Ferramenta de comparação mede fidelidade/contraste; conforto visual depende da avaliação nos PCs.

Versão atual 1.6.0. Compilação e 259 testes locais aprovados; validação física, login/UAC e uso prolongado entre PCs permanecem pendentes. Consulte docs/validacao.md. Sem áudio, arquivos, clipboard, múltiplos monitores ou Ctrl+Alt+Del sintetizado.
