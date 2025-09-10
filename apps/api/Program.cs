var builder = WebApplication.CreateBuilder(args);

// Add services to the container if needed

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "appostolic-api", version = "0.0.0" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
