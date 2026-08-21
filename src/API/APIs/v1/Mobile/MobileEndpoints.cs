using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Infrastructure.Repositories;
using Library.Common;
using Microsoft.AspNetCore.Mvc;

namespace API.APIs.v1.Mobile;

public static class MobileEndpoints
{
    public static void MapMobileEndpoints(this IEndpointRouteBuilder routes)
    {
        var mobileGroup = routes.MapGroup("/api/v1/mobile").RequireAuthorization("MobileAuthenticated").WithTags("Mobile Driver");

        mobileGroup.MapGet("/user", async (ICurrentUser currentUser, Infrastructure.Services.AuthService authService, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var profile = await authService.GetMobileUserProfileAsync(currentUser.UserId, ct);
            if (profile == null) return Results.NotFound(ApiResponse<string>.Fail("User profile not found"));
            return Results.Ok(ApiResponse<MobileUserResponseDto>.Ok(profile));
        })
        .Produces<ApiResponse<MobileUserResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงข้อมูลโปรไฟล์ผู้ใช้งาน / พนักงานขับรถ (Mobile User Profile)")
        .WithDescription("ส่ง Access Token เพื่อรับข้อมูลบัญชีผู้ใช้ ข้อมูลส่วนตัว ใบขับขี่ และรถประจำตัว");

        mobileGroup.MapPost("/device", async (
            [FromBody] RegisterDeviceRequestDto req, 
            ICurrentUser currentUser, 
            Infrastructure.Repositories.DbConnectionFactory db, 
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.DeviceId)) return Results.BadRequest(ApiResponse<string>.Fail("กรุณาระบุ DeviceId"));

            using var conn = db.CreateConnection();
            var checkSql = @"
                SELECT id, user_id AS UserId, device_id AS DeviceId, device_name AS DeviceName, 
                       device_model AS DeviceModel, app_version AS AppVersion, fcm_token AS FcmToken, 
                       ip_address AS IpAddress, is_active AS IsActive
                FROM user_devices 
                WHERE user_id = @UserId AND device_id = @DeviceId AND deleted_at IS NULL 
                ORDER BY created_at DESC 
                LIMIT 1;";

            var existing = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<UserDevice>(conn, new Dapper.CommandDefinition(checkSql, new { UserId = currentUser.UserId, req.DeviceId }, cancellationToken: ct));

            if (existing != null)
            {
                // Check if any property has changed
                bool isChanged = (req.DeviceName != null && req.DeviceName != existing.DeviceName)
                              || (req.DeviceModel != null && req.DeviceModel != existing.DeviceModel)
                              || (req.AppVersion != null && req.AppVersion != existing.AppVersion)
                              || (req.FcmToken != null && req.FcmToken != existing.FcmToken)
                              || (req.IpAddress != null && req.IpAddress != existing.IpAddress);

                if (isChanged)
                {
                    // Soft-delete the old device record
                    var deleteSql = @"
                        UPDATE user_devices
                        SET is_active = FALSE, deleted_at = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP
                        WHERE id = @existingId;";
                    await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(deleteSql, new { existingId = existing.Id }, cancellationToken: ct));

                    // Insert new device row with new values
                    var newDeviceName = req.DeviceName ?? existing.DeviceName;
                    var newDeviceModel = req.DeviceModel ?? existing.DeviceModel;
                    var newAppVersion = req.AppVersion ?? existing.AppVersion;
                    var newFcmToken = req.FcmToken ?? existing.FcmToken;
                    var newIpAddress = req.IpAddress ?? existing.IpAddress;

                    var insertSql = @"
                        INSERT INTO user_devices (user_id, device_id, device_name, device_model, app_version, fcm_token, ip_address, is_active, created_at)
                        VALUES (@UserId, @DeviceId, @newDeviceName, @newDeviceModel, @newAppVersion, @newFcmToken, @newIpAddress, TRUE, CURRENT_TIMESTAMP);";
                    await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(insertSql, new { UserId = currentUser.UserId, req.DeviceId, newDeviceName, newDeviceModel, newAppVersion, newFcmToken, newIpAddress }, cancellationToken: ct));
                }
            }
            else
            {
                var insertSql = @"
                    INSERT INTO user_devices (user_id, device_id, device_name, device_model, app_version, fcm_token, ip_address, is_active, created_at)
                    VALUES (@UserId, @DeviceId, @DeviceName, @DeviceModel, @AppVersion, @FcmToken, @IpAddress, TRUE, CURRENT_TIMESTAMP);";
                await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(insertSql, new { UserId = currentUser.UserId, req.DeviceId, req.DeviceName, req.DeviceModel, req.AppVersion, req.FcmToken, req.IpAddress }, cancellationToken: ct));
            }

            return Results.Ok(ApiResponse<string>.Ok("บันทึกข้อมูลอุปกรณ์สำเร็จ"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ลงทะเบียนหรืออัปเดตข้อมูลอุปกรณ์และ FCM Token (Register/Update Device)")
        .WithDescription("ส่ง Device ID, FCM Token, Model, App Version และ IP Address เพื่อใช้รับการแจ้งเตือน Push Notification");

        mobileGroup.MapPost("/change-password", async (
            [FromBody] ChangePasswordVerifyRequestDto req, 
            ICurrentUser currentUser, 
            Infrastructure.Services.AuthService authService, 
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
        .Produces<ApiResponse<ChangePasswordVerifyResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ยืนยันรหัสผ่านเพื่อขอ Token เปลี่ยนรหัสผ่าน (Change Password - Step 1: Verify)")
        .WithDescription("ส่งรหัสผ่านปัจจุบันเพื่อยืนยันตัวตน และรับ Verification Token สำหรับไปใช้ตั้งรหัสผ่านใหม่ (Token มีอายุ 10 นาที)");

        mobileGroup.MapPost("/newpassword", async (
            [FromBody] SetNewPasswordRequestDto req, 
            Infrastructure.Services.AuthService authService, 
            CancellationToken ct) =>
        {
            var (success, errorCode, message) = await authService.SetNewPasswordAsync(req.Token, req.Password, req.ConfirmPassword, ct);
            if (!success)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<string>.Ok(message));
        })
        .AllowAnonymous()
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ตั้งรหัสผ่านใหม่ด้วย Verification Token (Set New Password - Step 2: Confirm)")
        .WithDescription("ส่ง Verification Token พร้อมรหัสผ่านใหม่ (Password) และ ยืนยันรหัสผ่าน (ConfirmPassword) เพื่อเปลี่ยนรหัสผ่าน");

        mobileGroup.MapPost("/forgotpassword", async (
            [FromBody] ForgotPasswordVerifyRequestDto req, 
            Infrastructure.Services.AuthService authService, 
            CancellationToken ct) =>
        {
            var (dto, errorCode, message) = await authService.VerifyForgotPasswordAsync(req.Username, req.IdCardNo, ct);
            if (dto == null)
            {
                return Results.BadRequest(ApiResponse<string>.Fail(message));
            }
            return Results.Ok(ApiResponse<ChangePasswordVerifyResponseDto>.Ok(dto, message));
        })
        .AllowAnonymous()
        .Produces<ApiResponse<ChangePasswordVerifyResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ยืนยันตัวตนลืมรหัสผ่าน (Forgot Password - Step 1: Verify)")
        .WithDescription("ส่ง Username และ เลขบัตรประจำตัวประชาชน เพื่อยืนยันตัวตน และรับ Verification Token สำหรับไปตั้งรหัสผ่านใหม่ (ไม่ต้องใช้ Access Token)");

        var jobsGroup = mobileGroup.MapGroup("/jobs").RequireAuthorization("MobileDriver");

        const int fixedPageSize = 25;

        jobsGroup.MapGet("/", async (ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            bool isAdmin = string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            var jobs = await jobRepo.GetUnfinishedJobsForDriverAsync(currentUser.UserId, currentDriver.DriverId, isAdmin, ct);
            return Results.Ok(ApiResponse<IEnumerable<JobDto>>.Ok(jobs));
        })
        .Produces<ApiResponse<IEnumerable<JobDto>>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการงานที่ยังไม่เสร็จทั้งหมดของคนขับ (Active Jobs)")
        .WithDescription("ดึงรายการงานที่มีสถานะ Pending, Assigned, Started, Arrived ของคนขับที่ยืนยันตัวตนด้วย Access Token");

        async Task<IResult> HandleGetMobileJobsAsync(
            JobListRequestDto? req,
            ICurrentUser currentUser, 
            ICurrentDriver currentDriver, 
            JobRepository jobRepo, 
            CancellationToken ct)
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            bool isAdmin = string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);

            var pageSize = req?.PageSize ?? fixedPageSize;
            var reqOffset = req?.Offset ?? pageSize;
            var sqlOffset = reqOffset <= pageSize ? 0 : reqOffset - pageSize;

            var (items, totalCount) = await jobRepo.GetUnfinishedJobsForDriverPaginatedAsync(
                currentUser.UserId, 
                currentDriver.DriverId, 
                isAdmin,
                req?.Status,
                req?.Search,
                sqlOffset, 
                pageSize, 
                ct);

            var result = new JobListResponseDto
            {
                Items = items,
                TotalCount = totalCount
            };

            return Results.Ok(ApiResponse<JobListResponseDto>.Ok(result));
        }

        jobsGroup.MapPost("/", async ([FromBody] JobListRequestDto? req, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
            await HandleGetMobileJobsAsync(req, currentUser, currentDriver, jobRepo, ct))
        .Produces<ApiResponse<JobListResponseDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการงานทั้งหมดของคนขับ/แอดมินแบบ POST Body ส่ง offset (Mobile Active Jobs with Offset)")
        .WithDescription("ส่ง JSON Body เช่น { \"offset\": 25, \"pageSize\": 25, \"status\": \"Assigned\", \"search\": \"...\" } เพื่อดึงรายการงานแบบแบ่งหน้า");

        jobsGroup.MapPost("/list", async ([FromBody] JobListRequestDto? req, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
            await HandleGetMobileJobsAsync(req, currentUser, currentDriver, jobRepo, ct))
        .Produces<ApiResponse<JobListResponseDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการงานทั้งหมดแบบ POST Body ส่ง offset (/jobs/list)");

        jobsGroup.MapPost("/all", async ([FromBody] JobListRequestDto? req, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
            await HandleGetMobileJobsAsync(req, currentUser, currentDriver, jobRepo, ct))
        .Produces<ApiResponse<JobListResponseDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการงานทั้งหมดแบบ POST Body ส่ง offset (/jobs/all)");

        jobsGroup.MapPost("/history", async (
            [FromBody] JobHistoryRequestDto? req,
            ICurrentUser currentUser, 
            ICurrentDriver currentDriver, 
            JobRepository jobRepo, 
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            bool isAdmin = currentUser.Role == "Admin";

            var reqOffset = req?.Offset ?? fixedPageSize;
            var sqlOffset = reqOffset <= fixedPageSize ? 0 : reqOffset - fixedPageSize;

            var (items, totalCount) = await jobRepo.GetJobHistoryForDriverAsync(
                currentUser.UserId, 
                currentDriver.DriverId, 
                req?.Status,
                sqlOffset, 
                fixedPageSize, 
                isAdmin,
                ct);

            var result = new JobHistoryResponseDto
            {
                Items = items,
                TotalCount = totalCount
            };

            return Results.Ok(ApiResponse<JobHistoryResponseDto>.Ok(result));
        })
        .Produces<ApiResponse<JobHistoryResponseDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงประวัติงานทั้งหมดของคนขับแบบ POST Body (Job History)")
        .WithDescription("ส่ง JSON Body เช่น { \"offset\": 25, \"status\": \"Completed\" } (status รองรับ 'Completed', 'Cancelled', หรือไม่ส่ง/'All' เพื่อดึงทั้งคู่)");

        jobsGroup.MapGet("/{jobId:long}", async (long jobId, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            bool isAdmin = currentUser.Role == "Admin";
            var job = await jobRepo.GetJobDetailForDriverAsync(jobId, currentUser.UserId, currentDriver.DriverId, isAdmin, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));
            return Results.Ok(ApiResponse<JobDto>.Ok(job));
        })
        .Produces<ApiResponse<JobDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายละเอียดงานตาม ID (Get Job by ID)")
        .WithDescription("ดึงข้อมูลรายละเอียดงาน เช่น หัวข้องาน จุดรับ พิกัด เบอร์ติดต่อ ผู้ติดตาม วันเวลาเริ่มงาน");

        jobsGroup.MapPost("/{jobId:long}/start", async (long jobId, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentUser.UserId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Assigned", "Started", currentUser.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Assigned' status."));

            return Results.Ok(ApiResponse<string>.Ok("Job started successfully"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("เริ่มปฏิบัติงาน (Start Job)");

        jobsGroup.MapPost("/{jobId:long}/arrive", async (long jobId, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentUser.UserId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Started", "Arrived", currentUser.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Started' status."));

            return Results.Ok(ApiResponse<string>.Ok("Arrived at destination"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ถึงจุดหมาย (Arrive Destination)");

        jobsGroup.MapPost("/{jobId:long}/complete", async (long jobId, ICurrentUser currentUser, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentUser.UserId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Arrived", "Completed", currentUser.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Arrived' status."));

            return Results.Ok(ApiResponse<string>.Ok("Job completed successfully"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ปิดงานเสร็จสิ้น (Complete Job)");

        var notiGroup = mobileGroup.MapGroup("/notifications");

        notiGroup.MapGet("/", async (
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            ICurrentUser currentUser,
            DbConnectionFactory db,
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();

            int take = Math.Clamp(limit ?? 20, 1, 50);
            int skip = Math.Max(offset ?? 0, 0);

            using var conn = db.CreateConnection();

            const string countSql = @"
                SELECT 
                    COUNT(1) AS ""TotalCount"",
                    COUNT(CASE WHEN is_read = FALSE THEN 1 END) AS ""UnreadCount""
                FROM notification_outbox
                WHERE user_id = @UserId;";

            var counts = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<dynamic>(conn,
                new Dapper.CommandDefinition(countSql, new { UserId = currentUser.UserId }, cancellationToken: ct));

            int totalCount = counts != null ? (int)counts.TotalCount : 0;
            int unreadCount = counts != null ? (int)counts.UnreadCount : 0;

            const string listSql = @"
                SELECT id, title, body, payload_json AS ""PayloadJson"", is_read AS ""IsRead"", read_at AS ""ReadAt"", created_at AS ""CreatedAt""
                FROM notification_outbox
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                LIMIT @take OFFSET @skip;";

            var items = await Dapper.SqlMapper.QueryAsync<NotificationItemDto>(conn,
                new Dapper.CommandDefinition(listSql, new { UserId = currentUser.UserId, take, skip }, cancellationToken: ct));

            var result = new NotificationListResponseDto
            {
                Items = items,
                TotalCount = totalCount,
                UnreadCount = unreadCount
            };

            return Results.Ok(ApiResponse<NotificationListResponseDto>.Ok(result));
        })
        .Produces<ApiResponse<NotificationListResponseDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .WithSummary("ดึงรายการแจ้งเตือนของคนขับ (Driver Notifications)")
        .WithDescription("ดึงรายการแจ้งเตือนทั้งหมดของพนักงาน พร้อมจำนวนแจ้งเตือนที่ยังไม่ได้อ่าน (Unread Count)");

        notiGroup.MapPost("/{id:long}/read", async (
            long id,
            ICurrentUser currentUser,
            DbConnectionFactory db,
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();

            using var conn = db.CreateConnection();
            const string sql = @"
                UPDATE notification_outbox
                SET is_read = TRUE, read_at = CURRENT_TIMESTAMP
                WHERE id = @id AND user_id = @UserId;";

            var affected = await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(sql, new { id, UserId = currentUser.UserId }, cancellationToken: ct));
            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบการแจ้งเตือน"));

            return Results.Ok(ApiResponse<string>.Ok("ทำเครื่องหมายว่าอ่านแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ทำเครื่องหมายว่าอ่านแล้ว (Mark Notification as Read)");

        notiGroup.MapPost("/read-all", async (
            ICurrentUser currentUser,
            DbConnectionFactory db,
            CancellationToken ct) =>
        {
            if (currentUser.UserId <= 0) return Results.Unauthorized();

            using var conn = db.CreateConnection();
            const string sql = @"
                UPDATE notification_outbox
                SET is_read = TRUE, read_at = CURRENT_TIMESTAMP
                WHERE user_id = @UserId AND is_read = FALSE;";

            await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(sql, new { UserId = currentUser.UserId }, cancellationToken: ct));

            return Results.Ok(ApiResponse<string>.Ok("ทำเครื่องหมายว่าอ่านทั้งหมดแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .WithSummary("ทำเครื่องหมายว่าอ่านทั้งหมดแล้ว (Mark All Notifications as Read)");
    }
}
