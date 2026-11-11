using ApexPost.Web.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using ApexPost.Application;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using ApexPost.Web.Models;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using ApexPost.IntegrationTests.Authentication;
using Xunit;
using Microsoft.Extensions.Hosting;

namespace ApexPost.IntegrationTests.Integration;

public sealed class SecurityPipelineTests
{
    [Fact]
    public void Counter_and_money_order_request_models_do_not_expose_server_control_fields()
    {
        static string[] Names(Type type) => type.GetProperties().Select(property => property.Name).ToArray();
        var moneyOrderFields = Names(typeof(MoneyOrderViewModel));
        var retailFields = Names(typeof(AddRetailLineViewModel));
        var paymentFields = Names(typeof(CompleteTransactionViewModel));
        var voidFields = Names(typeof(VoidTransactionViewModel));
        var staffFields = Names(typeof(StaffEditViewModel));
        var branchFields = Names(typeof(BranchEditViewModel));
        Assert.NotNull(typeof(StaffEditViewModel).GetProperty("Id")?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), true).SingleOrDefault());
        Assert.NotNull(typeof(BranchEditViewModel).GetProperty("Id")?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), true).SingleOrDefault());
        Assert.DoesNotContain("Status", staffFields);
        Assert.DoesNotContain("Status", branchFields);
        foreach (var fields in new[] { moneyOrderFields, retailFields, paymentFields, voidFields, staffFields, branchFields })
        {
            Assert.DoesNotContain("Status", fields);
            Assert.DoesNotContain("CreatedAtUtc", fields);
            Assert.DoesNotContain("CreatedBy", fields);
            Assert.DoesNotContain("ModifiedAtUtc", fields);
        }
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/Account/Login");
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }

    [Fact]
    public async Task Audit_action_filter_persists_entries_to_sql_audit_store()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(x => x.Controller == "Account" && x.Action == "Login"));
    }

    [Fact]
    public async Task Integration_endpoint_returns_401_without_api_key()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/integration/parcel-status/not-found");
        response.StatusCode = HttpStatusCode.Unauthorized;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task Integration_endpoint_returns_500_when_partner_key_is_not_configured()
    {
        await using var factory = new ApexPostWebFactory(apiKey: null);
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/integration/parcel-status/not-found");
        response.StatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_login_form_is_redisplayed_as_html_not_json()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/Account/Login");
        var token = ExtractToken(html);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = "not-an-email",
            ["Password"] = "some-password",
            ["ReturnUrl"] = string.Empty
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partner_endpoint_returns_404_and_applies_partner_and_no_cache_headers()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", "integration-test-secret");
        using var response = await client.GetAsync("/integration/parcel-status/unknown-shipment");
        response.StatusCode = HttpStatusCode.NotFound;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Integration_endpoint_returns_403_from_blocked_ip()
    {
        await using var factory = new ApexPostWebFactory("203.0.113.10");
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/integration/parcel-status/not-found");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_endpoint_returns_429_after_limit_is_exceeded()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/Account/Login");
        var token = ExtractToken(html);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Email"] = "nobody@example.test", ["Password"] = "WrongPassword!123", ["ReturnUrl"] = "" }));
        }
        Assert.NotNull(last);
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        last!.Dispose();
    }

    [Fact]
    public async Task Unauthenticated_access_redirects_to_login()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/Dashboard/Index");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Counter_sale_post_persists_lines_payment_and_decrements_stock_only_at_payment()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });

        var loginHtml = await client.GetStringAsync("/Account/Login");
        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(loginHtml),
            ["Email"] = "counterclerk1@apexpost.local",
            ["Password"] = "CounterTestPassword!123",
            ["ReturnUrl"] = string.Empty
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        string clerkId;
        int originalQuantity;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            clerkId = await db.Staff.AsNoTracking().Where(x => x.Email == "counterclerk1@apexpost.local").Select(x => x.Id).SingleAsync();
            originalQuantity = (await db.BranchStock.AsNoTracking().SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001")).Quantity;
        }

        var indexHtml = await client.GetStringAsync("/Transactions/Index");
        using var create = await client.PostAsync("/Transactions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(indexHtml)
        }));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        string transactionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            transactionId = await db.CounterTransactions.AsNoTracking()
                .Where(x => x.ClerkId == clerkId && x.Status == TransactionStatus.Open)
                .OrderByDescending(x => x.CreatedAt).Select(x => x.Id).FirstAsync();
        }

        var detailsHtml = await client.GetStringAsync($"/Transactions/Details/{transactionId}");
        using var addLine = await client.PostAsync("/Transactions/AddRetail", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(detailsHtml),
            ["TransactionId"] = transactionId,
            ["Sku"] = "APX-001",
            ["Quantity"] = "1"
        }));
        Assert.Equal(HttpStatusCode.Redirect, addLine.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            var stockBeforePayment = await db.BranchStock.AsNoTracking().SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001");
            Assert.Equal(originalQuantity, stockBeforePayment.Quantity);
        }

        detailsHtml = await client.GetStringAsync($"/Transactions/Details/{transactionId}");
        using var complete = await client.PostAsync("/Transactions/Complete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(detailsHtml),
            ["TransactionId"] = transactionId,
            ["PaymentMethod"] = "Cash"
        }));
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            var transaction = await db.CounterTransactions.AsNoTracking().SingleAsync(x => x.Id == transactionId);
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.CounterTransactionId == transactionId);
            var stockAfterPayment = await db.BranchStock.AsNoTracking().SingleAsync(x => x.BranchCode == "IND01" && x.Sku == "APX-001");
            Assert.Equal(TransactionStatus.Paid, transaction.Status);
            Assert.Equal(1, await db.CounterTransactions.AsNoTracking().Where(x => x.Id == transactionId).SelectMany(x => x.Lines).CountAsync());
            Assert.Equal(transaction.Total, payment.Amount);
            Assert.Equal(originalQuantity - 1, stockAfterPayment.Quantity);
        }
    }

    [Fact]
    public async Task Auditor_report_screen_displays_stored_procedure_totals()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var expectedRows = new List<DailySalesRow>();
        DateOnly reportDay;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var sample = await db.CounterTransactions.AsNoTracking().FirstAsync(x => x.Status == TransactionStatus.Paid && x.CompletedAt != null);
            reportDay = DateOnly.FromDateTime(sample.CompletedAt!.Value.UtcDateTime);
            expectedRows.AddRange(await reports.GetDailySalesAsync(reportDay, null));
        }

        var loginHtml = await client.GetStringAsync("/Account/Login");
        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(loginHtml),
            ["Email"] = "auditor3@apexpost.local",
            ["Password"] = "AuditorTestPassword!123",
            ["ReturnUrl"] = string.Empty
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync($"/Reports/DailySales?day={reportDay:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var row in expectedRows)
        {
            Assert.Contains(row.BranchCode, html, StringComparison.Ordinal);
            Assert.Contains(row.TotalSales.ToString("N2"), html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Not_found_exception_returns_problem_details_with_trace_id()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var loginHtml = await client.GetStringAsync("/Account/Login");
        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(loginHtml),
            ["Email"] = "counterclerk1@apexpost.local",
            ["Password"] = "CounterTestPassword!123",
            ["ReturnUrl"] = string.Empty
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var indexHtml = await client.GetStringAsync("/Transactions/Index");
        using var response = await client.PostAsync("/Transactions/Complete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractToken(indexHtml),
            ["TransactionId"] = "00000000000000000000000000000000",
            ["PaymentMethod"] = "Cash"
        }));

        Assert.Equal(response.StatusCode, response.StatusCode);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ApexPost.IntegrationTests";
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task Clerk_receives_403_when_posting_void()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/Account/Login");
        var token = ExtractToken(html);
        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Email"] = "counterclerk1@apexpost.local", ["Password"] = "CounterTestPassword!123", ["ReturnUrl"] = "" }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var response = await client.PostAsync("/Transactions/Void", new FormUrlEncodedContent(new Dictionary<string, string> { ["TransactionId"] = "anything", ["Reason"] = "Test void" }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_returns_503_with_retry_after_but_keeps_health_available()
    {
        await using var factory = new ApexPostWebFactory(maintenanceEnabled: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var maintenance = await client.GetAsync("/Dashboard/Index");
        Assert.Equal((HttpStatusCode)503, maintenance.StatusCode);
        Assert.True(maintenance.Headers.Contains("Retry-After"));
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        await using var factory = new ApexPostWebFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Add("X-Correlation-Id", "assessment-test-123");
        using var response = await client.SendAsync(request);
        Assert.Equal("assessment-test-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    private static string ExtractToken(string html)
    {
        var match = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(html, "value=\\\"([^\\\"]+)\\\"[^>]*name=\\\"__RequestVerificationToken\\\"", RegexOptions.IgnoreCase);
        }

        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
