using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Infrastructure.Data;

public sealed class ApexPostDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApexPostDbContext>
{
    public ApexPostDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<ApexPostDesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = configuration["APEXPOST_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("ApexPost")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer(connection).Options;
        return new ApexPostDbContext(options);
    }
}
