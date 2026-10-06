namespace DeskBox.Services;

internal sealed class GlanceDailyRefreshPolicy
{
    private DateOnly? _completedDay;
    private DateTimeOffset _retryAfter;
    internal bool ShouldRefresh(DateTimeOffset now) => _completedDay != DateOnly.FromDateTime(now.LocalDateTime) && now >= _retryAfter;
    internal void Begin(DateTimeOffset now) => _retryAfter = now.AddMinutes(15);
    internal void Complete(DateTimeOffset now, DateOnly? published, bool latestVerified = false)
    {
        if (latestVerified || published >= DateOnly.FromDateTime(now.LocalDateTime)) _completedDay = DateOnly.FromDateTime(now.LocalDateTime);
    }
    internal void Reset() { _completedDay = null; _retryAfter = default; }
    internal void Cancel() => _retryAfter = default;
}
