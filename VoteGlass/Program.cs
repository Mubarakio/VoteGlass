using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VoteGlass.Components;
using VoteGlass.Components.Account;
using VoteGlass.Data;
using VoteGlass.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(
    options => options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped);

builder.Services.AddScoped<VoterProfileService>();
builder.Services.AddScoped<ElectionService>();
builder.Services.AddScoped<BallotService>();
builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<VoterElectionService>();
builder.Services.AddScoped<IAppEmailSender, TestEmailSender>();   // TEST MODE. Swap for the Gmail sender later.
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<VotingService>();
builder.Services.AddSingleton<ResultsNotifier>();
builder.Services.AddScoped<ResultsService>();
builder.Services.AddScoped<PlatformService>();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<OrgContext>();
builder.Services.AddScoped<IAuthorizationHandler, OrgAdminHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("OrgAdmin", policy => policy.AddRequirements(new OrgAdminRequirement()))
    .AddPolicy("OrgOwner", policy => policy.AddRequirements(new OrgAdminRequirement(ownerOnly: true)))
    .AddPolicy("MainAdmin", policy => policy.RequireAssertion(context =>
    {
        var mainEmail = builder.Configuration["SeedAdmin:Email"];
        var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;

        return context.User.IsInRole("Admin")
            && !string.IsNullOrWhiteSpace(mainEmail)
            && string.Equals(email, mainEmail, StringComparison.OrdinalIgnoreCase);
    }));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
    await scope.ServiceProvider.GetRequiredService<OrganizationService>().BackfillJoinCodesAsync();
}

app.Run();
