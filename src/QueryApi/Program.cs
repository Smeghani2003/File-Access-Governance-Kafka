using FileAccessGovernance.QueryApi.Data;
using FileAccessGovernance.QueryApi.Middleware;
using FileAccessGovernance.QueryApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<FileAccessGovernanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FileAccessGovernance")));

builder.Services.AddScoped<IFsObjectRepository, EfFsObjectRepository>();
builder.Services.AddScoped<ISidNameCacheRepository, EfSidNameCacheRepository>();
builder.Services.AddScoped<IFolderAccessService, FolderAccessService>();
builder.Services.AddScoped<ISidNameResolver, SidNameResolver>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<LdapOptions>(builder.Configuration.GetSection("Ldap"));
if (builder.Configuration.GetValue<bool>("Ldap:Enabled"))
{
    builder.Services.AddScoped<ISidDirectoryLookup, LdapSidDirectoryLookup>();
}
else
{
    // Local dev / no AD reachable — see design doc §7.
    builder.Services.AddScoped<ISidDirectoryLookup, NullSidDirectoryLookup>();
}

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests — see /tests/QueryApi.Tests.
public partial class Program { }
