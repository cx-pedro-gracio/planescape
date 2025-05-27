using KubeOps.Operator;
using Microsoft.Extensions.DependencyInjection;
using PlanescapeStackOperator.Controller;
using PlanescapeStackOperator.Services;
using PlanescapeStackOperator.Finalizer;
using PlanescapeStackOperator.Entities;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container
builder.Services
    .AddKubernetesOperator()
    .RegisterComponents()
    .AddFinalizer<PlanescapeStackFinalizer, PlanescapeStack>("planescape.io/stack-finalizer");
builder.Services.AddControllers();

// Register our services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IHelmService, HelmService>();
builder.Services.AddSingleton<IVaultService, VaultService>();
builder.Services.AddSingleton<IStackHealthService, StackHealthService>();

// Register our controllers
builder.Services.AddTransient<PlanescapeStackController>();
builder.Services.AddTransient<PlanescapeJobController>();

// Register our finalizers
builder.Services.AddTransient<IPlanescapeStackFinalizer, PlanescapeStackFinalizer>();

var app = builder.Build();

// Run the operator
await app.RunAsync();
