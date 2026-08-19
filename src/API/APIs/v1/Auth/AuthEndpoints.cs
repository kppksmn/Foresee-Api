using Core.DTOs;
using Core.Interfaces;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Library.Common;
using Microsoft.AspNetCore.Mvc;

namespace API.APIs.v1.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/login", async ([FromBody] LoginRequestDto req, AuthService authService, CancellationToken ct) =>
        {
            var (dto, errorCode, message) = await authService.AuthenticateAsync(req.Username, req.Password, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<AuthResponseDto>.Ok(dto, "Login successful"));
        });

        group.MapPost("/refresh", ([FromBody] RefreshTokenRequestDto req) =>
        {
            return Results.Ok(ApiResponse<string>.Ok("Token refreshed", "Not implemented mock"));
        });

        group.MapPost("/logout", () =>
        {
            return Results.Ok(ApiResponse<string>.Ok("Logged out successfully"));
        });
    }
}
