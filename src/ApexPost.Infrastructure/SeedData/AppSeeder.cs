using ApexPost.Application;
using ApexPost.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApexPost.Infrastructure;

public sealed class AppSeeder(
    IRepository<Branch> branches, IRepository<Staff> staff, IRepository<Product> products,
    IRepository<StockItem> stock, IRepository<ShipmentRate> rates, IRepository<CounterTransaction> transactions,
    IRepository<Shipment> shipments, IRepository<MoneyOrder> moneyOrders, IRepository<IdentityVerification> verifications, IRepository<Payment> payments,
    IPasswordService passwords, IConfiguration configuration, IHostEnvironment environment, ILogger<AppSeeder> logger, IUnitOfWork unitOfWork) : IAppSeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingBranches = await branches.GetAllAsync(cancellationToken);
        if (existingBranches.Count > 0)
        {
            // If the first local run happened before secrets were configured, provision hashes without resetting any data.
            foreach (var existingStaff in await staff.GetAllAsync(cancellationToken))
            {
                var configuredPassword = configuration[$"DemoPasswords:{existingStaff.Role}"];
                if (existingStaff.PasswordHash == "UNCONFIGURED" && !string.IsNullOrWhiteSpace(configuredPassword))
                {
                    // Hash each account separately so every password receives a unique random salt.
                    existingStaff.PasswordHash = passwords.Hash(configuredPassword);
                    await staff.UpdateAsync(existingStaff, cancellationToken);
                }
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Existing ApexPost data detected; sample records were preserved."); return;
        }
        if (!environment.IsDevelopment() && !configuration.GetValue<bool>("Seed:Enabled")) { logger.LogInformation("Seeding is disabled outside Development."); return; }
        var branchCodes = new[] { "IND01", "DEL01", "MUM01", "BLR01", "CHN01" };
        var branchList = branchCodes.Select((code, i) => new Branch { Code = code, Name = new[] { "Indore Central", "Delhi North", "Mumbai Central", "Bengaluru East", "Chennai Central" }[i], Region = new[] { "Central", "North", "West", "South", "South" }[i], OpeningHours = "09:00-17:00", Status = code == "CHN01" ? BranchStatus.Closed : BranchStatus.Active, Counters = [new Counter($"{code}-C1", "Counter 1"), new Counter($"{code}-C2", "Counter 2")] }).ToArray();
        foreach (var branch in branchList) await branches.AddAsync(branch, cancellationToken);

        var roleNames = Enum.GetValues<StaffRole>();
        for (var i = 0; i < 20; i++)
        {
            var role = roleNames[i % roleNames.Length];
            var email = $"{role.ToString().ToLowerInvariant()}{i + 1}@apexpost.local";
            var passwordKey = $"DemoPasswords:{role}";
            var configuredPassword = configuration[passwordKey];
            var hash = string.IsNullOrWhiteSpace(configuredPassword) ? "UNCONFIGURED" : passwords.Hash(configuredPassword);
            await staff.AddAsync(new Staff { Name = $"Demo Staff {i + 1:00}", Email = email, Role = role, HomeBranchCode = branchCodes[i % branchCodes.Length], Status = i == 18 ? StaffStatus.Inactive : StaffStatus.Active, PasswordHash = hash }, cancellationToken);
        }

        var categories = Enum.GetValues<ProductCategory>(); var taxes = Enum.GetValues<TaxCategory>();
        for (var i = 1; i <= 30; i++)
        {
            var product = new Product { Sku = $"APX-{i:000}", Name = new[] { "Parcel Box", "Padded Envelope", "Tape Roll", "A4 Paper", "Ballpoint Pen", "Document Wallet" }[(i - 1) % 6] + $" {i:00}", Category = categories[(i - 1) % categories.Length], UnitPrice = decimal.Round(15m + i * 3.25m, 2), TaxCategory = taxes[(i - 1) % taxes.Length] };
            await products.AddAsync(product, cancellationToken);
            foreach (var code in branchCodes) await stock.AddAsync(StockItem.Create(code, product.Sku, i % 7 == 0 ? 3 : 12 + i % 13), cancellationToken);
        }

        for (var z = 1; z <= 10; z++)
        foreach (var level in Enum.GetValues<ServiceLevel>())
            await rates.AddAsync(new ShipmentRate($"ZONE-{z:00}", level, 45m + z * 5m, 12m + z, level == ServiceLevel.Standard ? 20m : level == ServiceLevel.Express ? 10m : 15m), cancellationToken);

        // Commit the reference/catalogue stage before querying the generated staff IDs.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var staffList = await staff.GetAllAsync(cancellationToken);
        for (var i = 0; i < 200; i++)
        {
            var branchCode = branchCodes[i % branchCodes.Length]; var branch = branchList[i % branchList.Length];
            var clerk = staffList.First(x => x.Role == StaffRole.CounterClerk && x.HomeBranchCode == branchCode);
            var isRetail = i % 2 == 0;
            var line = new TransactionLine(Guid.NewGuid().ToString("N"), isRetail ? LineKind.RetailProduct : LineKind.Shipping, isRetail ? "Historical retail sale" : "Historical shipping service", isRetail ? $"APX-{i % 30 + 1:000}" : null, 1, 25m + i, isRetail ? 0.18m : 0m, 25m + i + (isRetail ? (25m + i) * 0.18m : 0m));
            var tx = new CounterTransaction { BranchCode = branchCode, CounterId = branch.Counters[0].Id, ClerkId = clerk.Id, Lines = [line], Subtotal = line.UnitPrice, Tax = line.UnitPrice * line.TaxRate, Total = line.LineTotal, PaymentMethod = PaymentMethod.Cash, Status = TransactionStatus.Paid, CreatedAt = DateTimeOffset.UtcNow.AddDays(-(i % 30)), CompletedAt = DateTimeOffset.UtcNow.AddDays(-(i % 30)) };
            await transactions.AddAsync(tx, cancellationToken);
            await payments.AddAsync(new Payment { CounterTransactionId = tx.Id, Method = PaymentMethod.Cash, Amount = tx.Total, PaidAtUtc = tx.CompletedAt ?? tx.CreatedAt }, cancellationToken);
            if (!isRetail) await shipments.AddAsync(new Shipment
            {
                BranchCode = branchCode,
                Kind = ShipmentKind.Parcel,
                WeightKg = 1m + i % 5,
                // Keep seeded dimensions within the same positive-centimetre invariant
                // enforced by CK_Shipments_Dimensions and the shipping domain rules.
                LengthCm = 30m + (i % 10),
                WidthCm = 20m + (i % 5),
                HeightCm = 10m + (i % 4),
                DestinationZone = $"ZONE-{i % 10 + 1:00}",
                ServiceLevel = Enum.GetValues<ServiceLevel>()[i % 3],
                Price = line.UnitPrice,
                CreatedAt = tx.CreatedAt
            }, cancellationToken);
        }
        for (var i = 0; i < 10; i++)
        {
            var branchCode = branchCodes[i % branchCodes.Length];
            var branch = branchList[i % branchList.Length];
            var amount = 500m + i * 100m;
            var fee = 25m;
            var clerk = staffList.First(x => x.Role == StaffRole.CounterClerk && x.HomeBranchCode == branchCode);
            var transaction = new CounterTransaction
            {
                BranchCode = branchCode, CounterId = branch.Counters[0].Id, ClerkId = clerk.Id,
                Lines = [new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.MoneyOrder, $"Historical money order to Demo Recipient {i + 1}", null, 1, amount + fee, 0m, amount + fee)],
                Subtotal = amount + fee, Tax = 0m, Total = amount + fee, PaymentMethod = PaymentMethod.Cash,
                Status = TransactionStatus.Paid, CreatedAt = DateTimeOffset.UtcNow.AddDays(-i), CompletedAt = DateTimeOffset.UtcNow.AddDays(-i)
            };
            await transactions.AddAsync(transaction, cancellationToken);
            await payments.AddAsync(new Payment { CounterTransactionId = transaction.Id, Method = PaymentMethod.Cash, Amount = transaction.Total, PaidAtUtc = transaction.CompletedAt ?? transaction.CreatedAt }, cancellationToken);
            var verification = new IdentityVerification
            {
                DocumentType = IdentityDocumentType.NationalId, MaskedDocumentNumber = $"****{i + 1000}",
                Result = i % 5 == 0 ? VerificationResult.Failed : VerificationResult.Successful,
                VerifiedByStaffId = clerk.Id, BranchCode = branchCode, CounterTransactionId = transaction.Id,
                VerifiedAt = transaction.CreatedAt
            };
            await verifications.AddAsync(verification, cancellationToken);
            await moneyOrders.AddAsync(new MoneyOrder
            {
                Amount = amount, Sender = $"Demo Sender {i + 1}", Recipient = $"Demo Recipient {i + 1}", Fee = fee,
                BranchCode = branchCode, CounterTransactionId = transaction.Id, Status = MoneyOrderStatus.Issued,
                IdentityVerificationId = verification.Id, CreatedAt = transaction.CreatedAt
            }, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("ApexPost development seed data initialized with 200 historical transactions.");
    }
}
