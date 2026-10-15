using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Session;

namespace NexoDesk.Shared.Tests;

public class InputCoordinatorTests
{
    private sealed class Sink : IInputSink
    {
        public readonly List<string> Events = [];
        public void Apply(KeyEventMessage key) => Events.Add($"key:{key.ScanCode}:{key.Action}");
        public void Apply(PointerEventMessage pointer) => Events.Add($"mouse:{pointer.Action}");
        public void ReleaseAll() => Events.Add("release");
    }
    private static KeyEventMessage Key(ushort code, KeyAction action = KeyAction.Down) => new(code, 0, false, action);
    private static PointerEventMessage Mouse(PointerAction action, PointerButton button = PointerButton.None) => new(.2,.3,button,action,0);

    [Fact]
    public void NewActivityTakesControlAndReleasesPriorKeysBeforeApplyingInput()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "Admin A"); using var b = coordinator.CreatePeer("b", "Admin B");
        a.Apply(Key(30)); b.Apply(Mouse(PointerAction.Move));
        Assert.Equal(new[] { "key:30:Down", "release", "mouse:Move" }, sink.Events);
        Assert.Equal("b", coordinator.State.OwnerId);
        Assert.Equal(2, coordinator.State.Epoch);
        a.Apply(Key(30, KeyAction.Up));
        Assert.Equal(3, sink.Events.Count);
        a.Apply(Key(48)); Assert.Equal("a", coordinator.State.OwnerId);
        Assert.Equal("release", sink.Events[^2]);
    }

    [Fact]
    public void OldRepeatOrDragCannotStealBackUntilUserReleasesAndActsAgain()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "A"); using var b = coordinator.CreatePeer("b", "B");
        a.Apply(Key(30)); a.Apply(Mouse(PointerAction.Down, PointerButton.Left));
        b.Apply(Key(48)); int count = sink.Events.Count;
        a.Apply(Key(30)); a.Apply(Mouse(PointerAction.Move));
        Assert.Equal(count, sink.Events.Count); Assert.Equal("b", coordinator.State.OwnerId);
        a.Apply(Key(30, KeyAction.Up)); a.Apply(Mouse(PointerAction.Up, PointerButton.Left));
        a.Apply(Mouse(PointerAction.Move)); Assert.Equal("a", coordinator.State.OwnerId);
    }

    [Fact]
    public void LeavingObserverDoesNotReleaseOwnersHeldInput()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "A"); var b = coordinator.CreatePeer("b", "B");
        a.Apply(Key(30)); b.Dispose();
        Assert.Single(sink.Events); Assert.Equal("a", coordinator.State.OwnerId);
        b.Apply(Key(48)); Assert.Single(sink.Events);
    }

    [Fact]
    public void LeavingOwnerReleasesOnceAndNextUserCanAcquire()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        var a = coordinator.CreatePeer("a", "A"); using var b = coordinator.CreatePeer("b", "B");
        a.Apply(Key(30)); a.ReleaseAll(); a.Dispose();
        Assert.Equal(1, sink.Events.Count(e => e == "release"));
        Assert.Equal("none", coordinator.State.OwnerId);
        b.Apply(Key(48)); Assert.Equal("b", coordinator.State.OwnerId);
    }

    [Fact]
    public void PhysicalActivityInterruptsAndRemoteCanAcquireAgainWithoutDisconnect()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "A");
        a.Apply(Key(30)); coordinator.NotifyLocalActivity();
        // Uma liberação antiga força a observação da atividade física pendente sem retomar controle.
        a.Apply(Key(30, KeyAction.Up)); Assert.Equal("local", coordinator.State.OwnerId);
        a.Apply(Mouse(PointerAction.Move)); Assert.Equal("a", coordinator.State.OwnerId);
        Assert.Equal(new[] { "key:30:Down", "release", "mouse:Move" }, sink.Events);
    }

    [Fact]
    public void MonitoringFailureStopsRemoteInputUntilRecovered()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "A");
        coordinator.SetLocalMonitoringAvailable(false);
        a.Apply(Key(30)); Assert.Empty(sink.Events);
        Assert.Equal("local", coordinator.State.OwnerId);
        coordinator.SetLocalMonitoringAvailable(true);
        a.Apply(Key(30)); Assert.Equal("a", coordinator.State.OwnerId);
    }

    [Fact]
    public void UnknownKeyUpDoesNotReleasePhysicalOrOtherInput()
    {
        var sink = new Sink(); var coordinator = new InputCoordinator(sink);
        using var a = coordinator.CreatePeer("a", "A");
        a.Apply(Key(30)); a.Apply(Key(48, KeyAction.Up));
        Assert.Single(sink.Events);
    }

    [Fact]
    public void TwoSlotGateReservesAndReleasesIndependently()
    {
        var gate = new SessionGate(2);
        Assert.True(gate.TryAcquire()); Assert.True(gate.TryAcquire()); Assert.False(gate.TryAcquire());
        gate.Release(); Assert.True(gate.IsBusy); Assert.True(gate.TryAcquire());
        gate.Release(); gate.Release(); Assert.False(gate.IsBusy);
    }
}
