using CHITSCHEME.Middleware;
using CHITSCHEME.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Background notification scheduler (runs every 30 mins) ──
builder.Services.AddHostedService<NotificationSchedulerService>();

// ── Promotional broadcast scheduler (runs every 45 mins, time-aware messages) ──
builder.Services.AddHostedService<PromotionalNotificationService>();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "pukhrajchit",
        Version = "v1"
    });

    //  ---------------------------------JWT Authentication configuration for Swagger-------------------------------------
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter 'Bearer' followed by a space and your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// -----------------------------------------Allow CORS Platform --------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["Key"];
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };
});


var app = builder.Build();

app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

//app.UseRouting();
//app.MapControllers();
//app.UseStaticFiles(); // For serving HTML files

app.UseAuthentication();
app.UseAuthorization();

// ── Auto-update LastSeen on every authenticated API call ────
app.UseLastSeen();


app.UseSwagger();  
app.UseSwaggerUI(c =>
{
    var env = app.Environment.EnvironmentName;

    if (env == Environments.Production)
    {
        c.SwaggerEndpoint("/pukhrajchit/swagger/v1/swagger.json", "pukhrajchit v1");
    }
    else
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "pukhrajchit v1");
    }
});

app.MapControllers();

app.Run();
