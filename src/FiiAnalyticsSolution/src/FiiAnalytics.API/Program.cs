using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using FiiAnalytics.Application.Interfaces;
using FiiAnalytics.Application.UseCases;
using FiiAnalytics.Domain.Interfaces;
using FiiAnalytics.Infrastructure.Repositories;
using FiiAnalytics.Infrastructure.Services;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuração dos Clientes da AWS para apontar para o LocalStack localmente
var awsOptions = builder.Configuration.GetAWSOptions();
var serviceUrl = builder.Configuration["AWS:ServiceUrl"] ?? "http://localstack:4566";

var s3Config = new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true };
var sqsConfig = new AmazonSQSConfig { ServiceURL = serviceUrl };
var dynamoConfig = new AmazonDynamoDBConfig { ServiceURL = serviceUrl };

builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(s3Config));
builder.Services.AddSingleton<IAmazonSQS>(new AmazonSQSClient(sqsConfig));
builder.Services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(dynamoConfig));

// 2. Registro dos Serviços de Infraestrutura
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IQueueService, QueueService>();

// 3. Registro dos Repositórios (Domain Contracts)
builder.Services.AddScoped<IFiiRepository, FiiRepository>();

// 4. Registro dos Casos de Uso (Application Layer)
builder.Services.AddScoped<ImportarCarteiraUseCase>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
     app.UseSwagger();
     app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();