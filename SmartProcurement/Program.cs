using Microsoft.AspNetCore.Mvc.Razor;
using SmartProcurement.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine("Frontend", "wwwroot")
});

builder.Services.AddControllersWithViews();
builder.Services.Configure<RazorViewEngineOptions>(options =>
{
    options.ViewLocationFormats.Clear();
    options.ViewLocationFormats.Add("/Frontend/Views/{1}/{0}.cshtml");
    options.ViewLocationFormats.Add("/Frontend/Views/Shared/{0}.cshtml");
});
builder.Services.AddScoped<SqlServerService>();
builder.Services.AddScoped<ConsultaIntentService>();
builder.Services.AddSingleton<DemoDataService>();
builder.Services.AddSingleton<ClasificacionComprasService>();
builder.Services.AddSingleton<CotizacionFlujoService>();
builder.Services.AddSingleton<SolicitudCotizacionService>();
builder.Services.AddScoped<TableroJefeService>();
builder.Services.AddScoped<LayoutService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
