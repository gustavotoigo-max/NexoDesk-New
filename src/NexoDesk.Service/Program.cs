using System.Security.Principal;
using System.ServiceProcess;
using NexoDesk.Service;

if (!WindowsIdentity.GetCurrent().IsSystem)
{
    Console.Error.WriteLine("O agente deve ser iniciado pelo serviço NexoDesk New, como SYSTEM.");
    return 1;
}
if (args.Length == 1 && args[0] == "--worker")
{
    await new RemoteWorker().RunAsync();
    return 0;
}
ServiceBase.Run(new SupervisorService());
return 0;