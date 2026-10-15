# Protocolo

TCP privado com TLS mútuo e pin SHA-256. Header little-endian de 16 bytes: magic NXDK (4), versão (2), tipo (2), comprimento (4) e sequência (4). Controles JSON até 64 KiB; vídeo até 8 MiB. Leitura exata e validação de estado, tipos, campos e timeouts.

1. Hello informa versão, clientId e capacidades.
2. HelloAck informa identidade/recursos e PasswordChallenge (salt, iterations, nonce).
3. ADMIN deriva a chave e envia StartSession com mode/passwordProof.
4. Servidor verifica prova, allowlist atual e política.
5. SessionAccepted inicia vídeo/entrada; SessionDenied informa motivo seguro.

Transcript: NexoDesk-password-v1|nonce|fingerprintCliente|fingerprintServidor|modo. HMAC-SHA256/base64 de 32 bytes. Nonce novo por conexão. O worker sempre exige PasswordProvider: verificador ausente é recusa. Núcleo sem provider existe para reutilização/testes.

Framing conserva versão 1; campos novos são opcionais na desserialização, mas exigidos pela política do produto. Não se promete compatibilidade operacional com a versão anterior. Use NexoDesk New nos dois lados.

DisplayInfo informa dimensões/DPI. VideoFrame: índice uint32, timestamp int64, largura/altura uint16 e JPEG. CursorUpdate é independente. PointerEvent usa coordenadas normalizadas. KeyEvent usa scan code, tecla virtual, extended e estado. Ping/Pong verifica conexão/latência. EndSession encerra ordenadamente.

Desconexão libera entradas apenas de sessão autorizada e devolve a vaga, inclusive em exceções. Estados: Disconnected → Connecting → Authenticated → Negotiating → Streaming → Closing → Disconnected. PairRequest/PairResult pareiam dentro do TLS; não autorizam streaming.

IPC local: comprimento int32 e JSON até 64 KiB. Comandos status, pair, cancelPair, end, reload e shutdown. Pipe com ACL de SYSTEM/administradores, verificação do servidor e rejeição de rede.
## Tela diferencial (1.1.0)

Recurso desktop-tiles-v1 negociado em Hello/HelloAck. Tipos 17 DesktopUpdate (binário) e 18 FrameAck (JSON). Sem negociação, permanece VideoFrame JPEG.

DesktopUpdate: índice u32, timestamp i64, largura/altura u16, flags u8 (Reset=1, Final=2), reservado u8=0, quantidade de blocos u16. Cada bloco: x/y/largura/altura u16, comprimento i32 e pixels BGR32 comprimidos com Brotli.

Limites: 192 KiB por mensagem, 64 blocos, bloco de até 128×128, dimensão até 8192 e área até 32 Mi pixels. Retângulos devem estar contidos na tela. Descompressão deve produzir exatamente largura×altura×4 bytes; conteúdo truncado/excedente é rejeitado. A primeira atualização inicia uma base; deltas sem base não são aceitos pelo apresentador.

O ADMIN confirma o índice após aplicar o lote. O REMOTO mantém um lote em trânsito e encerra após 15 s sem confirmação. Entrada continua sendo processada durante a espera. Final conta uma captura completa no indicador de FPS. Reset ocorre no início, ao mudar resolução e ao mudar desktop. TCP/TLS preserva ordem; deltas não são descartados no receptor.
## Controle por atividade (1.5.0)

Capacidade opcional `activity-control-v1` em ambos os Hello/HelloAck. Tipo 19 `ControlState`, JSON com `epoch` monotônico, `ownerId` e `ownerName`. Dono é o sessionId autenticado, `local` ou `none`. O agente publica somente mudanças, com consulta interna a cada 100 ms; o ADMIN ignora estados antigos. Não há mensagem de pedido: entrada nova de uma sessão autorizada para Controlar transfere a posse no agente. A permissão de enviar permanece ativa para permitir recuperação por atividade. Observação não adquire controle.

A indicação de posse é negociada; a arbitragem é sempre aplicada no remoto quando Acesso simultâneo está habilitado, inclusive para clientes antigos. No modo anterior, o agente aceita um ADMIN e não anuncia essa capacidade. Versão de framing permanece 1; tipo 19 nunca é enviado sem negociação. Cada sessão conserva seu próprio FrameAck/histórico de blocos. Desconexão não libera entrada de outra sessão.
