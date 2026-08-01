using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class PaymentPhase1Tests
{
    [TestCase(" paid ", "PAID")]
    [TestCase("CANCELLED", "CANCELLED")]
    [TestCase("expired", "EXPIRED")]
    [TestCase("FAILED", "FAILED")]
    [TestCase("pending", "PENDING")]
    [TestCase("PROCESSING", "PROCESSING")]
    [TestCase("underpaid", "UNDERPAID")]
    [TestCase("new-status", "UNKNOWN")]
    public void NormalizeStatus_HandlesOfficialValues(string input, string expected)
        => PayOsProvider.NormalizeStatus(input).Should().Be(expected);

    [Test]
    public void CallbackBaseUrl_ProductionAcceptsOnlyPublicHttps()
    {
        PaymentService.TryValidateCallbackBaseUrl("https://app.example.test/base", true, out var valid).Should().BeTrue();
        valid.Should().Be("https://app.example.test/base");
        PaymentService.TryValidateCallbackBaseUrl("http://localhost:5240", true, out _).Should().BeFalse();
        PaymentService.TryValidateCallbackBaseUrl("https://user@host.test/path?x=1", true, out _).Should().BeFalse();
    }

    [Test]
    public void CallbackBaseUrl_DevelopmentFallsBackToDemoThenLocalhost()
    {
        PaymentService.ResolveCallbackBaseUrl(null, "http://demo.test:5240/", true).Should().Be("http://demo.test:5240");
        PaymentService.ResolveCallbackBaseUrl(null, null, true).Should().Be("http://localhost:5240");
    }

    [Test]
    public void ProviderOrderCode_ParsesOnlyExactLegacyReference()
    {
        PayOsProvider.TryParseOrderCode("PO_123", out var code).Should().BeTrue();
        code.Should().Be(123);
        PayOsProvider.TryParseOrderCode("xPO_123", out _).Should().BeFalse();
        PayOsProvider.TryParseOrderCode("PO_123x", out _).Should().BeFalse();
    }

    [Test]
    public void PaymentModel_HasFilteredUniqueProviderOrderCodeIndex()
    {
        using var db = TestDb.CreateInMemory();
        var index = db.Model.FindEntityType(typeof(PaymentTransaction))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ux_payment_transactions_provider_order_code");
        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("provider_order_code IS NOT NULL");
    }

    [Test]
    public async Task ReconcileClient_SendsBearerToken()
    {
        var handler = new CapturingHandler();
        var client = new PlanApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://app.example.test/") });
        await client.ReconcilePaymentAsync("token-value", 123L);
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/plans/payments/123/reconcile");
        handler.Request.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "token-value"));
    }

    [Test]
    public async Task Reconcile_OwnedCompleted_ShortCircuitsWithoutProviderCall()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.PaymentTransactions.Add(Payment(user.Id, 101, "completed"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();

        var result = await CreateService(db, provider).ReconcileAsync(user.Id, 101, CancellationToken.None);

        result!.Status.Should().Be("completed");
        provider.Verify(p => p.GetTransactionStatusAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Reconcile_ForeignOrder_NeverCallsProvider()
    {
        using var db = TestDb.CreateInMemory();
        var owner = AddUser(db);
        var requester = AddUser(db);
        db.PaymentTransactions.Add(Payment(owner.Id, 102, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();

        (await CreateService(db, provider).ReconcileAsync(requester.Id, 102, CancellationToken.None)).Should().BeNull();
        provider.Verify(p => p.GetTransactionStatusAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Reconcile_PaidInvoiceWithUnpaidBalance_DoesNotActivate()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        var payment = Payment(user.Id, 103, "pending");
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(103, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 103, "link", "PAID", "PAID", 90, 100, 10));

        await CreateService(db, provider).ReconcileAsync(user.Id, 103, CancellationToken.None);

        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("failed");
        (await db.UserPlans.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task Reconcile_ProviderUnavailable_LeavesLifecycleUnchanged()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.PaymentTransactions.Add(Payment(user.Id, 104, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(104, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(false, 104, null, "UNKNOWN", "ERROR", 0, 0, 0));

        var result = await CreateService(db, provider).ReconcileAsync(user.Id, 104, CancellationToken.None);

        result!.Status.Should().Be("pending");
        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("pending");
    }

    [Test]
    public async Task Reconcile_Processing_StaysPendingButReturnsProviderStatus()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.PaymentTransactions.Add(Payment(user.Id, 105, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(105, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 105, "link", "PROCESSING", "PROCESSING", 0, 100, 100));

        var result = await CreateService(db, provider).ReconcileAsync(user.Id, 105, CancellationToken.None);

        result!.Status.Should().Be("pending");
        result.ProviderStatus.Should().Be("PROCESSING");
    }

    [Test]
    public async Task Reconcile_VerifiedPaid_CreatesExactlyOnePlanAndCompletesPayment()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        var plan = new Plan { Id = Guid.NewGuid(), PlanKey = "pro", DisplayName = "Pro" };
        db.Plans.Add(plan);
        db.PaymentTransactions.Add(Payment(user.Id, 106, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(106, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 106, "link", "PAID", "paid", 100, 100, 0));

        var service = CreateService(db, provider);
        (await service.ReconcileAsync(user.Id, 106, CancellationToken.None))!.Status.Should().Be("completed");
        (await service.ReconcileAsync(user.Id, 106, CancellationToken.None))!.Status.Should().Be("completed");

        (await db.UserPlans.CountAsync()).Should().Be(1);
        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("completed");
    }

    [TestCase("completed")]
    [TestCase("refunded")]
    [TestCase("demo_completed")]
    public async Task Reconcile_ImmutableStates_CannotBeDowngraded(string status)
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.PaymentTransactions.Add(Payment(user.Id, 107, status));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();

        (await CreateService(db, provider).ReconcileAsync(user.Id, 107, CancellationToken.None))!.Status.Should().Be(status);
        provider.Verify(p => p.GetTransactionStatusAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Reconcile_IntegrityFailed_CannotBePromotedByPaidProviderResult()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        var payment = Payment(user.Id, 108, "failed");
        payment.ErrorMessage = "integrity_failed";
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(108, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 108, null, "PAID", "PAID", 100, 100, 0));

        await CreateService(db, provider).ReconcileAsync(user.Id, 108, CancellationToken.None);
        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("failed");
        (await db.UserPlans.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task Reconcile_FailedWithCreatedLink_CanBePromotedByPaidProviderResult()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.Plans.Add(new Plan { Id = Guid.NewGuid(), PlanKey = "pro", DisplayName = "Pro" });
        var payment = Payment(user.Id, 111, "failed");
        payment.ProviderPaymentLinkId = "link-111";
        payment.ErrorMessage = "Key cannot be null or empty (Parameter 'key')";
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.GetTransactionStatusAsync(111, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 111, "link-111", "PAID", "PAID", 100, 100, 0));

        (await CreateService(db, provider).ReconcileAsync(user.Id, 111, CancellationToken.None))!.Status.Should().Be("completed");
        var recovered = await db.PaymentTransactions.SingleAsync();
        recovered.Status.Should().Be("completed");
        recovered.ErrorMessage.Should().BeNull();
        (await db.UserPlans.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Reconcile_FailedWithoutLocalLink_StillPromotedWhenProviderConfirmsPaid()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.Plans.Add(new Plan { Id = Guid.NewGuid(), PlanKey = "pro", DisplayName = "Pro" });
        db.PaymentTransactions.Add(Payment(user.Id, 112, "failed"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        // The provider is authoritative: a PAID verdict for the exact order code with a matching
        // amount means real money arrived, so the local lifecycle must be recovered.
        provider.Setup(p => p.GetTransactionStatusAsync(112, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionStatusResult(true, 112, "link-112", "PAID", "PAID", 100, 100, 0));

        (await CreateService(db, provider).ReconcileAsync(user.Id, 112, CancellationToken.None))!.Status.Should().Be("completed");
        var recovered = await db.PaymentTransactions.SingleAsync();
        recovered.Status.Should().Be("completed");
        recovered.ProviderPaymentLinkId.Should().Be("link-112");
        (await db.UserPlans.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Webhook_PaidWithMatchingAmount_ActivatesWithoutInvoiceIntegrityData()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.Plans.Add(new Plan { Id = Guid.NewGuid(), PlanKey = "pro", DisplayName = "Pro" });
        db.PaymentTransactions.Add(Payment(user.Id, 113, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        // Webhook payloads expose only the received amount; Expected/Remaining are not available.
        provider.Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 113, "link", "PAID", "PAID", 100, 100, 0, null));

        (await CreateService(db, provider).ProcessWebhookAsync("{\"signature\":\"x\"}", CancellationToken.None)).Disposition.Should().Be(WebhookDisposition.Accepted);
        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("completed");
        (await db.UserPlans.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Webhook_UnknownStatus_IsNonDestructive()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        db.PaymentTransactions.Add(Payment(user.Id, 109, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 109, "link", "UNKNOWN", " strange ", 0, 100, 100, null));

        (await CreateService(db, provider).ProcessWebhookAsync("{\"signature\":\"x\"}", CancellationToken.None)).Disposition.Should().Be(WebhookDisposition.Ignored);
        var payment = await db.PaymentTransactions.SingleAsync();
        payment.Status.Should().Be("pending");
        payment.ProviderStatus.Should().Be("STRANGE");
    }

    [Test]
    public async Task Webhook_PaidWithMissingOwner_IsRetryableAndRetainsPaidProviderStatus()
    {
        using var db = TestDb.CreateInMemory();
        db.PaymentTransactions.Add(Payment(Guid.NewGuid(), 110, "pending"));
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        provider.Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(true, 110, "link", "PAID", "PAID", 100, 100, 0, null));

        (await CreateService(db, provider).ProcessWebhookAsync("{\"signature\":\"x\"}", CancellationToken.None)).Disposition.Should().Be(WebhookDisposition.RetryableFailure);
        var payment = await db.PaymentTransactions.SingleAsync();
        payment.Status.Should().Be("pending");
        payment.ProviderStatus.Should().Be("PAID");
    }

    [Test]
    public async Task Webhook_InvalidVerificationIsIgnored_UnexpectedVerificationFailureIsRetryable()
    {
        using var db = TestDb.CreateInMemory();
        var invalid = new Mock<IPaymentProvider>();
        invalid.Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookVerificationResult(false, 0, null, "UNKNOWN", "INVALID", 0, 0, 0, null));
        (await CreateService(db, invalid).ProcessWebhookAsync("{\"signature\":\"x\"}", CancellationToken.None)).Disposition.Should().Be(WebhookDisposition.Ignored);

        var broken = new Mock<IPaymentProvider>();
        broken.Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("temporary"));
        (await CreateService(db, broken).ProcessWebhookAsync("{\"signature\":\"x\"}", CancellationToken.None)).Disposition.Should().Be(WebhookDisposition.RetryableFailure);
    }

    [Test]
    public async Task CreatePayment_UsesCleanCallbacksAndRejectsProviderOrderCodeMismatch()
    {
        using var db = TestDb.CreateInMemory();
        var user = AddUser(db);
        await db.SaveChangesAsync();
        var provider = new Mock<IPaymentProvider>();
        PaymentRequest? captured = null;
        provider.Setup(p => p.CreatePaymentLinkAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PaymentLinkResult(true, "https://checkout.example.test", 1, "link", null));
        var service = CreateService(db, provider, "https://app.example.test");

        var action = () => service.CreatePaymentAsync(user.Id, "pro", "monthly", CancellationToken.None);
        await action.Should().ThrowAsync<PaymentProviderException>();
        captured!.ReturnUrl.Should().Be("https://app.example.test/payment/result");
        captured.CancelUrl.Should().Be(captured.ReturnUrl);
        captured.ReturnUrl.Should().NotContain("?");
        (await db.PaymentTransactions.SingleAsync()).Status.Should().Be("failed");
    }

    private static PaymentService CreateService(AppDbContext db, Mock<IPaymentProvider> provider, string? callbackBaseUrl = null)
    {
        provider.SetupGet(p => p.ProviderName).Returns("PayOS");
        var plans = new Mock<IPlanService>();
        plans.Setup(p => p.GetPlanByKey(It.IsAny<string>())).Returns(new Plan { Id = Guid.NewGuid(), PlanKey = "pro", DisplayName = "Pro", MonthlyPriceVnd = 100, YearlyPriceVnd = 1000 });
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(Environments.Development);
        return new PaymentService(provider.Object, db, plans.Object, new Mock<IAuditLogService>().Object,
            Microsoft.Extensions.Options.Options.Create(new AI_Study_Hub_v2.Options.PayOsSettings { ExpireMinutes = 2, CallbackBaseUrl = callbackBaseUrl ?? string.Empty }), config, environment.Object, NullLogger<PaymentService>.Instance);
    }

    private static User AddUser(AppDbContext db)
    {
        var user = new User { Id = Guid.NewGuid(), SupabaseUserId = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), FullName = "Test", IsActive = true };
        db.Users.Add(user);
        return user;
    }

    private static PaymentTransaction Payment(Guid userId, long orderCode, string status) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, TxnRef = $"PO_{orderCode}", ProviderOrderCode = orderCode,
        PlanKey = "pro", BillingCycle = "monthly", AmountVnd = 100, Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"isValid\":true,\"status\":\"pending\",\"amountVnd\":1}", Encoding.UTF8, "application/json")
            });
        }
    }
}
