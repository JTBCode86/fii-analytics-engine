using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using FiiAnalytics.Application.Interfaces;
using FiiAnalytics.Application.Queries;
using FiiAnalytics.Application.UseCases;
using FiiAnalytics.Domain.Interfaces;
using FiiAnalytics.Infrastructure.Repositories;
using FiiAnalytics.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuração dos Clientes AWS (LocalStack)
var serviceUrl = builder.Configuration["AWS:ServiceUrl"] ?? "http://localstack:4566";

builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true }));
builder.Services.AddSingleton<IAmazonSQS>(new AmazonSQSClient(new AmazonSQSConfig { ServiceURL = serviceUrl }));
builder.Services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = serviceUrl }));

// 2. Registro dos Serviços de Infraestrutura
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IQueueService, QueueService>();

// 3. Registro dos Repositórios (Domain Contracts)
builder.Services.AddScoped<IFiiRepository, FiiRepository>();

// 4. Registro da camada de Aplicação (UseCases e Queries)
builder.Services.AddScoped<ImportarCarteiraUseCase>();
builder.Services.AddScoped<GetRentabilidadeCarteiraHandler>();
builder.Services.AddScoped<GetAtivoConsolidadoHandler>();

// 5. Configuração da API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (KeyNotFoundException ex)
    {
        context.Response.StatusCode = 404; // Not Found
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.UseAuthorization();
app.MapControllers();

app.Run();