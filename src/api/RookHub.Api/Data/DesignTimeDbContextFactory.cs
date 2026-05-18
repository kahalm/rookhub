using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RookHub.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = "Server=localhost;Port=3307;Database=rookhub;User=rookhub;Password=rookhub_secret;";
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(11, 0, 0)));
        return new AppDbContext(optionsBuilder.Options);
    }
}
