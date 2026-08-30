using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Api.Data;
using OrderProcessingService.Api.Middleware;
using OrderProcessingService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<OrderDbContext>(o => o.UseInMemoryDatabase("OrderProcessing"));
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// Payment boundary = HTTP (satisfies "HTTP Client for inter-service communication").
builder.Services.AddHttpClient<IPaymentApiClient, PaymentApiClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Payment:BaseUrl"] ?? "http://localhost:5114"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    DbInitializer.Initialize(scope.ServiceProvider.GetRequiredService<OrderDbContext>());
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();