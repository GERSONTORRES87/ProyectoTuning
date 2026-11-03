namespace Turning.Application.Features.ExperimentSessions;

/// <summary>Coordina las escrituras de una sesión en la instancia de la API.</summary>
public static class SessionOperationLock
{
    // shortcut: coordinación de una instancia; usar bloqueo distribuido al escalar la API.
    private static readonly Dictionary<Guid, Entry> Entries = new();
    private static readonly Lock Gate = new();

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    public static async Task<IDisposable> AcquireAsync(Guid sessionId, CancellationToken ct = default)
    {
        Entry entry;
        lock (Gate)
        {
            if (!Entries.TryGetValue(sessionId, out entry!))
                Entries[sessionId] = entry = new Entry();
            entry.Users++;
        }

        try { await entry.Semaphore.WaitAsync(ct); }
        catch { Release(sessionId, entry, acquired: false); throw; }
        return new Lease(sessionId, entry);
    }

    private static void Release(Guid id, Entry entry, bool acquired)
    {
        lock (Gate)
        {
            if (acquired) entry.Semaphore.Release();
            if (--entry.Users == 0)
            {
                Entries.Remove(id);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Lease(Guid id, Entry entry) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(id, entry, acquired: true);
        }
    }
}
