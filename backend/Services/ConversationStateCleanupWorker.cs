using BarberSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Hourly cleanup: WhatsAppConversationState rows that expired long ago, and chat files past
// AttachmentRetention (below).
// Deletes WhatsAppConversationState rows that expired long ago. Every read already ignores (or
// resets, WhatsAppController.ResetIfExpired) an expired row, so they're dead weight: production had
// 38 rows, all but one expired weeks earlier. A row is kept for a day past its ExpiresAt so a
// message being handled right as it expires never has its row deleted under it.
public class ConversationStateCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<ConversationStateCleanupWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public static Task<int> DeleteExpiredAsync(AppDbContext db, DateTime utcNow, CancellationToken ct = default)
    {
        var cutoff = utcNow - Grace;
        return db.WhatsAppConversationStates.Where(s => s.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
    }

    // Chat files (customer and owner) are kept 90 days (owner's choice, QA run 5 SEC-1: nothing was
    // ever deleted). The message stays, reading "📎 name" like a file too big to download.
    public static readonly TimeSpan AttachmentRetention = TimeSpan.FromDays(90);
    private const int AttachmentBatch = 200;

    public static async Task<int> DeleteOldAttachmentsAsync(AppDbContext db, DateTime utcNow, CancellationToken ct = default)
    {
        var cutoff = utcNow - AttachmentRetention;
        var total = 0;
        while (true)
        {
            var batch = await db.ChatAttachments.Where(a => a.CreatedAt < cutoff)
                .OrderBy(a => a.CreatedAt).Select(a => new { a.Id, a.FileName }).Take(AttachmentBatch).ToListAsync(ct);
            if (batch.Count == 0) return total;
            var ids = batch.Select(a => a.Id).ToList();
            var names = batch.ToDictionary(a => a.Id, a => a.FileName);
            var messages = await db.ChatMessages.Where(m => m.AttachmentId != null && ids.Contains(m.AttachmentId)).ToListAsync(ct);
            foreach (var m in messages)
            {
                var label = $"📎 {names[m.AttachmentId!]}";
                m.Text = m.Text.Length > 0 ? $"{label}\n{m.Text}" : label;
                m.AttachmentId = null;
            }
            await db.SaveChangesAsync(ct);
            await db.ChatAttachments.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass soon after startup, then hourly.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await DeleteExpiredAsync(db, DateTime.UtcNow, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} expired WhatsApp conversation rows", deleted);
                var files = await DeleteOldAttachmentsAsync(db, DateTime.UtcNow, stoppingToken);
                if (files > 0)
                    logger.LogInformation("Deleted {Count} chat files older than {Days} days", files, AttachmentRetention.TotalDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conversation state cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
