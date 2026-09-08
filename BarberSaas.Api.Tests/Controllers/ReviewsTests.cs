using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class ReviewsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    // Seeds a per-business Customer row for `accountId` plus a CONFIRMED appointment that already
    // ended -- i.e. a "completed" appointment (there is no stored COMPLETED status). Booking through
    // the API can't produce a past appointment (AvailabilityService drops past slots), so it's
    // inserted directly.
    private static int _seedCounter;

    private async Task SeedCompletedAppointment(string slug, string accountId, string phone, bool pendingCancellation = false)
    {
        // Distinct start time per seeded appointment so two of them for the same business don't
        // collide on the partial unique index (BusinessId, Date, StartTime) for CONFIRMED rows.
        var hour = 9 + Interlocked.Increment(ref _seedCounter) % 8;
        var start = $"{hour:D2}:00";
        var end = $"{hour:D2}:30";

        using var db = Db();
        var business = db.Businesses.First(b => b.Slug == slug);
        var item = new Item { BusinessId = business.Id, NameEn = "Haircut", NameAr = "Haircut", NameHe = "Haircut", DurationMinutes = 30, Price = 20m };
        db.Items.Add(item);
        var customer = new Customer { BusinessId = business.Id, CustomerAccountId = accountId, Name = "Rev", FamilyName = "Iewer", Phone = phone };
        db.Customers.Add(customer);
        db.Appointments.Add(new Appointment
        {
            BusinessId = business.Id,
            CustomerId = customer.Id,
            ItemId = item.Id,
            Date = DateTime.Now.Date.AddDays(-2),
            StartTime = start,
            EndTime = end,
            Status = AppointmentStatus.CONFIRMED,
            PendingCancellationApproval = pendingCancellation,
        });
        await db.SaveChangesAsync();
    }

    private double RatingAverageOf(string slug)
    {
        using var db = Db();
        return db.Businesses.First(b => b.Slug == slug).RatingAverage;
    }

    [Fact]
    public async Task Create_WithoutCompletedAppointment_Returns403()
    {
        await RegisterAndLoginBusiness("rev-noappt@example.com", "rev-noappt");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000001");
        Authorize(Client, customer.Token);

        var resp = await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-noappt", 5, "Great"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithCompletedAppointment_SucceedsAndUpdatesAggregate()
    {
        await RegisterAndLoginBusiness("rev-ok@example.com", "rev-ok");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000002");
        await SeedCompletedAppointment("rev-ok", customer.CustomerId, "+15552000002");
        Authorize(Client, customer.Token);

        var resp = await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-ok", 4, "Solid cut"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ReviewDto>();
        Assert.Equal(4, dto!.Rating);
        Assert.Equal(4d, RatingAverageOf("rev-ok"));
    }

    [Fact]
    public async Task Create_PendingCancellationAppointmentOnly_Returns403()
    {
        await RegisterAndLoginBusiness("rev-pending@example.com", "rev-pending");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000003");
        await SeedCompletedAppointment("rev-pending", customer.CustomerId, "+15552000003", pendingCancellation: true);
        Authorize(Client, customer.Token);

        var resp = await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-pending", 5, null));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Twice_Returns409()
    {
        await RegisterAndLoginBusiness("rev-dup@example.com", "rev-dup");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000004");
        await SeedCompletedAppointment("rev-dup", customer.CustomerId, "+15552000004");
        Authorize(Client, customer.Token);

        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-dup", 5, "One"))).StatusCode);
        var second = await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-dup", 3, "Two"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Update_ByAuthor_ChangesAggregate_ByOther_Returns404()
    {
        await RegisterAndLoginBusiness("rev-edit@example.com", "rev-edit");
        var author = await LoginCustomerViaWhatsAppAsync("+15552000005");
        await SeedCompletedAppointment("rev-edit", author.CustomerId, "+15552000005");
        Authorize(Client, author.Token);
        var created = await (await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-edit", 5, "First"))).Content.ReadFromJsonAsync<ReviewDto>();

        var patch = await Client.PatchAsJsonAsync($"/api/reviews/{created!.Id}", new UpdateReviewRequest(2, "Changed my mind"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(2d, RatingAverageOf("rev-edit"));

        var other = await LoginCustomerViaWhatsAppAsync("+15552000006");
        Authorize(Client, other.Token);
        var patchByOther = await Client.PatchAsJsonAsync($"/api/reviews/{created.Id}", new UpdateReviewRequest(5, "hijack"));
        Assert.Equal(HttpStatusCode.NotFound, patchByOther.StatusCode);
    }

    [Fact]
    public async Task Delete_ByAuthor_RemovesAndRecomputes()
    {
        await RegisterAndLoginBusiness("rev-del@example.com", "rev-del");
        var author = await LoginCustomerViaWhatsAppAsync("+15552000007");
        await SeedCompletedAppointment("rev-del", author.CustomerId, "+15552000007");
        Authorize(Client, author.Token);
        var created = await (await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-del", 5, "Bye soon"))).Content.ReadFromJsonAsync<ReviewDto>();

        var del = await Client.DeleteAsync($"/api/reviews/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal(0d, RatingAverageOf("rev-del"));
        Assert.Equal(0, GetRatingCount("rev-del"));
    }

    [Fact]
    public async Task HiddenReview_ExcludedFromPublicListAndAggregate()
    {
        await RegisterAndLoginBusiness("rev-hide@example.com", "rev-hide");
        var a = await LoginCustomerViaWhatsAppAsync("+15552000008");
        var b = await LoginCustomerViaWhatsAppAsync("+15552000009");
        await SeedCompletedAppointment("rev-hide", a.CustomerId, "+15552000008");
        await SeedCompletedAppointment("rev-hide", b.CustomerId, "+15552000009");

        Authorize(Client, a.Token);
        var hidden = await (await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-hide", 1, "Bad"))).Content.ReadFromJsonAsync<ReviewDto>();
        Authorize(Client, b.Token);
        await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-hide", 5, "Good"));

        // Average of 1 and 5 while both visible.
        Assert.Equal(3d, RatingAverageOf("rev-hide"));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BarberSaas.Api.Data.AppDbContext>();
            var reviewRow = db.Reviews.First(r => r.Id == hidden!.Id);
            reviewRow.IsHidden = true;
            db.SaveChanges();
            await scope.ServiceProvider.GetRequiredService<BarberSaas.Api.Services.ReviewService>()
                .RecomputeAggregate(reviewRow.BusinessId);
        }

        Assert.Equal(5d, RatingAverageOf("rev-hide"));

        Client.DefaultRequestHeaders.Authorization = null;
        var publicList = await Client.GetFromJsonAsync<PublicReviewListDto>("/api/businesses/rev-hide/reviews");
        Assert.Equal(1, publicList!.Rating.Count);
        Assert.Single(publicList.Reviews.Items);
        Assert.Equal(5, publicList.Reviews.Items[0].Rating);
    }

    [Fact]
    public async Task OwnerReply_RoundTrips_AndAppearsInPublicList()
    {
        var token = await RegisterAndLoginBusiness("rev-reply@example.com", "rev-reply");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000010");
        await SeedCompletedAppointment("rev-reply", customer.CustomerId, "+15552000010");
        Authorize(Client, customer.Token);
        var created = await (await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-reply", 4, "Nice"))).Content.ReadFromJsonAsync<ReviewDto>();

        Authorize(Client, token);
        var reply = await Client.PostAsJsonAsync($"/api/admin/reviews/{created!.Id}/reply", new OwnerReplyRequest("Thanks for coming in!"));
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var publicList = await Client.GetFromJsonAsync<PublicReviewListDto>("/api/businesses/rev-reply/reviews");
        Assert.Equal("Thanks for coming in!", publicList!.Reviews.Items[0].OwnerReply);
    }

    [Fact]
    public async Task Eligibility_ReflectsCompletedAppointmentAndExistingReview()
    {
        await RegisterAndLoginBusiness("rev-elig@example.com", "rev-elig");
        var customer = await LoginCustomerViaWhatsAppAsync("+15552000011");
        Authorize(Client, customer.Token);

        var before = await Client.GetFromJsonAsync<ReviewEligibilityDto>("/api/reviews/eligibility?businessSlug=rev-elig");
        Assert.False(before!.CanReview);
        Assert.False(before.AlreadyReviewed);

        await SeedCompletedAppointment("rev-elig", customer.CustomerId, "+15552000011");
        var after = await Client.GetFromJsonAsync<ReviewEligibilityDto>("/api/reviews/eligibility?businessSlug=rev-elig");
        Assert.True(after!.CanReview);

        await Client.PostAsJsonAsync("/api/reviews", new CreateReviewRequest("rev-elig", 5, "Yes"));
        var withReview = await Client.GetFromJsonAsync<ReviewEligibilityDto>("/api/reviews/eligibility?businessSlug=rev-elig");
        Assert.True(withReview!.AlreadyReviewed);
        Assert.Equal(5, withReview.Review!.Rating);
    }

    private int GetRatingCount(string slug)
    {
        using var db = Db();
        return db.Businesses.First(b => b.Slug == slug).RatingCount;
    }
}
