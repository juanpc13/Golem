namespace WardenCli;

/// <summary>THE BARRIER of a batch (propuesta 95): the queues sent together wait for each other at every @sync — a queue that reaches it waits
/// until every other queue of the batch reached it too, or finished, or was stopped; then all go on together. The console's own directive:
/// never sent to a golem.
///
/// 8-oct-2026 (Juan: "yellow parece que no recibió el pedido… naranja terminó colisionando con yellow"): the queue that ARRIVED LAST opened
/// the barrier and then waited on the NEXT one, which nobody would open — its tab stayed "sending…", the next SEND skipped it, and a peer
/// walked into it. Every arrival now waits on the very barrier it reached, captured before it may open.</summary>
public sealed class SyncBarrier
{
    private int participants, arrived;
    private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SyncBarrier(int participants) { this.participants = participants; }

    /// <summary>This queue reached a @sync: the task completes when every queue still in the batch reached it too.</summary>
    public Task ArriveAsync()
    {
        var reached = gate;   // the barrier this queue reached — opened by this very arrival when it is the last
        arrived++;
        if (arrived >= participants) Open();
        return reached.Task;
    }

    /// <summary>This queue finished or was stopped: the others no longer wait for it.</summary>
    public void Leave()
    {
        participants--;
        if (arrived >= participants && participants > 0) Open();
    }

    private void Open()
    {
        var opened = gate;
        arrived = 0;
        gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        opened.TrySetResult();
    }
}
