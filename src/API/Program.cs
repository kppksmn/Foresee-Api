using System.Text;
using API.APIs.v1.Admin;
using API.APIs.v1.Auth;
using API.APIs.v1.Mobile;
using API.Security;
using Core.Interfaces;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient();

// DI Configuration
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<UserProfileRepository>();
builder.Services.AddScoped<JobRepository>();
builder.Services.AddScoped<AuditLogRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PushNotificationService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ICurrentDriver, CurrentDriver>();

// Authentication & Authorization Setup
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_123456";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ForeseeAPI",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ForeseeClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MobileAuthenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("MobileDriver", policy => policy.RequireAuthenticatedUser().RequireRole("Driver", "Admin"));
    options.AddPolicy("MobileAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});

builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Auto-initialize DB schema and Seed default Admin
try
{
    Console.WriteLine("[DB-INIT-DEBUG] Starting DB Initialization...");
    using var scope = app.Services.CreateScope();
    var dbFactory = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
    using var conn = dbFactory.CreateConnection();
    await DbInitializer.InitializeAsync(conn);
    Console.WriteLine("[DB-INIT-DEBUG] DB Initialization finished successfully.");
}
catch (Exception ex)
{
    Console.WriteLine($"[DB-INIT-ERROR] Failed to initialize DB: {ex.Message} | Stack: {ex.StackTrace}");
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Foresee API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("DefaultPolicy");
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Route mappings
app.MapAuthEndpoints();
app.MapMobileEndpoints();
app.MapAdminEndpoints();

app.Run();

public partial class Program { }
