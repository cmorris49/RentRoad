using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Localization;
using RentRoad.Services.Listings;
using RentRoad.Services.Storage;
using RentRoad.Services.Rentals;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection using user secrets or environment variables.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews().AddRentRoadLocalization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<IRentalRequestService, RentalRequestService>();
builder.Services.AddScoped<IRentalManagementReadService, RentalManagementReadService>();
builder.Services.AddScoped<IListingImageService, ListingImageService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ListingEditorSessions>();
builder.Services.AddScoped<ListingEditorService>();
builder.Services.AddBlobImageStorage(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRequestLocalization();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
