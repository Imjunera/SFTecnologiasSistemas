using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Application.DTOs;
using SF.Tecnologias.Application.Services;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;
using SF.Tecnologias.Infrastructure.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure for Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SFTecnologiasApi";
});

// Configure Kestrel to listen on port 5000
builder.WebHost.UseUrls("http://localhost:5000");

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DbContext (supports both PostgreSQL and SQLite)
var dbProvider = builder.Configuration.GetValue<string>("DatabaseProvider") ?? "SQLite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (dbProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        // Default to SQLite for desktop distribution
        options.UseSqlite(connectionString);
    }
});

// Multi-tenant resolution (empresa context from the authenticated user's JWT claims)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();

// Development seed (idempotent, Development environment only)
builder.Services.AddScoped<DevelopmentSeeder>();

// Add application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProdutoService, ProdutoService>();
builder.Services.AddScoped<ICategoriaService, CategoriaService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IMesaService, MesaService>();
builder.Services.AddScoped<ICaixaService, CaixaService>();

// Add authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSettings["Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(jwtSecret))
    {
        jwtSecret = "dev-only-insecure-jwt-secret-do-not-use-in-production-0123456789";
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Secret ausente ou invalido (min 32 caracteres). " +
            "Em Production, configure a variavel de ambiente Jwt__Secret.");
    }
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(5),
            RequireSignedTokens = true
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("Token validated successfully");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Add CORS
builder.Services.AddCors(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    }
    else
    {
        options.AddPolicy("AllowAll", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:5173" };
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    }
});

var app = builder.Build();

// Apply migrations and seed data on startup (seeder is idempotent)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    if (db.Database.IsSqlite())
    {
        // SQLite: Use EnsureCreated for simplicity in desktop distribution
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        // PostgreSQL: Use migrations for proper versioning
        await db.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

// Error handling (before authentication)
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext context) =>
{
    var exceptionHandlerPathFeature =
        context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
    var error = exceptionHandlerPathFeature?.Error;

    var problemDetails = new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "An error occurred",
    };

    if (app.Environment.IsDevelopment())
    {
        problemDetails.Detail = error?.Message;
    }
    else
    {
        problemDetails.Detail = "An internal error occurred. Please try again later.";
    }

    return Results.Problem(problemDetails);
});

app.UseAuthentication();
app.UseAuthorization();

// Health endpoint
app.MapGet("/health", () => Results.Ok());

// Auth endpoint (no authorization required)
app.MapPost("/api/auth/login", async (IAuthService authService, LoginRequest request) =>
{
    try
    {
        var response = await authService.LoginAsync(request);
        if (response is null)
            return Results.Unauthorized();
        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Login failed",
            Detail = ex.Message
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Login failed",
            Detail = ex.Message
        });
    }
});

// Produtos endpoints (protegidos por autenticação e com isolamento multiempresa)
var produtosApi = app.MapGroup("/api/produtos").RequireAuthorization();

produtosApi.MapGet("/", async (IProdutoService service, [FromQuery] string? busca, [FromQuery] bool? apenasAtivos) =>
{
    try
    {
        var produtos = await service.ObterTodosAsync(busca, apenasAtivos ?? false);
        return Results.Ok(produtos);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

produtosApi.MapGet("/{id:int}", async (IProdutoService service, int id) =>
{
    try
    {
        var produto = await service.ObterPorIdAsync(id);
        return produto is not null ? Results.Ok(produto) : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

produtosApi.MapPost("/", async (IProdutoService service, CriarProdutoRequest request) =>
{
    try
    {
        var criado = await service.CriarAsync(request);
        return Results.Created($"/api/produtos/{criado.Id}", criado);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

produtosApi.MapPut("/{id:int}", async (IProdutoService service, int id, AtualizarProdutoRequest request) =>
{
    try
    {
        var atualizado = await service.AtualizarAsync(id, request);
        return Results.Ok(atualizado);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

produtosApi.MapDelete("/{id:int}", async (IProdutoService service, int id) =>
{
    try
    {
        var sucesso = await service.InativarAsync(id);
        return sucesso ? Results.NoContent() : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

produtosApi.MapDelete("/{id:int}/permanente", async (IProdutoService service, int id) =>
{
    try
    {
        var sucesso = await service.ExcluirAsync(id);
        return sucesso ? Results.NoContent() : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

// ============================================================
// CATEGORIAS ENDPOINTS
// ============================================================

var categoriasApi = app.MapGroup("/api/categorias").RequireAuthorization();

categoriasApi.MapGet("/", async (ICategoriaService service, [FromQuery] string? busca, [FromQuery] bool? apenasAtivos) =>
{
    try { return Results.Ok(await service.ObterTodosAsync(busca, apenasAtivos ?? false)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

categoriasApi.MapGet("/{id:int}", async (ICategoriaService service, int id) =>
{
    try { var c = await service.ObterPorIdAsync(id); return c is not null ? Results.Ok(c) : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

categoriasApi.MapPost("/", async (ICategoriaService service, CriarCategoriaRequest request) =>
{
    try { var c = await service.CriarAsync(request); return Results.Created($"/api/categorias/{c.Id}", c); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

categoriasApi.MapPut("/{id:int}", async (ICategoriaService service, int id, AtualizarCategoriaRequest request) =>
{
    try { return Results.Ok(await service.AtualizarAsync(id, request)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

categoriasApi.MapDelete("/{id:int}", async (ICategoriaService service, int id) =>
{
    try { return await service.InativarAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

categoriasApi.MapDelete("/{id:int}/permanente", async (ICategoriaService service, int id) =>
{
    try { return await service.ExcluirAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

// ============================================================
// CLIENTES ENDPOINTS
// ============================================================

var clientesApi = app.MapGroup("/api/clientes").RequireAuthorization();

clientesApi.MapGet("/", async (IClienteService service, [FromQuery] string? busca, [FromQuery] bool? apenasAtivos) =>
{
    try { return Results.Ok(await service.ObterTodosAsync(busca, apenasAtivos ?? false)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

clientesApi.MapGet("/{id:int}", async (IClienteService service, int id) =>
{
    try { var c = await service.ObterPorIdAsync(id); return c is not null ? Results.Ok(c) : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

clientesApi.MapPost("/", async (IClienteService service, CriarClienteRequest request) =>
{
    try { var c = await service.CriarAsync(request); return Results.Created($"/api/clientes/{c.Id}", c); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

clientesApi.MapPut("/{id:int}", async (IClienteService service, int id, AtualizarClienteRequest request) =>
{
    try { return Results.Ok(await service.AtualizarAsync(id, request)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

clientesApi.MapDelete("/{id:int}", async (IClienteService service, int id) =>
{
    try { return await service.InativarAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

clientesApi.MapDelete("/{id:int}/permanente", async (IClienteService service, int id) =>
{
    try { return await service.ExcluirAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

// ============================================================
// MESAS ENDPOINTS
// ============================================================

var mesasApi = app.MapGroup("/api/mesas").RequireAuthorization();

mesasApi.MapGet("/", async (IMesaService service, [FromQuery] string? busca, [FromQuery] bool? apenasAtivos) =>
{
    try { return Results.Ok(await service.ObterTodosAsync(busca, apenasAtivos ?? false)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

mesasApi.MapGet("/{id:int}", async (IMesaService service, int id) =>
{
    try { var m = await service.ObterPorIdAsync(id); return m is not null ? Results.Ok(m) : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

mesasApi.MapPost("/", async (IMesaService service, CriarMesaRequest request) =>
{
    try { var m = await service.CriarAsync(request); return Results.Created($"/api/mesas/{m.Id}", m); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

mesasApi.MapPut("/{id:int}", async (IMesaService service, int id, AtualizarMesaRequest request) =>
{
    try { return Results.Ok(await service.AtualizarAsync(id, request)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

mesasApi.MapDelete("/{id:int}", async (IMesaService service, int id) =>
{
    try { return await service.InativarAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

mesasApi.MapDelete("/{id:int}/permanente", async (IMesaService service, int id) =>
{
    try { return await service.ExcluirAsync(id) ? Results.NoContent() : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

// ============================================================
// CAIXA ENDPOINTS
// ============================================================

var caixaApi = app.MapGroup("/api/caixa");

caixaApi.MapGet("/sessao-atual", async (ICaixaService service) =>
{
    try { var s = await service.ObterSessaoAbertaAsync(); return s is not null ? Results.Ok(s) : Results.NotFound(); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
});

caixaApi.MapPost("/abrir", async (ICaixaService service, AbrirCaixaRequest request) =>
{
    try { var s = await service.AbrirAsync(request); return Results.Created($"/api/caixa/sessao-atual", s); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

caixaApi.MapPost("/fechar/{id:int}", async (ICaixaService service, int id, FecharCaixaRequest request) =>
{
    try { return Results.Ok(await service.FecharAsync(id, request)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

app.Run();
