using Core.DTOs;
using Core.Interfaces;
using Dapper;
using Infrastructure.Data;
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
            var (dto, errorCode, message) = await authService.AuthenticateAsync(req.Username, req.Password, req.Channel, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<AuthResponseDto>.Ok(dto, "Login successful"));
        })
        .Produces<ApiResponse<AuthResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("เข้าสู่ระบบ (Login)")
        .WithDescription("เข้าสู่ระบบโดยระบุ Username, Password และ Channel (1 = Web Admin Dashboard, 2 = Mobile App)");

        group.MapPost("/refresh", async ([FromBody] RefreshTokenRequestDto req, AuthService authService, CancellationToken ct) =>
        {
            var (dto, errorCode, message) = await authService.RefreshTokenAsync(req.RefreshToken, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<AuthResponseDto>.Ok(dto, "Token refreshed successfully"));
        })
        .Produces<ApiResponse<AuthResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ขอ Access Token ใหม่ด้วย Refresh Token")
        .WithDescription("ส่ง Refresh Token เพื่อขอ Access Token ใหม่");

        group.MapPost("/logout", () =>
        {
            return Results.Ok(ApiResponse<string>.Ok("Logged out successfully"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .WithSummary("ออกจากระบบ (Logout)");

        group.MapPost("/change-password", async (
            [FromBody] ChangePasswordVerifyRequestDto req, 
            ICurrentUser currentUser, 
            AuthService authService, 
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var (dto, errorCode, message) = await authService.VerifyPasswordForChangeAsync(currentUser.UserId, req.Password, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<ChangePasswordVerifyResponseDto>.Ok(dto, message));
        })
        .RequireAuthorization()
        .Produces<ApiResponse<ChangePasswordVerifyResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ยืนยันรหัสผ่านเพื่อขอ Token เปลี่ยนรหัสผ่าน (Change Password - Step 1: Verify)")
        .WithDescription("ส่งรหัสผ่านปัจจุบันเพื่อยืนยันตัวตน และรับ Verification Token สำหรับไปใช้ตั้งรหัสผ่านใหม่ในขั้นตอนถัดไป (Token มีอายุ 10 นาที)");

        group.MapPost("/newpassword", async (
            [FromBody] SetNewPasswordRequestDto req, 
            AuthService authService, 
            CancellationToken ct) =>
        {
            var (success, errorCode, message) = await authService.SetNewPasswordAsync(req.Token, req.Password, req.ConfirmPassword, ct);
            if (!success)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<string>.Ok(message));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ตั้งรหัสผ่านใหม่ด้วย Verification Token (Set New Password - Step 2: Confirm)")
        .WithDescription("ส่ง Verification Token พร้อมรหัสผ่านใหม่ (Password) และ ยืนยันรหัสผ่าน (ConfirmPassword) เพื่อเปลี่ยนรหัสผ่าน");

        group.MapPost("/forgotpassword", async (
            [FromBody] ForgotPasswordVerifyRequestDto req, 
            AuthService authService, 
            CancellationToken ct) =>
        {
            var (dto, errorCode, message) = await authService.VerifyForgotPasswordAsync(req.Username, req.IdCardNo, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<ChangePasswordVerifyResponseDto>.Ok(dto, message));
        })
        .Produces<ApiResponse<ChangePasswordVerifyResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ยืนยันตัวตนลืมรหัสผ่าน (Forgot Password - Step 1: Verify)")
        .WithDescription("ส่ง Username และ เลขบัตรประจำตัวประชาชน เพื่อยืนยันตัวตน และรับ Verification Token สำหรับไปตั้งรหัสผ่านใหม่ (Token มีอายุ 10 นาที)");

        group.MapPost("/forgot-password", async (
            [FromBody] ForgotPasswordVerifyRequestDto req, 
            AuthService authService, 
            CancellationToken ct) =>
        {
            var (dto, errorCode, message) = await authService.VerifyForgotPasswordAsync(req.Username, req.IdCardNo, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<ChangePasswordVerifyResponseDto>.Ok(dto, message));
        })
        .Produces<ApiResponse<ChangePasswordVerifyResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ยืนยันตัวตนลืมรหัสผ่าน (Alias: /forgot-password)");

        group.MapPost("/new-password", async (
            [FromBody] SetNewPasswordRequestDto req, 
            AuthService authService, 
            CancellationToken ct) =>
        {
            var (success, errorCode, message) = await authService.SetNewPasswordAsync(req.Token, req.Password, req.ConfirmPassword, ct);
            if (!success)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<string>.Ok(message));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ตั้งรหัสผ่านใหม่ด้วย Verification Token (Alias: /new-password)");

        group.MapGet("/me/menus", async (
            ICurrentUser currentUser,
            MenuManagementRepository menuRepo,
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var menus = await menuRepo.GetNavigableMenusForUserAsync(currentUser.UserId, currentUser.Role, ct);
            return Results.Ok(ApiResponse<List<UserNavMenuDto>>.Ok(menus, "ดึงโครงสร้างเมนูสำหรับผู้ใช้สำเร็จ"));
        })
        .RequireAuthorization()
        .Produces<ApiResponse<List<UserNavMenuDto>>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงโครงสร้างเมนูที่ผู้ใช้ได้รับสิทธิ์ (Get Navigable Menus for Current User)");

        group.MapGet("/me/jobs", async (
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] string? date,
            [FromQuery] string? startDate,
            [FromQuery] string? endDate,
            ICurrentUser currentUser,
            DbConnectionFactory db,
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();

            using var conn = db.CreateConnection();
            var sql = @"
                SELECT j.id, j.job_number AS ""jobNumber"", j.title, j.description, j.driver_id AS ""driverId"", j.vehicle_id AS ""vehicleId"", j.status,
                       j.pickup_location AS ""pickupLocation"", j.pickup_lat AS ""pickupLat"", j.pickup_lng AS ""pickupLng"",
                       j.contact_name AS ""contactName"", j.contact_phone AS ""contactPhone"", j.companions,
                       j.companion_id AS ""companionId"",
                       j.scheduled_start_at AS ""scheduledStartAt"",
                       CASE 
                           WHEN j.companion_id IS NULL THEN j.companions
                           ELSE COALESCE(
                               NULLIF(TRIM(COALESCE(cp_p.first_name, '') || ' ' || COALESCE(cp_p.last_name, '')), ''),
                               cp_u.username,
                               j.companions
                           )
                       END AS ""companionName"",
                       j.cancellation_reason AS ""cancellationReason"",
                       j.cancelled_at AS ""cancelledAt"",
                       j.cancelled_by AS ""cancelledBy"",
                       TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') AS ""scheduledDate"",
                       TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'HH24:MI') AS ""scheduledTime"",
                       CASE 
                           WHEN j.driver_id IS NULL THEN NULL
                           ELSE COALESCE(
                               NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''),
                               u.username,
                               'พนักงาน #' || CAST(j.driver_id AS TEXT)
                           )
                       END AS ""driverName"",
                       v.plate_number AS ""vehiclePlate"",
                       vt.name AS ""vehicleType""
                FROM jobs j
                LEFT JOIN user_profiles p ON p.user_id = j.driver_id
                LEFT JOIN users u ON u.id = j.driver_id
                LEFT JOIN user_profiles cp_p ON cp_p.user_id = j.companion_id AND cp_p.deleted_at IS NULL
                LEFT JOIN users cp_u ON cp_u.id = j.companion_id AND cp_u.deleted_at IS NULL
                LEFT JOIN vehicles v ON v.id = j.vehicle_id AND v.deleted_at IS NULL
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                WHERE j.deleted_at IS NULL
                  AND (j.driver_id = @UserId OR j.companion_id = @UserId)
                  AND (@status IS NULL OR @status = '' OR j.status = @status)
                  AND (@date IS NULL OR @date = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') = @date)
                  AND (@startDate IS NULL OR @startDate = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') >= @startDate)
                  AND (@endDate IS NULL OR @endDate = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') <= @endDate)
                  AND (
                    @search IS NULL OR @search = '' OR 
                    j.job_number ILIKE '%' || @search || '%' OR 
                    j.title ILIKE '%' || @search || '%' OR
                    j.pickup_location ILIKE '%' || @search || '%' OR
                    COALESCE(j.contact_name, '') ILIKE '%' || @search || '%' OR
                    COALESCE(v.plate_number, '') ILIKE '%' || @search || '%'
                  )
                ORDER BY j.id DESC;";

            var list = await conn.QueryAsync<AdminJobListItemDto>(new CommandDefinition(sql, new { UserId = currentUser.UserId, search, status, date, startDate, endDate }, cancellationToken: ct));
            return Results.Ok(ApiResponse<IEnumerable<AdminJobListItemDto>>.Ok(list, "ดึงรายการงานของฉันสำเร็จ"));
        })
        .RequireAuthorization()
        .Produces<ApiResponse<IEnumerable<AdminJobListItemDto>>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการงานของฉัน (Get My Jobs)");
    }
}
