using NexoDesk.App.Services;
using NexoDesk.App.ViewModels;

namespace NexoDesk.Windows.Tests;

public class UpdateViewModelTests
{
    private static readonly UpdateInfo NewVersion = new(new Version(1, 3, 0), "v1.3.0",
        "https://github.com/gustavotoigo-max/NexoDesk-New/releases/download/v1.3.0/NexoDesk-New-Setup-1.3.0.exe",
        "Teste", new string('a', 64));

    [Fact]
    public async Task MainButtonChecksDownloadsAndExitsOnlyAfterInstallerStarts()
    {
        var calls = new List<string>();
        using var vm = new UpdateViewModel(
            () => { calls.Add("exit"); return Task.CompletedTask; },
            _ => { calls.Add("check"); return Task.FromResult(new UpdateCheckResult(UpdateCheckState.Available, NewVersion, "Disponível")); },
            (info, _, _) => { Assert.Equal(NewVersion, info); calls.Add("install"); return Task.FromResult(true); });
        await vm.UpdateAsync();
        Assert.Equal(new[] { "check", "install", "exit" }, calls);
        Assert.False(vm.Busy);
    }

    [Theory]
    [InlineData(UpdateCheckState.UpToDate)]
    [InlineData(UpdateCheckState.NotPublished)]
    [InlineData(UpdateCheckState.Failed)]
    public async Task MainButtonDoesNotInstallWhenCheckHasNoValidNewRelease(UpdateCheckState state)
    {
        using var vm = new UpdateViewModel(
            () => throw new InvalidOperationException("Não pode fechar."),
            _ => Task.FromResult(new UpdateCheckResult(state, null, "Resultado da consulta")),
            (_, _, _) => throw new InvalidOperationException("Não pode instalar."));
        await vm.UpdateAsync();
        Assert.Equal("Resultado da consulta", vm.Status);
        Assert.False(vm.Available);
    }

    [Fact]
    public async Task FailedRefreshCannotInstallPreviouslyAvailableRelease()
    {
        int checks = 0, installs = 0;
        using var vm = new UpdateViewModel(() => Task.CompletedTask,
            _ => Task.FromResult(++checks == 1
                ? new UpdateCheckResult(UpdateCheckState.Available, NewVersion, "Disponível")
                : new UpdateCheckResult(UpdateCheckState.Failed, null, "Sem internet")),
            (_, _, _) => { installs++; return Task.FromResult(true); });
        await vm.CheckAsync();
        Assert.True(vm.Available);
        await vm.UpdateAsync();
        Assert.False(vm.Available);
        Assert.Equal(0, installs);
    }

    [Fact]
    public async Task RepeatedClickDoesNotDuplicatePendingCheckOrInstallation()
    {
        var pending = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int checks = 0, installs = 0;
        using var vm = new UpdateViewModel(() => Task.CompletedTask,
            _ => { checks++; return pending.Task; },
            (_, _, _) => { installs++; return Task.FromResult(false); });
        Task first = vm.UpdateAsync();
        await vm.UpdateAsync();
        pending.SetResult(new(UpdateCheckState.Available, NewVersion, "Disponível"));
        await first;
        Assert.Equal(1, checks);
        Assert.Equal(1, installs);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task ClosingWindowCancelsCheckWithoutInstallation()
    {
        var pending = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = new UpdateViewModel(() => throw new InvalidOperationException("Não pode fechar."),
            ct => pending.Task.WaitAsync(ct),
            (_, _, _) => throw new InvalidOperationException("Não pode instalar."));
        Task check = vm.UpdateAsync();
        vm.Dispose();
        await check;
        Assert.False(vm.UpdateCommand.CanExecute(null));
        Assert.False(vm.CheckCommand.CanExecute(null));
        Assert.False(vm.InstallCommand.CanExecute(null));
    }
}
