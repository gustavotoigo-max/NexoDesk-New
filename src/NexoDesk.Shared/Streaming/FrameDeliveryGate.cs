namespace NexoDesk.Shared.Streaming;

/// <summary>Confirmação por atualização limita a fila entre captura, transporte e apresentação.</summary>
public sealed class FrameDeliveryGate
{
    private readonly object _gate = new();
    private TaskCompletionSource? _pending;
    private uint _index;
    public Task Begin(uint index)
    {
        lock (_gate)
        {
            if (_pending is not null) throw new InvalidOperationException("Já há imagem aguardando confirmação.");
            _index = index;
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }
    }
    public bool Acknowledge(uint index)
    {
        lock (_gate)
        {
            if (_pending is null || index != _index) return false;
            TaskCompletionSource completion = _pending;
            _pending = null;
            completion.TrySetResult();
            return true;
        }
    }
}