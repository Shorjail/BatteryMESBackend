using System.Text;
using BatteryMes.Api.Security;
using BatteryMes.Api.Services;
using BatteryMes.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;

namespace BatteryMes.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMesServices(this IServiceCollection services, IConfiguration config)
    {
        // ---------- Database (SqlSugar, code-first) ----------
        services.AddScoped<ISqlSugarClient>(_ => SqlSugarConfig.CreateClient(config));

        // ---------- Auth ----------
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddScoped<JwtTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                var jwt = config.GetSection("Jwt").Get<JwtOptions>()!;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();

        // ---------- Application services ----------
        services.AddScoped<UnitService>();
        services.AddScoped<AlarmService>();
        services.AddScoped<OeeService>();

        // Demo PLC simulator (dev only — see Simulator:* config)
        services.Configure<SimulatorOptions>(config.GetSection("Simulator"));
        services.AddSingleton<LineSimulator>();
        services.AddHostedService(sp => sp.GetRequiredService<LineSimulator>());

        // ---------- API docs ----------
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "BatteryMES API", Version = "v1" });
        });

        services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        return services;
    }
}
