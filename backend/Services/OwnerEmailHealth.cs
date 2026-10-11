namespace BarberSaas.Api.Services;

// In-memory status of the Gmail owner-email channel (IOwnerEmailSender). Gmail refresh tokens
// die silently (expired in "Testing" mode, revoked on a password change) and every owner email
// then fails without anyone noticing -- this backs the platform-admin dashboard warning and the
// alert email OwnerEmailMonitor sends. Deliberately not persisted: OwnerEmailHealthWorker
// re-checks shortly after every start, so a restart only loses the status for a minute.
public class OwnerEmailHealth
{
    public enum Notification { None, Failing, Recovered }

    public record Snapshot(string Status, DateTime? FailingSince, string? LastError, DateTime? LastCheckedAt);

    private readonly Lock _lock = new();
    private DateTime? _failingSince;
    private string? _lastError;
    private DateTime? _lastCheckedAt;
    private DateTime? _lastAlertSentAt;
    private bool _recoveryPending;
    private DateTime? _retryNotifyAfter;

    public void RecordSuccess()
    {
        lock (_lock)
        {
            // Only announce a recovery if we actually told someone it was broken.
            if (_failingSince is not null && _lastAlertSentAt is not null) _recoveryPending = true;
            _failingSince = null;
            _lastError = null;
            _lastAlertSentAt = null;
            _lastCheckedAt = DateTime.UtcNow;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_lock)
        {
            _failingSince ??= DateTime.UtcNow;
            _lastError = error.Length > 500 ? error[..500] : error;
            _lastCheckedAt = DateTime.UtcNow;
            _recoveryPending = false;
        }
    }

    public Snapshot GetSnapshot()
    {
        lock (_lock)
        {
            var status = _failingSince is not null ? "failing" : _lastCheckedAt is not null ? "ok" : "unknown";
            return new Snapshot(status, _failingSince, _lastError, _lastCheckedAt);
        }
    }

    // What OwnerEmailMonitor should send now: the first alert of a failure episode, a repeat
    // every `repeatAfter` while it stays broken, and one "working again" note after recovery.
    public Notification PendingNotification(DateTime now, TimeSpan repeatAfter)
    {
        lock (_lock)
        {
            if (now < _retryNotifyAfter) return Notification.None;
            if (_failingSince is not null)
                return _lastAlertSentAt is null || now - _lastAlertSentAt >= repeatAfter ? Notification.Failing : Notification.None;
            return _recoveryPending ? Notification.Recovered : Notification.None;
        }
    }

    public void MarkNotified(Notification sent, DateTime now)
    {
        lock (_lock)
        {
            if (sent == Notification.Failing) _lastAlertSentAt = now;
            if (sent == Notification.Recovered) _recoveryPending = false;
            _retryNotifyAfter = null;
        }
    }

    // The alert itself couldn't be sent (e.g. Brevo rejecting the server) -- back off instead of
    // retrying, and logging the failure, every worker tick.
    public void DeferNotification(DateTime until)
    {
        lock (_lock) _retryNotifyAfter = until;
    }
}
