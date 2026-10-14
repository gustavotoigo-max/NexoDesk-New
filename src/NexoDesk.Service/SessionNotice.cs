using System.Diagnostics;
using System.Runtime.InteropServices;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Session;

namespace NexoDesk.Service;

/// <summary>O Windows mostra um aviso local se a interface da bandeja não estiver disponível.</summary>
internal static class SessionNotice
{
    public static async Task WatchAsync(AgentSession session)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        while (session.StateMachine.State == SessionState.Streaming)
        {
            bool interfaceRunning = false;
            foreach (Process process in Process.GetProcessesByName("NexoDesk New"))
            {
                using (process)
                {
                    try { if (process.SessionId == sessionId) interfaceRunning = true; }
                    catch (InvalidOperationException) { }
                }
            }
            if (!interfaceRunning)
            {
                string title = "NexoDesk New — acesso remoto ativo";
                string message = $"ADMIN {session.ClientName} está acessando este computador.\nOK fecha este aviso. Cancelar encerra o acesso. O aviso fecha automaticamente em 10 segundos.";
                uint response = await Task.Run(() =>
                {
                    WTSSendMessage(IntPtr.Zero, (uint)sessionId, title, title.Length * 2, message, message.Length * 2,
                        0x00000001 | 0x00000030, 10, out uint result, true);
                    return result;
                }).ConfigureAwait(false);
                if (response == 2) session.End(EndReason.LocalUserEnded);
                // Um aviso por sessão: fechar ou expirar não deve fazê-lo reaparecer.
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSSendMessage(IntPtr server, uint sessionId, string title, int titleLength, string message,
        int messageLength, uint style, uint timeout, out uint response, bool wait);
}