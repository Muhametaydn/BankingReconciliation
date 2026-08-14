using System.Security.Claims;
using System.Text.RegularExpressions;
using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Security;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Api.Endpoints;

public static partial class LocalAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapLocalAuthenticationEndpoints(
        this IEndpointRouteBuilder app,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return app;
        }

        app.MapPost("/api/auth/register", Register)
            .WithName("RegisterLocalUser")
            .Produces<LocalAuthenticationSessionResponse>(StatusCodes.Status201Created)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status409Conflict);

        app.MapPost("/api/auth/login", Login)
            .WithName("LoginLocalUser")
            .Produces<LocalAuthenticationSessionResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/auth/session", GetSession)
            .WithName("GetLocalAuthenticationSession")
            .RequireAuthorization()
            .Produces<LocalUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/auth/users", GetUsers)
            .WithName("GetLocalUsers")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<List<LocalUserResponse>>(StatusCodes.Status200OK);

        app.MapPut("/api/auth/users/{id:guid}/role", UpdateRole)
            .WithName("UpdateLocalUserRole")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<LocalUserResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult Register(
        RegisterLocalUserRequest request,
        ILocalUserAccountStore userStore,
        LocalAuthenticationTokenService tokenService,
        IReconciliationAuditRepository auditRepository)
    {
        var validationError = ValidateCredentials(request.Username, request.Password);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        var result = userStore.Register(request.Username, request.Password);
        if (result.UsernameAlreadyExists || result.User is null)
        {
            return Results.Conflict(new ReconciliationErrorResponse
            {
                Error = "UsernameAlreadyExists",
                Message = "Bu kullanıcı adı daha önce alınmış."
            });
        }

        auditRepository.Add(
            ReconciliationAuditAction.UserRegistered,
            result.User.Username,
            ReconciliationAuditResourceType.UserAccount,
            result.User.Id.ToString(),
            null,
            new { result.User.Username, Role = result.User.Role.ToString() });

        var response = CreateSessionResponse(result.User, tokenService, result.IsFirstAdministrator);
        return Results.Created($"/api/auth/users/{result.User.Id}", response);
    }

    private static IResult Login(
        LoginLocalUserRequest request,
        ILocalUserAccountStore userStore,
        LocalAuthenticationTokenService tokenService)
    {
        var user = userStore.ValidateCredentials(request.Username, request.Password);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(CreateSessionResponse(user, tokenService, false));
    }

    private static IResult GetSession(
        ClaimsPrincipal principal,
        ILocalUserAccountStore userStore)
    {
        var actor = ReconciliationUserIdentity.GetActor(principal);
        var user = actor is null ? null : userStore.GetByUsername(actor);
        return user is null ? Results.Unauthorized() : Results.Ok(ToResponse(user));
    }

    private static IResult GetUsers(ILocalUserAccountStore userStore) =>
        Results.Ok(userStore.GetAll().Select(ToResponse).ToList());

    private static IResult UpdateRole(
        Guid id,
        UpdateLocalUserRoleRequest request,
        ClaimsPrincipal principal,
        ILocalUserAccountStore userStore,
        IReconciliationAuditRepository auditRepository)
    {
        if (!Enum.TryParse<LocalUserRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidUserRole",
                Message = "Rol Operator, Approver veya Administrator olmalıdır."
            });
        }

        var before = userStore.GetAll().SingleOrDefault(user => user.Id == id);
        var actor = ReconciliationUserIdentity.GetActor(principal)!;
        if (before is not null && string.Equals(before.Username, actor, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new ReconciliationErrorResponse
            {
                Error = "CannotChangeOwnRole",
                Message = "Admin kendi rolünü değiştiremez."
            });
        }

        var previousRole = before?.Role;
        var result = userStore.UpdateRole(id, role);
        if (result.Outcome == LocalUserRoleUpdateOutcome.NotFound || result.User is null)
        {
            return Results.NotFound(new ReconciliationErrorResponse
            {
                Error = "UserNotFound",
                Message = "Kullanıcı bulunamadı."
            });
        }

        if (result.Outcome == LocalUserRoleUpdateOutcome.LastAdministrator)
        {
            return Results.Conflict(new ReconciliationErrorResponse
            {
                Error = "LastAdministratorCannotBeDemoted",
                Message = "Sistemde en az bir Admin hesabı kalmalıdır."
            });
        }

        auditRepository.Add(
            ReconciliationAuditAction.UserRoleUpdated,
            actor,
            ReconciliationAuditResourceType.UserAccount,
            id.ToString(),
            new { Username = result.User.Username, Role = previousRole?.ToString() },
            new { result.User.Username, Role = result.User.Role.ToString() });
        return Results.Ok(ToResponse(result.User));
    }

    private static LocalAuthenticationSessionResponse CreateSessionResponse(
        LocalUserAccount user,
        LocalAuthenticationTokenService tokenService,
        bool isFirstAdministrator)
    {
        var token = tokenService.CreateToken(user);
        return new LocalAuthenticationSessionResponse
        {
            AccessToken = token.Value,
            ExpiresAt = token.ExpiresAt,
            User = ToResponse(user),
            IsFirstAdministrator = isFirstAdministrator
        };
    }

    private static LocalUserResponse ToResponse(LocalUserAccount user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Role = user.Role.ToString(),
        CreatedAt = user.CreatedAt
    };

    private static ReconciliationErrorResponse? ValidateCredentials(string username, string password)
    {
        if (!UsernamePattern().IsMatch(username.Trim()))
        {
            return new ReconciliationErrorResponse
            {
                Error = "InvalidUsername",
                Message = "Kullanıcı adı 3-50 karakter olmalı; harf, rakam, nokta, tire ve alt çizgi kullanılabilir."
            };
        }

        if (password.Length is < 10 or > 128 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit))
        {
            return new ReconciliationErrorResponse
            {
                Error = "WeakPassword",
                Message = "Parola 10-128 karakter olmalı ve büyük harf, küçük harf ve rakam içermelidir."
            };
        }

        return null;
    }

    [GeneratedRegex("^[\\p{L}\\p{N}._-]{3,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
