using NexoDesk.Shared.Messages;

namespace NexoDesk.Shared.Session;

/// <summary>Um injetor, um dono por vez. Atividade nova transfere o controle sem fechar conexões.</summary>
public sealed class InputCoordinator(IInputSink sink)
{
    private readonly IInputSink _sink = sink;
    private readonly object _gate = new();
    private Peer? _owner;
    private ControlStateMessage _state = new(0, "none", "Aguardando atividade");
    private long _localEpoch, _observedLocalEpoch;
    private int _localQueued;
    private volatile bool _monitoringAvailable = true;
    public void SetLocalMonitoringAvailable(bool available)
    { _monitoringAvailable = available; if (!available) NotifyLocalActivity(); }
    public ControlStateMessage State { get { lock (_gate) return _state; } }
    public Peer CreatePeer(string id, string name) => new(this, id, name);

    // Chamado pelo monitor físico: sem waits, SendInput ou lock no callback nativo.
    public void NotifyLocalActivity()
    {
        Interlocked.Increment(ref _localEpoch);
        if (Interlocked.Exchange(ref _localQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (_gate) ObserveLocal(); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
                finally
                {
                    Volatile.Write(ref _localQueued, 0);
                    if (Volatile.Read(ref _localEpoch) != Volatile.Read(ref _observedLocalEpoch)) NotifyLocalActivity();
                }
            });
    }

    private void ObserveLocal()
    {
        long epoch = Volatile.Read(ref _localEpoch);
        if (epoch == _observedLocalEpoch) return;
        _observedLocalEpoch = epoch;
        ChangeOwner(null, "local", "Pessoa no PC REMOTO");
    }

    private void ChangeOwner(Peer? next, string id, string name)
    {
        if (_state.OwnerId == id) return;
        try { if (_owner is not null) _sink.ReleaseAll(); }
        finally
        {
            if (_owner is not null) { _owner.AppliedKeys.Clear(); _owner.AppliedButtons.Clear(); }
            _owner = next;
            _state = new(_state.Epoch + 1, id, name);
        }
    }

    public sealed class Peer : IInputSink, IDisposable
    {
        private readonly InputCoordinator _coordinator;
        public string Id { get; private set; }
        public string Name { get; private set; }
        private bool _disposed;
        private readonly HashSet<(ushort, bool)> _observedKeys = [];
        private readonly HashSet<PointerButton> _observedButtons = [];
        internal readonly HashSet<(ushort, bool)> AppliedKeys = [];
        internal readonly HashSet<PointerButton> AppliedButtons = [];
        internal Peer(InputCoordinator coordinator, string id, string name) { _coordinator = coordinator; Id = id; Name = name; }
        public void Bind(string id, string name) { Id = id; Name = name; }

        public void Apply(KeyEventMessage key)
        {
            lock (_coordinator._gate)
            {
                if (_disposed) return;
                _coordinator.ObserveLocal();
                var identity = (key.ScanCode, key.IsExtended);
                if (key.Action == KeyAction.Up)
                {
                    _observedKeys.Remove(identity);
                    if (!_coordinator._monitoringAvailable || _coordinator._owner != this || !AppliedKeys.Remove(identity)) return;
                }
                else
                {
                    if (!_coordinator._monitoringAvailable) return;
                    bool fresh = _observedKeys.Add(identity);
                    // Repetição de uma tecla já mantida não rouba o controle de outra pessoa.
                    if (!fresh && _coordinator._owner != this) return;
                    _coordinator.ChangeOwner(this, Id, Name);
                    AppliedKeys.Add(identity);
                }
                _coordinator._sink.Apply(key);
            }
        }

        public void Apply(PointerEventMessage pointer)
        {
            lock (_coordinator._gate)
            {
                if (_disposed) return;
                _coordinator.ObserveLocal();
                if (pointer.Action == PointerAction.Up)
                {
                    _observedButtons.Remove(pointer.Button);
                    if (!_coordinator._monitoringAvailable || _coordinator._owner != this || !AppliedButtons.Remove(pointer.Button)) return;
                }
                else
                {
                    if (!_coordinator._monitoringAvailable) return;
                    if (pointer.Action == PointerAction.Down)
                    {
                        bool fresh = _observedButtons.Add(pointer.Button);
                        if (!fresh && _coordinator._owner != this) return;
                    }
                    // Um arraste antigo não pode continuar depois que outro assumiu.
                    if (pointer.Action == PointerAction.Move && _observedButtons.Count > 0 && _coordinator._owner != this) return;
                    _coordinator.ChangeOwner(this, Id, Name);
                    if (!_coordinator._monitoringAvailable) return;
                    if (pointer.Action == PointerAction.Down) AppliedButtons.Add(pointer.Button);
                }
                _coordinator._sink.Apply(pointer);
            }
        }

        public void ReleaseAll()
        {
            lock (_coordinator._gate)
            {
                _observedKeys.Clear(); _observedButtons.Clear();
                if (_coordinator._owner == this) _coordinator.ChangeOwner(null, "none", "Aguardando atividade");
            }
        }
        public void Dispose() { lock (_coordinator._gate) { ReleaseAll(); _disposed = true; } }
    }
}
