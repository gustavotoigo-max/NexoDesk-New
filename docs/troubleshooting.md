# Diagnóstico

| Sintoma | Ação |
|---|---|
| Serviço não responde | Instale pelo Setup; consulte sc.exe query NexoDeskRemote em PowerShell elevado |
| Nenhum IP Tailscale | Conecte o Tailscale e tente novamente |
| Vários IPs locais | Selecione o IP da interface usada |
| Rede local bloqueada | Confirme perfil Privado e ausência de isolamento Wi-Fi |
| Senha recusada | Conecte novamente e informe a senha atual |
| Identidade mudou | Confirme reinstalação/rotação e refaça pareamento |
| Código expirou | Gere outro; válido uma vez por dez minutos |
| Segunda sessão recusada | Encerre a primeira |
| Imagem indisponível | Confira sessão física, políticas e service-errors.log |
| Login exige Ctrl+Alt+Del | Comando não sintetizado; valide o login da empresa |
| Atualização não aparece | Release nova precisa de Setup e digest SHA-256 |
| UI não inicia com Windows | Verifique a tarefa NexoDesk-New.Interface da conta administrativa configurada |

Logs: %ProgramData%\NexoDesk-New\logs. Falhas de criação do agente: service-errors.log na pasta principal. Não compartilhe config.json sem remover verificadores e segredos.

Execute scripts/Test-Prerequisites.ps1 para conferir dependências. Setup inclui .NET; SDK é necessário apenas para compilar.