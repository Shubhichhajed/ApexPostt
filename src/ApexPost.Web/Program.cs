using System.Threading.RateLimiting;
using ApexPost.Application;
using ApexPost.Domain;
using ApexPost.Infrastructure;
using ApexPost.Web.Authorization;
using ApexPost.Web.Filters;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ApexPost.Infrastructure.Data;
using ApexPost.Infrastructure.Repositories.EfCore;
using ApexPost.Infrastructure.Audit;
using ApexPost.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<AuditActionFilter>();
    options.Filters.AddService<ValidationActionFilter>();
    options.Filters.AddService<PerformanceActionFilter>();
});
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
var connectionString = builder.Configuration.GetConnectionString("ApexPost");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Application Name=\"SQL Server Management Studio\";Command Timeout=0");
}
builder.Services.AddSingleton<ChangeTrackingOutboxInterceptor>();
builder.Services.AddDbContextFactory<ApexPostDbContext>((services, options) =>
{
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    options.AddInterceptors(services.GetRequiredService<ChangeTrackingOutboxInterceptor>());
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors().LogTo(Console.WriteLine, LogLevel.Information);
    }
});
builder.Services.AddScoped(typeof(IRepository<>), typeof(EFRepository<>));
builder.Services.AddScoped<EfIdentityVerificationRepository>();
builder.Services.AddScoped<IRepository<IdentityVerification>>(services => services.GetRequiredService<EfIdentityVerificationRepository>());
builder.Services.AddScoped<IIdentityVerificationRepository>(services => services.GetRequiredService<EfIdentityVerificationRepository>());
builder.Services.AddScoped<IBranchRepository, EfBranchRepository>();
builder.Services.AddScoped<IProductRepository, EfProductRepository>();
builder.Services.AddScoped<IStockRepository, EfStockRepository>();
builder.Services.AddScoped<ICounterTransactionRepository, EfCounterTransactionRepository>();
builder.Services.AddScoped<IStaffRepository, EfStaffRepository>();
builder.Services.AddScoped<IShipmentRepository, EfShipmentRepository>();
builder.Services.AddScoped<IMoneyOrderRepository, EfMoneyOrderRepository>();
builder.Services.AddScoped<IPaymentRepository, EfPaymentRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IStockAdjustmentService, EfStockAdjustmentService>();
builder.Services.AddScoped<IOfflineSyncService, OfflineSyncService>();
builder.Services.AddScoped<IProductPriceRevisionService, EfProductPriceRevisionService>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddScoped<IAuditStore, EfAuditStore>();
builder.Services.AddSingleton<IPasswordService, Pbkdf2PasswordService>();
builder.Services.AddSingleton<DomainNotifications>();
builder.Services.AddSingleton<IShippingPricingStrategy, ZoneRateShippingPricingStrategy>();
builder.Services.AddSingleton<IRateCache, RateCache>();
builder.Services.AddScoped<ICourierService, SimulatedCourierService>();
builder.Services.AddSingleton(new LowStockRule(builder.Configuration.GetValue("Inventory:LowStockThreshold", 5)));
builder.Services.AddSingleton(new MoneyOrderPolicy(builder.Configuration.GetValue("MoneyOrders:IdentityThreshold", 1000m)));
builder.Services.AddSingleton<LowStockSpecification>();
builder.Services.AddScoped<IAppSeeder, AppSeeder>();
builder.Services.AddScoped<ICounterService, CounterService>();
builder.Services.AddScoped<IBranchAdministrationService, BranchAdministrationService>();
builder.Services.AddScoped<IMoneyOrderService, MoneyOrderService>();
builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationService>();
builder.Services.AddScoped<IReportService, EfReportService>();
builder.Services.AddScoped<IPlatformQueryService, EfPlatformQueryService>();
builder.Services.AddScoped<AuditActionFilter>();
builder.Services.AddScoped<ValidationActionFilter>();
builder.Services.AddScoped<PerformanceActionFilter>();
builder.Services.AddSingleton<IAuthorizationHandler, SameBranchAuthorizationHandler>();

builder.Services.AddAuthentication("ApexPostCookie").AddCookie("ApexPostCookie", options =>
{
    options.Cookie.Name = "ApexPost.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToAccessDenied = context =>
        {
            if (HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BranchManagerOnly", policy => policy.RequireRole(nameof(StaffRole.BranchManager), nameof(StaffRole.Administrator)));
    options.AddPolicy("SameBranchOnly", policy => policy.RequireAuthenticatedUser().AddRequirements(new SameBranchRequirement()));
});
builder.Services.AddCors(options => options.AddPolicy("PartnerWidget", policy =>
{
    var origin = builder.Configuration["Integration:PartnerWidgetOrigin"];
    if (!string.IsNullOrWhiteSpace(origin))
    {
        policy.WithOrigins(origin).WithMethods("GET").WithHeaders("X-Api-Key", "X-Partner-Id", "Accept");
    }
    else
    {
        policy.SetIsOriginAllowed(_ => false);
    }
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = 429, Title = "Too many requests. Try again later." }, cancellationToken: token);
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("partner", context =>
    {
        var partner = context.Items["AuthenticatedPartnerId"] as string ?? "unvalidated-partner";
        return RateLimitPartition.GetFixedWindowLimiter(partner, _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();

// Keep middleware ordering intentional: correlation -> safe errors -> telemetry -> headers -> maintenance -> routing -> cross-cutting access controls.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestTimingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<MaintenanceModeMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseMiddleware<IpRestrictionMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Restricted MVC routes receive an explicit no-cache policy after authorization.
app.UseWhen(context => context.GetEndpoint()?.Metadata.GetMetadata<RestrictedEndpointAttribute>() is not null, branch => branch.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        return Task.CompletedTask;
    });
    await next();
}));
// Partner routes receive a response marker and structured diagnostic after API-key validation.
app.UseWhen(context => context.GetEndpoint()?.Metadata.GetMetadata<RequireApiKeyAttribute>() is not null, branch => branch.Use(async (context, next) =>
{
    app.Logger.LogDebug("Partner integration request {Path}; CorrelationId={CorrelationId}", context.Request.Path, context.TraceIdentifier);
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-ApexPost-Integration"] = "partner";
        return Task.CompletedTask;
    });
    await next();
}));
// Map + Run demonstrate a terminal liveness branch; the standard /health endpoint remains available during maintenance.
app.Map("/health/live", branch => branch.Run(async context =>
{
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new { status = "Healthy", traceId = context.TraceIdentifier }, cancellationToken: context.RequestAborted);
}));
app.MapHealthChecks("/health", new HealthCheckOptions());
app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

var notificationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApexPostNotifications");
var notifications = app.Services.GetRequiredService<DomainNotifications>();
notifications.StockLow += stock => notificationLogger.LogWarning("StockLow notification: branch={BranchCode}, SKU={Sku}, quantity={Quantity}", stock.BranchCode, stock.Sku, stock.Quantity);
notifications.MoneyOrderIssued += order => { if (order.Amount > app.Configuration.GetValue("MoneyOrders:IdentityThreshold", 1000m)) { notificationLogger.LogWarning("High-value money order notification: id={MoneyOrderId}, branch={BranchCode}, amount={Amount}", order.Id, order.BranchCode, order.Amount); } else { notificationLogger.LogInformation("MoneyOrderIssued notification: id={MoneyOrderId}, branch={BranchCode}, amount={Amount}", order.Id, order.BranchCode, order.Amount); } };
notifications.TransactionCompleted += transaction => notificationLogger.LogInformation("TransactionCompleted notification: id={TransactionId}, branch={BranchCode}, total={Total}", transaction.Id, transaction.BranchCode, transaction.Total);
notifications.TransactionVoided += transaction => notificationLogger.LogWarning("TransactionVoided notification: id={TransactionId}, branch={BranchCode}, reasonProvided={HasReason}", transaction.Id, transaction.BranchCode, !string.IsNullOrWhiteSpace(transaction.Reason));
notifications.TransactionRefunded += transaction => notificationLogger.LogWarning("TransactionRefunded notification: id={TransactionId}, branch={BranchCode}, reasonProvided={HasReason}", transaction.Id, transaction.BranchCode, !string.IsNullOrWhiteSpace(transaction.Reason));

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        await db.Database.MigrateAsync();
    }
    var seeder = scope.ServiceProvider.GetRequiredService<IAppSeeder>();
    await seeder.SeedAsync();
    if (app.Environment.IsDevelopment())
    {
        var diagnostics = EfQueryDiagnostics.Capture(db, app.Configuration.GetValue("Inventory:LowStockThreshold", 5));
        var queryLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ApexPost.EFCoreQuerySamples");
        foreach (var query in diagnostics)
        {
            queryLogger.LogInformation("EF Core ToQueryString sample {QueryName}: {Sql}", query.Key, query.Value);
        }
    }
}

app.Run();

public partial class Program { }
