using BatteryMes.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMesServices(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "https://batterymes.vercel.app",
                "https://batterymes.shorjail.com" // <-- Add your new subdomain here
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
var app = builder.Build();

// Middleware order matters: exception handler wraps everything,
// request logging sees the final status code.
app.UseMiddleware<BatteryMes.Api.Middleware.ExceptionHandlingMiddleware>();
app.UseMiddleware<BatteryMes.Api.Middleware.RequestLoggingMiddleware>();

// Swagger available in all environments (internal API — restrict via network if needed)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BatteryMES API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new { service = "BatteryMES API", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

// Code-first schema init + seed data at startup.
// Non-fatal: the API still serves Swagger/UI if the DB is unreachable.
try
{
    using var scope = app.Services.CreateScope();
    await BatteryMes.Api.Data.DatabaseSeeder.InitializeAsync(scope.ServiceProvider);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database initialization failed — API will run without DB. Check Database:* settings.");
}

app.Run();
