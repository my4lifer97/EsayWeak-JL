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

    // Chat files go after 90 days (owner's choice); the message stays, naming the file.
    [Fact]
    public async Task DeletesChatFilesOlderThan90Days_KeepingTheMessage()
    {
        var now = DateTime.UtcNow;
        string oldMessageId, captionedId, newMessageId;
        using (var db = Db())
        {
            var business = new Business { Name = "Files", Email = $"{Guid.NewGuid():N}@cleanup.test", Slug = $"files-{Guid.NewGuid():N}" };
            db.Businesses.Add(business);
            ChatMessage WithFile(string name, string text, DateTime at)
            {
                var file = new ChatAttachment { BusinessId = business.Id, FileName = name, ContentType = "image/jpeg", Data = [1, 2, 3], CreatedAt = at };
                db.ChatAttachments.Add(file);
                var message = ChatLog.NewMessage(business.Id, "+972500000010", ChatSender.Customer, text);
                message.AttachmentId = file.Id;
                db.ChatMessages.Add(message);
                return message;
            }
            oldMessageId = WithFile("old.jpg", "", now.AddDays(-91)).Id;
            captionedId = WithFile("trip.jpg", "look", now.AddDays(-100)).Id;
            newMessageId = WithFile("new.jpg", "", now.AddDays(-89)).Id;
            await db.SaveChangesAsync();
        }

        using (var db = Db())
            Assert.Equal(2, await ConversationStateCleanupWorker.DeleteOldAttachmentsAsync(db, now));

        using (var db = Db())
        {
            Assert.Equal(["new.jpg"], await db.ChatAttachments.Where(a => a.FileName.EndsWith(".jpg")).Select(a => a.FileName).ToListAsync());
            var old = await db.ChatMessages.SingleAsync(m => m.Id == oldMessageId);
            Assert.Null(old.AttachmentId);
            Assert.Equal("📎 old.jpg", old.Text);
            Assert.Equal("📎 trip.jpg\nlook", (await db.ChatMessages.SingleAsync(m => m.Id == captionedId)).Text);
            Assert.NotNull((await db.ChatMessages.SingleAsync(m => m.Id == newMessageId)).AttachmentId);
        }
    }
}
