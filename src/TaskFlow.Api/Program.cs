var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Stage 0 intentionally contains no public API contract yet.
// HTTP endpoints are introduced in stage 7 after Domain/Application/Persistence exist.

app.Run();
