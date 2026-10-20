using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Tests.Services;

public class ConversationStateCleanupTests : IntegrationTestBase
{
    [Fact]
    public async Task DeletesOnlyRowsExpiredLongerThanTheGrace()
    {
        var now = DateTime.UtcNow;
        using (var db = Db())
        {
            var business = new Business { Name = "Cleanup", Email = $"{Guid.NewGuid():N}@cleanup.test", Slug = $"cleanup-{Guid.NewGuid():N}" };
            db.Businesses.Add(business);
            db.WhatsAppConversationStates.AddRange(
                new WhatsAppConversationState { BusinessId = business.Id, Phone = "+972500000001", ExpiresAt = now.AddDays(-30) },
                new WhatsAppConversationState { BusinessId = business.Id, Phone = "+972500000002", ExpiresAt = now - ConversationStateCleanupWorker.Grace - TimeSpan.FromMinutes(1) },
                // Expired, but within the grace: a message may be handling it right now.
                new WhatsAppConversationState { BusinessId = business.Id, Phone = "+972500000003", ExpiresAt = now.AddMinutes(-5) },
                new WhatsAppConversationState { BusinessId = business.Id, Phone = "+972500000004", ExpiresAt = now.AddHours(24) });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
            Assert.Equal(2, await ConversationStateCleanupWorker.DeleteExpiredAsync(db, now));

        using (var db = Db())
        {
            var left = await db.WhatsAppConversationStates.Select(s => s.Phone).OrderBy(p => p).ToListAsync();
            Assert.Equal(["+972500000003", "+972500000004"], left);
        }
    }
}
