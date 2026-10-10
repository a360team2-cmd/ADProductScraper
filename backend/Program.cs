using ADProductScraper.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Respect Visual Studio launch profiles and command-line --urls; keep a local-only fallback.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5080");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
{
    Title = "AD Product Scraper API",
    Version = "v1",
    Description = "Local product collections and Excel exports. Start Angular separately with ng serve on localhost:4200."
}));
builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
builder.Services.AddSingleton<SafeWeb>();
builder.Services.AddSingleton<EtsyClient>();
builder.Services.AddSingleton<Jobs>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Jobs>());
var app = builder.Build();
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    app.Logger.LogError(e.Exception, "Unobserved background task failure");
    e.SetObserved();
};
app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host is not ("localhost" or "127.0.0.1")) { context.Response.StatusCode = 403; return; }
    if (context.Request.Method is "POST" or "DELETE" or "PUT")
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin is not ("http://localhost:5080" or "http://127.0.0.1:5080" or "http://localhost:4200" or "http://127.0.0.1:4200")) { context.Response.StatusCode = 403; return; }
    }
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AD Product Scraper API v1");
        options.RoutePrefix = "swagger";
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.MapGet("/api/health", () => new { status = "ok" });
app.MapGet("/api/etsy/status", (EtsyClient etsy) => new { configured = etsy.Configured });
app.MapGet("/api/jobs", (Jobs jobs) => jobs.List());
app.MapPost("/api/jobs", (StartRequest request, Jobs jobs) => Results.Ok(new { id = jobs.Create(request) }));
app.MapGet("/api/jobs/{id}", (string id, Jobs jobs) => jobs.Get(id) is { } job ? Results.Ok(job) : Results.NotFound());
app.MapGet("/api/jobs/{id}/products", (string id, int? skip, int? take, Jobs jobs) => jobs.Products(id, Math.Max(0, skip ?? 0), Math.Clamp(take ?? 50, 1, 200)) is { } products ? Results.Ok(products) : Results.NotFound());
app.MapPost("/api/jobs/{id}/cancel", (string id, Jobs jobs) => jobs.Cancel(id) ? Results.Ok() : Results.NotFound());
app.MapPost("/api/jobs/{id}/resume", (string id, Jobs jobs) => jobs.Resume(id) ? Results.Ok() : Results.NotFound());
app.MapGet("/api/jobs/{id}/excel", (string id, Jobs jobs) =>
{
    var products = jobs.Products(id);
    if (products == null) return Results.NotFound();
    if (products.Length == 0) return Results.BadRequest(new { error = "No products have been collected yet." });
    return Results.File(ExcelExport.Build(products, jobs.Status(id)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"products-{id[..8]}.xlsx");
});
if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("index.html");
app.Run();

