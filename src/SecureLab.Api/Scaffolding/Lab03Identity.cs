using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();
            var userName = input.UserName?.Trim() ?? "";
            var email = input.Email?.Trim() ?? "";
            var displayName = input.DisplayName?.Trim() ?? "";
            var password = input.Password ?? "";

            if (userName.Length == 0)
                errors["userName"] = ["Ім'я користувача обов'язкове."];
            else if (userName.Length > 256)
                errors["userName"] = ["Ім'я користувача не довше за 256 символів."];

            if (email.Length == 0)
                errors["email"] = ["Email обов'язковий."];
            else if (email.Length > 256)
                errors["email"] = ["Email не довший за 256 символів."];

            if (displayName.Length == 0)
                errors["displayName"] = ["Відображуване ім'я обов'язкове."];
            else if (displayName.Length > 120)
                errors["displayName"] = ["Відображуване ім'я не довше за 120 символів."];

            if (password.Length == 0)
                errors["password"] = ["Пароль обов'язковий."];
            else if (password.Length > 128)
                errors["password"] = ["Пароль не довший за 128 символів."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                DisplayName = displayName
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                var codes = result.Errors.Select(error => error.Code).ToHashSet();
                if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Реєстрація неможлива",
                        detail: "Обліковий запис з такими даними вже існує.");

                var failures = new Dictionary<string, string[]>();
                if (codes.Contains("InvalidUserName"))
                    failures["userName"] = ["Дозволені латинські літери, цифри та символи - . _ @ +."];
                if (codes.Contains("InvalidEmail"))
                    failures["email"] = ["Email має некоректний формат."];
                if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
                    failures["password"] = ["Пароль: щонайменше 10 символів, велика й мала літери, цифра та спеціальний символ."];
                if (failures.Count == 0)
                    failures["registration"] = ["Реєстрацію не виконано."];
                return Results.ValidationProblem(failures);
            }

            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            return Results.Created("/api/me", new RegistrationResponse(user.Id, userName, displayName));
        });

        app.MapPost("/api/auth/login", async (
            LoginInput input,
            SignInManager<ApplicationUser> signIn,
            HttpContext httpContext,
            ILogger<Program> logger) =>
        {
            var succeeded = false;
            if (!string.IsNullOrWhiteSpace(input.UserName) && !string.IsNullOrEmpty(input.Password))
            {
                var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
                succeeded = result.Succeeded;
            }
            logger.LogInformation(
                "Auth event {EventType}: result {Result}, trace {TraceId}",
                "login", succeeded ? "success" : "failure", httpContext.TraceIdentifier);
            return succeeded ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (
            SignInManager<ApplicationUser> signIn,
            HttpContext httpContext,
            ILogger<Program> logger) =>
        {
            await signIn.SignOutAsync();
            logger.LogInformation(
                "Auth event {EventType}: result {Result}, trace {TraceId}",
                "logout", "success", httpContext.TraceIdentifier);
            return Results.NoContent();
        });

        app.MapGet("/api/me", GetCurrentUser).RequireAuthorization();

        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        return Results.Ok(new CurrentUserResponse(verifiedUserId, userName, role));
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid UserId, string UserName, string Role);