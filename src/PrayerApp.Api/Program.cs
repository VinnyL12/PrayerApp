var builder = WebApplication.CreateBuilder(args);

// Register services in the DI container (think: wiring up dependencies before app.listen()).
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline (think: Express middleware, in order).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
