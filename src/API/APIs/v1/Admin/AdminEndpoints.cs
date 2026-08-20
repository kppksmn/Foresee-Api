using Core.DTOs;
using Core.Entities;
using Core.Interfaces;
using Dapper;
using Infrastructure.Repositories;
using Library.Common;
using Microsoft.AspNetCore.Mvc;

namespace API.APIs.v1.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var adminGroup = routes.MapGroup("/api/v1/admin").RequireAuthorization("AdminOnly").WithTags("Web Admin");
        var mobileAdminGroup = routes.MapGroup("/api/v1/mobile/admin").RequireAuthorization("MobileAdmin").WithTags("Mobile Admin");

        RegisterAdminRoutes(adminGroup);
        RegisterAdminRoutes(mobileAdminGroup);
    }

    private static void RegisterAdminRoutes(RouteGroupBuilder group)
    {
        group.MapPost("/jobs", async ([FromBody] CreateJobDto req, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                INSERT INTO jobs (job_number, title, description, driver_id, vehicle_id, status, pickup_location, pickup_lat, pickup_lng, contact_name, contact_phone, companions, scheduled_start_at, created_by, created_at)
                VALUES (@JobNumber, @Title, @Description, @DriverId, @VehicleId, @Status, @PickupLocation, @PickupLat, @PickupLng, @ContactName, @ContactPhone, @Companions, @ScheduledStartAt, @CreatedBy, @CreatedAt)
                RETURNING id;";

            var status = req.DriverId.HasValue ? "Assigned" : "Pending";
            var jobNumber = "JOB-" + DateTime.UtcNow.Ticks.ToString()[^8..];

            var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
            {
                JobNumber = jobNumber,
                req.Title,
                req.Description,
                req.DriverId,
                req.VehicleId,
                Status = status,
                req.PickupLocation,
                req.PickupLat,
                req.PickupLng,
                req.ContactName,
                req.ContactPhone,
                req.Companions,
                req.ScheduledStartAt,
                CreatedBy = user.UserId,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken: ct));

            await auditRepo.LogAsync(user.UserId, "CREATE", "jobs", id.ToString(), $"สร้างงานใหม่ เลขที่งาน {jobNumber} หัวข้อ: {req.Title}", ct: ct);

            // Send Push Notification if driver is assigned
            if (req.DriverId.HasValue)
            {
                _ = pushNotificationService.SendJobAssignedNotificationAsync(req.DriverId.Value, id, jobNumber, req.Title, req.PickupLocation, ct);
            }

            List<string>? warnings = null;
            if (req.DriverId.HasValue)
            {
                var profileRepo = new UserProfileRepository(db);
                var driver = await profileRepo.GetByUserIdAsync(req.DriverId.Value, ct);
                if (driver != null && driver.LicenseExpirationDate.HasValue && driver.LicenseExpirationDate.Value < DateTime.UtcNow.Date)
                {
                    warnings = new List<string> { $"Warning: Driver '{driver.FirstName} {driver.LastName}' has an expired license!" };
                }
            }

            return Results.Ok(ApiResponse<CreatedJobResponseDto>.Ok(new CreatedJobResponseDto(id, jobNumber, status), "Job created successfully", warnings));
        })
        .Produces<ApiResponse<CreatedJobResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("สร้างงานขนส่งใหม่ (Create Job)");

        group.MapPost("/jobs/{jobId:long}/assign", async (long jobId, [FromBody] AssignJobDto req, ICurrentUser user, DbConnectionFactory db, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var jobInfo = await conn.QueryFirstOrDefaultAsync("SELECT driver_id AS \"driverId\", job_number AS \"jobNumber\", title, pickup_location AS \"pickupLocation\" FROM jobs WHERE id = @jobId AND deleted_at IS NULL;", new { jobId });
            if (jobInfo == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            var previousDriverId = jobInfo.driverId != null ? (long?)Convert.ToInt64(jobInfo.driverId) : null;

            var sql = @"
                UPDATE jobs 
                SET driver_id = @DriverId, vehicle_id = COALESCE(@VehicleId, vehicle_id), status = 'Assigned', updated_at = @Now, updated_by = @UserId
                WHERE id = @jobId AND deleted_at IS NULL;";

            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, req.DriverId, req.VehicleId, Now = DateTime.UtcNow, user.UserId }, cancellationToken: ct));
            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            // Send Push Notifications
            if (previousDriverId.HasValue && previousDriverId.Value != req.DriverId)
            {
                _ = pushNotificationService.SendJobCancelledNotificationAsync(previousDriverId.Value, jobId, (string)jobInfo.jobNumber, (string)jobInfo.title, "งานถูกเปลี่ยนผู้ขับขี่หรือมอบหมายให้พนักงานท่านอื่น", ct);
            }
            _ = pushNotificationService.SendJobAssignedNotificationAsync(req.DriverId, jobId, (string)jobInfo.jobNumber, (string)jobInfo.title, (string)jobInfo.pickupLocation, ct);

            List<string>? warnings = null;
            var profileRepo = new UserProfileRepository(db);
            var driver = await profileRepo.GetByUserIdAsync(req.DriverId, ct);
            if (driver != null && driver.LicenseExpirationDate.HasValue && driver.LicenseExpirationDate.Value < DateTime.UtcNow.Date)
            {
                warnings = new List<string> { $"Warning: Driver '{driver.FirstName} {driver.LastName}' has an expired license!" };
            }

            return Results.Ok(ApiResponse<string>.Ok("Job assigned successfully", warnings: warnings));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("มอบหมายงานให้คนขับ (Assign Job)");

        group.MapPost("/jobs/{jobId:long}/cancel", async (long jobId, [FromBody] CancelJobDto req, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var jobInfo = await conn.QueryFirstOrDefaultAsync(@"
                SELECT driver_id AS ""driverId"", job_number AS ""jobNumber"", title, status 
                FROM jobs 
                WHERE id = @jobId AND deleted_at IS NULL;", new { jobId });
            if (jobInfo == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            var sql = @"
                UPDATE jobs 
                SET status = 'Cancelled', cancellation_reason = @Reason, cancelled_at = @Now, cancelled_by = @UserId, updated_at = @Now, updated_by = @UserId
                WHERE id = @jobId AND status IN ('Pending', 'Assigned', 'Started', 'Arrived') AND deleted_at IS NULL;";

            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, req.Reason, Now = DateTime.UtcNow, user.UserId }, cancellationToken: ct));
            if (affected == 0) return Results.BadRequest(ApiResponse<string>.Fail("Unable to cancel job. It may already be completed or cancelled."));

            // Record status history
            var historySql = @"
                INSERT INTO job_status_histories (job_id, from_status, to_status, changed_by, notes, created_at)
                VALUES (@jobId, @FromStatus, 'Cancelled', @UserId, @Reason, @Now);";
            await conn.ExecuteAsync(new CommandDefinition(historySql, new { jobId, FromStatus = (string)jobInfo.status, UserId = user.UserId, Reason = req.Reason, Now = DateTime.UtcNow }, cancellationToken: ct));

            // Send push notification if driver is assigned
            if (jobInfo.driverId != null)
            {
                long driverId = Convert.ToInt64(jobInfo.driverId);
                string jobNum = (string)jobInfo.jobNumber ?? jobId.ToString();
                string title = (string)jobInfo.title ?? "";
                _ = pushNotificationService.SendJobCancelledNotificationAsync(driverId, jobId, jobNum, title, req.Reason, ct);
            }

            var targetJobNumber = (string)jobInfo.jobNumber ?? jobId.ToString();
            await auditRepo.LogAsync(user.UserId, "CANCEL", "jobs", jobId.ToString(), $"ยกเลิกงาน เลขที่งาน {targetJobNumber} เหตุผล: {req.Reason}", ct: ct);

            return Results.Ok(ApiResponse<string>.Ok("Job cancelled successfully"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ยกเลิกงาน (Cancel Job)");

        group.MapGet("/users", async ([FromQuery] string? search, DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                SELECT u.id, u.username, u.role, u.is_active AS ""isActive"",
                       COALESCE(NULLIF(TRIM(p.first_name || ' ' || p.last_name), ''), u.username) AS ""name"",
                       p.employee_code AS ""employeeId"", p.phone, p.email,
                       p.id_card_no AS ""idCardNo"",
                       TO_CHAR(p.birth_date, 'YYYY-MM-DD') AS ""birthDate"",
                       p.license_no AS ""licenseNo"",
                       TO_CHAR(p.license_expiration_date, 'YYYY-MM-DD') AS ""licenseExpiration"",
                       CASE WHEN p.license_expiration_date < CURRENT_DATE THEN 'Expired' ELSE 'Valid' END AS ""licenseStatus"",
                       p.vehicle_id AS ""vehicleId"",
                       v.plate_number AS ""vehiclePlate"",
                       vt.name AS ""vehicleType"",
                       (
                           SELECT COUNT(1) 
                           FROM jobs j 
                           WHERE j.driver_id = u.id 
                             AND j.status NOT IN ('Completed', 'Cancelled') 
                             AND j.deleted_at IS NULL
                       ) AS ""activeJobsCount""
                FROM users u
                LEFT JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
                LEFT JOIN vehicles v ON v.id = p.vehicle_id AND v.deleted_at IS NULL
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                WHERE u.deleted_at IS NULL
                  AND (@search IS NULL OR u.username ILIKE '%' || @search || '%' OR p.first_name ILIKE '%' || @search || '%' OR p.last_name ILIKE '%' || @search || '%' OR p.employee_code ILIKE '%' || @search || '%' OR p.phone ILIKE '%' || @search || '%')
                ORDER BY u.id DESC;";

            var list = await conn.QueryAsync<AdminUserListItemDto>(new CommandDefinition(sql, new { search }, cancellationToken: ct));
            return Results.Ok(ApiResponse<IEnumerable<AdminUserListItemDto>>.Ok(list));
        })
        .Produces<ApiResponse<IEnumerable<AdminUserListItemDto>>>(StatusCodes.Status200OK)
        .WithSummary("ดึงรายชื่อผู้ใช้งานทั้งหมด (List Users)");

        group.MapGet("/jobs", async ([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? mode, DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                SELECT j.id, j.job_number AS ""jobNumber"", j.title, j.description, j.driver_id AS ""driverId"", j.status,
                       j.pickup_location AS ""pickupLocation"", j.pickup_lat AS ""pickupLat"", j.pickup_lng AS ""pickupLng"",
                       j.contact_name AS ""contactName"", j.contact_phone AS ""contactPhone"", j.companions,
                       j.cancellation_reason AS ""cancellationReason"",
                       j.cancelled_at AS ""cancelledAt"",
                       j.cancelled_by AS ""cancelledBy"",
                       COALESCE(
                           NULLIF(TRIM(COALESCE(cb_p.first_name, '') || ' ' || COALESCE(cb_p.last_name, '')), ''),
                           cb_u.username,
                           (
                               SELECT COALESCE(NULLIF(TRIM(COALESCE(h_p.first_name, '') || ' ' || COALESCE(h_p.last_name, '')), ''), h_u.username)
                               FROM job_status_histories h
                               JOIN users h_u ON h_u.id = h.changed_by
                               LEFT JOIN user_profiles h_p ON h_p.user_id = h_u.id AND h_p.deleted_at IS NULL
                               WHERE h.job_id = j.id AND h.to_status = 'Cancelled'
                               ORDER BY h.created_at DESC
                               LIMIT 1
                           ),
                           CASE 
                               WHEN j.status = 'Cancelled' AND j.updated_by IS NOT NULL THEN (
                                   SELECT COALESCE(NULLIF(TRIM(COALESCE(up_p.first_name, '') || ' ' || COALESCE(up_p.last_name, '')), ''), up_u.username)
                                   FROM users up_u
                                   LEFT JOIN user_profiles up_p ON up_p.user_id = up_u.id AND up_p.deleted_at IS NULL
                                   WHERE up_u.id = j.updated_by
                               )
                               ELSE NULL
                           END
                       ) AS ""cancelledByName"",
                       TO_CHAR(j.scheduled_start_at, 'YYYY-MM-DD') AS ""scheduledDate"",
                       TO_CHAR(j.scheduled_start_at, 'HH24:MI') AS ""scheduledTime"",
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
                LEFT JOIN vehicles v ON v.id = j.vehicle_id AND v.deleted_at IS NULL
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                LEFT JOIN users cb_u ON cb_u.id = j.cancelled_by AND cb_u.deleted_at IS NULL
                LEFT JOIN user_profiles cb_p ON cb_p.user_id = cb_u.id AND cb_p.deleted_at IS NULL
                WHERE j.deleted_at IS NULL
                  AND (@status IS NULL OR @status = '' OR j.status = @status)
                  AND (
                    CASE 
                      WHEN @mode = 'history' THEN j.status IN ('Completed', 'Cancelled')
                      ELSE j.status NOT IN ('Completed', 'Cancelled')
                    END
                  )
                  AND (
                    @search IS NULL OR @search = '' OR 
                    j.job_number ILIKE '%' || @search || '%' OR 
                    j.title ILIKE '%' || @search || '%' OR
                    j.pickup_location ILIKE '%' || @search || '%' OR
                    COALESCE(j.contact_name, '') ILIKE '%' || @search || '%' OR
                    COALESCE(p.first_name || ' ' || p.last_name, u.username, '') ILIKE '%' || @search || '%' OR
                    COALESCE(v.plate_number, '') ILIKE '%' || @search || '%'
                  )
                ORDER BY j.id DESC;";

            var list = await conn.QueryAsync<AdminJobListItemDto>(new CommandDefinition(sql, new { search, status, mode }, cancellationToken: ct));
            return Results.Ok(ApiResponse<IEnumerable<AdminJobListItemDto>>.Ok(list));
        })
        .Produces<ApiResponse<IEnumerable<AdminJobListItemDto>>>(StatusCodes.Status200OK)
        .WithSummary("ดึงรายการงานขนส่งทั้งหมด (List Jobs)");

        group.MapGet("/jobs/{id:long}", async (long id, DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                SELECT j.id, j.job_number AS ""jobNumber"", j.title, j.description, j.driver_id AS ""driverId"", j.vehicle_id AS ""vehicleId"", j.status,
                       j.pickup_location AS ""pickupLocation"", j.pickup_lat AS ""pickupLat"", j.pickup_lng AS ""pickupLng"",
                       j.contact_name AS ""contactName"", j.contact_phone AS ""contactPhone"", j.companions,
                       j.cancellation_reason AS ""cancellationReason"",
                       j.cancelled_at AS ""cancelledAt"",
                       j.cancelled_by AS ""cancelledBy"",
                       COALESCE(
                           NULLIF(TRIM(COALESCE(cb_p.first_name, '') || ' ' || COALESCE(cb_p.last_name, '')), ''),
                           cb_u.username,
                           (
                               SELECT COALESCE(NULLIF(TRIM(COALESCE(h_p.first_name, '') || ' ' || COALESCE(h_p.last_name, '')), ''), h_u.username)
                               FROM job_status_histories h
                               JOIN users h_u ON h_u.id = h.changed_by
                               LEFT JOIN user_profiles h_p ON h_p.user_id = h_u.id AND h_p.deleted_at IS NULL
                               WHERE h.job_id = j.id AND h.to_status = 'Cancelled'
                               ORDER BY h.created_at DESC
                               LIMIT 1
                           ),
                           CASE 
                               WHEN j.status = 'Cancelled' AND j.updated_by IS NOT NULL THEN (
                                   SELECT COALESCE(NULLIF(TRIM(COALESCE(up_p.first_name, '') || ' ' || COALESCE(up_p.last_name, '')), ''), up_u.username)
                                   FROM users up_u
                                   LEFT JOIN user_profiles up_p ON up_p.user_id = up_u.id AND up_p.deleted_at IS NULL
                                   WHERE up_u.id = j.updated_by
                               )
                               ELSE NULL
                           END
                       ) AS ""cancelledByName"",
                       TO_CHAR(j.scheduled_start_at, 'YYYY-MM-DD') AS ""scheduledDate"",
                       TO_CHAR(j.scheduled_start_at, 'HH24:MI') AS ""scheduledTime"",
                       j.scheduled_start_at AS ""scheduledStartAt"",
                       CASE 
                           WHEN j.driver_id IS NULL THEN NULL
                           ELSE COALESCE(
                               NULLIF(TRIM(COALESCE(p.first_name, '') || ' ' || COALESCE(p.last_name, '')), ''),
                               u.username,
                               'Driver #' || CAST(j.driver_id AS TEXT)
                           )
                       END AS ""driverName"",
                       v.plate_number AS ""vehiclePlate""
                FROM jobs j
                LEFT JOIN user_profiles p ON p.user_id = j.driver_id AND p.deleted_at IS NULL
                LEFT JOIN users u ON u.id = j.driver_id AND u.deleted_at IS NULL
                LEFT JOIN vehicles v ON v.id = j.vehicle_id AND v.deleted_at IS NULL
                LEFT JOIN users cb_u ON cb_u.id = j.cancelled_by AND cb_u.deleted_at IS NULL
                LEFT JOIN user_profiles cb_p ON cb_p.user_id = cb_u.id AND cb_p.deleted_at IS NULL
                WHERE j.id = @id AND j.deleted_at IS NULL;";

            var job = await conn.QueryFirstOrDefaultAsync<AdminJobListItemDto>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            return Results.Ok(ApiResponse<AdminJobListItemDto>.Ok(job));
        })
        .Produces<ApiResponse<AdminJobListItemDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ดึงรายละเอียดงานตาม ID (Get Job by ID)");

        group.MapPut("/jobs/{id:long}", async (long id, [FromBody] UpdateJobDto req, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var oldJobSql = @"
                SELECT j.job_number AS ""jobNumber"", j.title, j.description, j.driver_id AS ""driverId"", j.vehicle_id AS ""vehicleId"", j.status,
                       j.pickup_location AS ""pickupLocation"", j.pickup_lat AS ""pickupLat"", j.pickup_lng AS ""pickupLng"",
                       j.contact_name AS ""contactName"", j.contact_phone AS ""contactPhone"", j.companions,
                       j.scheduled_start_at AS ""scheduledStartAt""
                FROM jobs j WHERE j.id = @id AND j.deleted_at IS NULL;";
            var oldJob = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(oldJobSql, new { id }, cancellationToken: ct));
            if (oldJob == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            if (!string.IsNullOrEmpty(req.Status) && req.Status != "Pending" && req.Status != "Cancelled")
            {
                if (!req.DriverId.HasValue) return Results.BadRequest(ApiResponse<string>.Fail("สถานะงานที่ระบุจำเป็นต้องมีพนักงานขับรถ"));
                if (!req.VehicleId.HasValue) return Results.BadRequest(ApiResponse<string>.Fail("สถานะงานที่ระบุจำเป็นต้องมียานพาหนะ"));
            }

            var status = req.DriverId.HasValue ? "Assigned" : "Pending";

            // Check if driver was reassigned/changed, log to job_assignment_histories
            var previousDriverId = (long?)oldJob.driverId;
            if (req.DriverId.HasValue && req.DriverId != previousDriverId)
            {
                var logSql = @"
                    INSERT INTO job_assignment_histories (job_id, driver_id, assigned_by, assigned_at)
                    VALUES (@id, @DriverId, @UserId, @Now);";
                await conn.ExecuteAsync(new CommandDefinition(logSql, new { id, req.DriverId, UserId = user.UserId, Now = DateTime.UtcNow }, cancellationToken: ct));
            }

            var sql = @"
                UPDATE jobs
                SET title = @Title,
                    description = @Description,
                    driver_id = @DriverId,
                    vehicle_id = @VehicleId,
                    status = CASE 
                        WHEN @ExplicitStatus IS NOT NULL AND @ExplicitStatus != '' THEN @ExplicitStatus
                        WHEN @DriverId IS NULL AND status IN ('Pending', 'Assigned') THEN 'Pending'
                        WHEN @DriverId IS NOT NULL AND status = 'Pending' THEN 'Assigned'
                        ELSE status 
                    END,
                    cancellation_reason = CASE
                        WHEN @ExplicitStatus = 'Cancelled' THEN @CancellationReason
                        ELSE cancellation_reason
                    END,
                    cancelled_at = CASE
                        WHEN @ExplicitStatus = 'Cancelled' AND cancelled_at IS NULL THEN @Now
                        ELSE cancelled_at
                    END,
                    cancelled_by = CASE
                        WHEN @ExplicitStatus = 'Cancelled' AND (cancelled_by IS NULL OR status != 'Cancelled') THEN @UserId
                        ELSE cancelled_by
                    END,
                    pickup_location = @PickupLocation,
                    pickup_lat = @PickupLat,
                    pickup_lng = @PickupLng,
                    contact_name = @ContactName,
                    contact_phone = @ContactPhone,
                    companions = @Companions,
                    scheduled_start_at = @ScheduledStartAt,
                    updated_by = @UserId,
                    updated_at = @Now
                WHERE id = @id AND deleted_at IS NULL;";

            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                id,
                req.Title,
                req.Description,
                req.DriverId,
                req.VehicleId,
                ExplicitStatus = req.Status,
                req.CancellationReason,
                Status = status,
                req.PickupLocation,
                req.PickupLat,
                req.PickupLng,
                req.ContactName,
                req.ContactPhone,
                req.Companions,
                req.ScheduledStartAt,
                UserId = user.UserId,
                Now = DateTime.UtcNow
            }, cancellationToken: ct));

            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            // Log status change history if status changed
            if (!string.IsNullOrEmpty(req.Status) && oldJob.status != req.Status)
            {
                var historySql = @"
                    INSERT INTO job_status_histories (job_id, from_status, to_status, changed_by, notes, created_at)
                    VALUES (@id, @FromStatus, @ToStatus, @UserId, @Notes, @Now);";
                await conn.ExecuteAsync(new CommandDefinition(historySql, new { id, FromStatus = (string)oldJob.status, ToStatus = req.Status, UserId = user.UserId, Notes = req.CancellationReason, Now = DateTime.UtcNow }, cancellationToken: ct));
            }

            // Calculate diff for audit log & notifications
            var changes = new List<string>();
            if (oldJob.title != req.Title) changes.Add($"หัวข้อ: '{oldJob.title}' -> '{req.Title}'");
            if (oldJob.description != req.Description) changes.Add($"รายละเอียด: '{oldJob.description}' -> '{req.Description}'");
            
            if (oldJob.driverId != req.DriverId)
            {
                var oldDriverName = oldJob.driverId != null ? await conn.ExecuteScalarAsync<string>("SELECT first_name || ' ' || last_name FROM user_profiles WHERE user_id = @driverId AND deleted_at IS NULL;", new { driverId = oldJob.driverId }) ?? $"#{oldJob.driverId}" : "ไม่ระบุ";
                var newDriverName = req.DriverId != null ? await conn.ExecuteScalarAsync<string>("SELECT first_name || ' ' || last_name FROM user_profiles WHERE user_id = @driverId AND deleted_at IS NULL;", new { driverId = req.DriverId }) ?? $"#{req.DriverId}" : "ไม่ระบุ";
                changes.Add($"คนขับ: '{oldDriverName}' -> '{newDriverName}'");
            }
            if (oldJob.vehicleId != req.VehicleId)
            {
                var oldPlate = oldJob.vehicleId != null ? await conn.ExecuteScalarAsync<string>("SELECT plate_number FROM vehicles WHERE id = @vehicleId;", new { vehicleId = oldJob.vehicleId }) ?? $"#{oldJob.vehicleId}" : "ไม่ระบุ";
                var newPlate = req.VehicleId != null ? await conn.ExecuteScalarAsync<string>("SELECT plate_number FROM vehicles WHERE id = @vehicleId;", new { vehicleId = req.VehicleId }) ?? $"#{req.VehicleId}" : "ไม่ระบุ";
                changes.Add($"รถ: '{oldPlate}' -> '{newPlate}'");
            }

            if (!string.IsNullOrEmpty(req.Status) && oldJob.status != req.Status) changes.Add($"สถานะ: '{oldJob.status}' -> '{req.Status}'");
            if (oldJob.pickupLocation != req.PickupLocation) changes.Add($"จุดรับ: '{oldJob.pickupLocation}' -> '{req.PickupLocation}'");
            
            var oldLat = (double?)oldJob.pickupLat;
            var oldLng = (double?)oldJob.pickupLng;
            if (oldLat != req.PickupLat || oldLng != req.PickupLng)
            {
                changes.Add($"พิกัด: ({oldLat?.ToString() ?? "-"}, {oldLng?.ToString() ?? "-"}) -> ({req.PickupLat?.ToString() ?? "-"}, {req.PickupLng?.ToString() ?? "-"})");
            }

            if (oldJob.contactName != req.ContactName) changes.Add($"ชื่อผู้ติดต่อ: '{oldJob.contactName}' -> '{req.ContactName}'");
            if (oldJob.contactPhone != req.ContactPhone) changes.Add($"เบอร์ผู้ติดต่อ: '{oldJob.contactPhone}' -> '{req.ContactPhone}'");
            if (oldJob.companions != req.Companions) changes.Add($"ผู้ติดตาม: '{oldJob.companions}' -> '{req.Companions}'");

            var oldScheduled = (DateTime?)oldJob.scheduledStartAt;
            if (oldScheduled != req.ScheduledStartAt)
            {
                changes.Add($"เวลานัดหมาย: '{oldScheduled?.ToString("yyyy-MM-dd HH:mm") ?? "-"}' -> '{req.ScheduledStartAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"}'");
            }

            var diffText = changes.Count > 0 ? string.Join(", ", changes) : "ไม่มีการเปลี่ยนแปลงฟิลด์หลัก";
            var currentJobNumber = (string)oldJob.jobNumber ?? id.ToString();
            await auditRepo.LogAsync(user.UserId, "UPDATE", "jobs", id.ToString(), $"แก้ไขงาน เลขที่งาน {currentJobNumber} [{diffText}]", ct: ct);

            // Handle Push Notifications: Cancellation, Reassignment, New Assignment, or Data Update
            var isCancelled = (!string.IsNullOrEmpty(req.Status) && req.Status == "Cancelled" && oldJob.status != "Cancelled");
            var isReassigned = (previousDriverId.HasValue && req.DriverId.HasValue && previousDriverId.Value != req.DriverId.Value);
            var isUnassigned = (previousDriverId.HasValue && !req.DriverId.HasValue);

            if (isCancelled)
            {
                var targetDriverId = req.DriverId ?? (oldJob.driverId != null ? (long?)Convert.ToInt64(oldJob.driverId) : null);
                if (targetDriverId.HasValue)
                {
                    var reason = !string.IsNullOrWhiteSpace(req.CancellationReason) ? req.CancellationReason : "ผู้ดูแลระบบยกเลิกงาน";
                    _ = pushNotificationService.SendJobCancelledNotificationAsync(targetDriverId.Value, id, currentJobNumber, req.Title ?? (string)oldJob.title, reason, ct);
                }
            }
            else if (isReassigned || isUnassigned)
            {
                if (previousDriverId.HasValue)
                {
                    _ = pushNotificationService.SendJobCancelledNotificationAsync(previousDriverId.Value, id, currentJobNumber, (string)oldJob.title, "งานถูกเปลี่ยนผู้ขับขี่หรือยกเลิกการมอบหมาย", ct);
                }
                if (req.DriverId.HasValue)
                {
                    _ = pushNotificationService.SendJobAssignedNotificationAsync(req.DriverId.Value, id, currentJobNumber, req.Title, req.PickupLocation, ct);
                }
            }
            else if (req.DriverId.HasValue && (previousDriverId != req.DriverId || oldJob.status == "Pending"))
            {
                _ = pushNotificationService.SendJobAssignedNotificationAsync(req.DriverId.Value, id, currentJobNumber, req.Title, req.PickupLocation, ct);
            }
            else if (req.DriverId.HasValue && changes.Count > 0)
            {
                // Send Job Updated Push Notification to the assigned driver
                _ = pushNotificationService.SendJobUpdatedNotificationAsync(req.DriverId.Value, id, currentJobNumber, req.Title ?? (string)oldJob.title, req.PickupLocation, diffText, ct);
            }

            List<string>? warnings = null;
            if (req.DriverId.HasValue)
            {
                var profileRepo = new UserProfileRepository(db);
                var driver = await profileRepo.GetByUserIdAsync(req.DriverId.Value, ct);
                if (driver != null && driver.LicenseExpirationDate.HasValue && driver.LicenseExpirationDate.Value < DateTime.UtcNow.Date)
                {
                    warnings = new List<string> { $"Warning: Driver '{driver.FirstName} {driver.LastName}' has an expired license!" };
                }
            }

            return Results.Ok(ApiResponse<string>.Ok("Job updated successfully", warnings: warnings));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("แก้ไขข้อมูลงาน (Update Job)");

        group.MapDelete("/jobs/{id:long}", async (long id, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var jobInfo = await conn.QueryFirstOrDefaultAsync(@"
                SELECT driver_id AS ""driverId"", job_number AS ""jobNumber"", title, status 
                FROM jobs 
                WHERE id = @id AND deleted_at IS NULL;", new { id });
            if (jobInfo == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            var sql = @"
                UPDATE jobs 
                SET deleted_at = @Now, deleted_by = @UserId 
                WHERE id = @id AND deleted_at IS NULL;";
            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, UserId = user.UserId, Now = DateTime.UtcNow }, cancellationToken: ct));
            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            // Notify driver if active job was deleted
            if (jobInfo.driverId != null && jobInfo.status != "Completed" && jobInfo.status != "Cancelled")
            {
                long driverId = Convert.ToInt64(jobInfo.driverId);
                string jobNum = (string)jobInfo.jobNumber ?? id.ToString();
                string title = (string)jobInfo.title ?? "";
                _ = pushNotificationService.SendJobCancelledNotificationAsync(driverId, id, jobNum, title, "งานถูกลบออกจากระบบโดยผู้ดูแลระบบ", ct);
            }

            var targetJobNumber = (string)jobInfo.jobNumber ?? id.ToString();
            await auditRepo.LogAsync(user.UserId, "DELETE", "jobs", id.ToString(), $"ลบงาน เลขที่งาน {targetJobNumber}", ct: ct);

            return Results.Ok(ApiResponse<string>.Ok("Job deleted successfully"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ลบงาน (Delete Job)");

        group.MapDelete("/jobs/all", async (ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var activeJobs = (await conn.QueryAsync(@"
                SELECT driver_id AS ""driverId"", id, job_number AS ""jobNumber"", title 
                FROM jobs 
                WHERE deleted_at IS NULL AND driver_id IS NOT NULL AND status NOT IN ('Completed', 'Cancelled');")).ToList();

            var sql = "UPDATE jobs SET deleted_at = @Now, deleted_by = @UserId WHERE deleted_at IS NULL;";
            var count = await conn.ExecuteAsync(new CommandDefinition(sql, new { UserId = user.UserId, Now = DateTime.UtcNow }, cancellationToken: ct));

            foreach (var j in activeJobs)
            {
                if (j.driverId != null)
                {
                    long driverId = Convert.ToInt64(j.driverId);
                    string jobNum = (string)j.jobNumber ?? j.id.ToString();
                    string title = (string)j.title ?? "";
                    _ = pushNotificationService.SendJobCancelledNotificationAsync(driverId, (long)j.id, jobNum, title, "งานทั้งหมดถูกลบออกจากระบบโดยผู้ดูแลระบบ", ct);
                }
            }

            await auditRepo.LogAsync(user.UserId, "DELETE_ALL", "jobs", null, $"ลบงานทั้งหมดจำนวน {count} งาน", ct: ct);

            return Results.Ok(ApiResponse<object>.Ok(new { deletedCount = count }, "Deleted all jobs successfully"));
        })
        .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
        .WithSummary("ลบงานทั้งหมด (Delete All Jobs)");

        group.MapGet("/audit-logs", async ([FromQuery] string? search, [FromQuery] long? userId, [FromQuery] string? entityName, [FromQuery] string? startDate, [FromQuery] string? endDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, DbConnectionFactory db = null!, CancellationToken ct = default) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;
            var offset = (page - 1) * pageSize;

            using var conn = db.CreateConnection();
            var countSql = @"
                SELECT COUNT(1)
                FROM audit_logs a
                LEFT JOIN users u ON u.id = a.user_id
                LEFT JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
                WHERE (@search IS NULL OR @search = '' OR a.action ILIKE '%' || @search || '%' OR a.entity_name ILIKE '%' || @search || '%' OR a.details ILIKE '%' || @search || '%' OR u.username ILIKE '%' || @search || '%' OR (p.first_name || ' ' || p.last_name) ILIKE '%' || @search || '%')
                  AND (@userId IS NULL OR a.user_id = @userId)
                  AND (@entityName IS NULL OR @entityName = '' OR a.entity_name = @entityName)
                  AND (@startDate IS NULL OR @startDate = '' OR a.created_at >= (@startDate || ' 00:00:00')::timestamp AT TIME ZONE 'Asia/Bangkok')
                  AND (@endDate IS NULL OR @endDate = '' OR a.created_at <= (@endDate || ' 23:59:59')::timestamp AT TIME ZONE 'Asia/Bangkok');";

            var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, new { search, userId, entityName, startDate, endDate }, cancellationToken: ct));

            var sql = @"
                SELECT a.id, a.user_id AS ""userId"", a.action, a.entity_name AS ""entityName"", a.entity_id AS ""entityId"",
                       a.details, a.ip_address AS ""ipAddress"",
                       TO_CHAR(a.created_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD HH24:MI:SS') AS ""createdAt"",
                       COALESCE(NULLIF(TRIM(p.first_name || ' ' || p.last_name), ''), u.username, 'System') AS ""userName""
                FROM audit_logs a
                LEFT JOIN users u ON u.id = a.user_id
                LEFT JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
                WHERE (@search IS NULL OR @search = '' OR a.action ILIKE '%' || @search || '%' OR a.entity_name ILIKE '%' || @search || '%' OR a.details ILIKE '%' || @search || '%' OR u.username ILIKE '%' || @search || '%' OR (p.first_name || ' ' || p.last_name) ILIKE '%' || @search || '%')
                  AND (@userId IS NULL OR a.user_id = @userId)
                  AND (@entityName IS NULL OR @entityName = '' OR a.entity_name = @entityName)
                  AND (@startDate IS NULL OR @startDate = '' OR a.created_at >= (@startDate || ' 00:00:00')::timestamp AT TIME ZONE 'Asia/Bangkok')
                  AND (@endDate IS NULL OR @endDate = '' OR a.created_at <= (@endDate || ' 23:59:59')::timestamp AT TIME ZONE 'Asia/Bangkok')
                ORDER BY a.id DESC
                LIMIT @pageSize OFFSET @offset;";

            var list = await conn.QueryAsync(new CommandDefinition(sql, new { search, userId, entityName, startDate, endDate, pageSize, offset }, cancellationToken: ct));

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return Results.Ok(ApiResponse<object>.Ok(new
            {
              items = list,
              totalCount,
              totalPages,
              page,
              pageSize
            }));
        })
        .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
        .WithSummary("ดึงประวัติการปฏิบัติงาน (Audit Logs)");

        group.MapGet("/vehicle-types", async (DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                SELECT vt.id, vt.name, vt.description,
                       (SELECT COUNT(1) FROM vehicles v WHERE v.vehicle_type_id = vt.id AND v.deleted_at IS NULL)::int AS ""vehicleCount"",
                       vt.created_at AS ""createdAt""
                FROM vehicle_types vt 
                WHERE vt.deleted_at IS NULL 
                ORDER BY vt.id ASC;";
            var list = await conn.QueryAsync<VehicleTypeItemDto>(sql);
            return Results.Ok(ApiResponse<IEnumerable<VehicleTypeItemDto>>.Ok(list));
        })
        .Produces<ApiResponse<IEnumerable<VehicleTypeItemDto>>>(StatusCodes.Status200OK)
        .WithSummary("ดึงประเภทรถทั้งหมด (List Vehicle Types)");

        group.MapPost("/vehicle-types", async ([FromBody] CreateVehicleTypeDto req, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(ApiResponse<string>.Fail("กรุณากรอกชื่อประเภทรถ"));

            using var conn = db.CreateConnection();
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM vehicle_types WHERE name = @Name AND deleted_at IS NULL;",
                new { req.Name });

            if (exists > 0)
                return Results.BadRequest(ApiResponse<string>.Fail("ประเภทรถนี้มีอยู่ในระบบแล้ว"));

            var id = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO vehicle_types (name, description, created_by, created_at) VALUES (@Name, @Description, @CreatedBy, @Now) RETURNING id;",
                new { req.Name, Description = req.Description ?? "-", CreatedBy = user.UserId, Now = DateTime.UtcNow });

            await auditRepo.LogAsync(user.UserId, "CREATE", "vehicle_types", id.ToString(), $"เพิ่มประเภทรถ: {req.Name}");

            return Results.Ok(ApiResponse<CreatedEntityResponseDto>.Ok(new CreatedEntityResponseDto(id, req.Name), "เพิ่มประเภทรถใหม่เรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<CreatedEntityResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("เพิ่มประเภทรถใหม่ (Create Vehicle Type)");

        group.MapPut("/vehicle-types/{id:long}", async (long id, [FromBody] UpdateVehicleTypeDto req, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(ApiResponse<string>.Fail("กรุณากรอกชื่อประเภทรถ"));

            using var conn = db.CreateConnection();
            var oldType = await conn.QueryFirstOrDefaultAsync("SELECT name, description FROM vehicle_types WHERE id = @id AND deleted_at IS NULL;", new { id });
            if (oldType == null)
                return Results.NotFound(ApiResponse<string>.Fail("ไม่พบประเภทรถนี้"));

            var duplicate = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM vehicle_types WHERE name = @Name AND id != @id AND deleted_at IS NULL;",
                new { req.Name, id });

            if (duplicate > 0)
                return Results.BadRequest(ApiResponse<string>.Fail("ชื่อประเภทรถนี้มีอยู่ในระบบแล้ว"));

            var newDescription = req.Description ?? "-";
            await conn.ExecuteAsync(
                "UPDATE vehicle_types SET name = @Name, description = @Description, updated_by = @UpdatedBy, updated_at = @Now WHERE id = @id;",
                new { id, req.Name, Description = newDescription, UpdatedBy = user.UserId, Now = DateTime.UtcNow });

            var oldName = (string)oldType.name;
            var oldDesc = (string)oldType.description ?? "-";

            var diffs = new List<string>();
            if (oldName != req.Name) diffs.Add($"ชื่อ: '{oldName}' -> '{req.Name}'");
            if (oldDesc != newDescription) diffs.Add($"คำอธิบาย: '{oldDesc}' -> '{newDescription}'");

            var diffText = diffs.Count > 0 ? string.Join(", ", diffs) : "ไม่มีการเปลี่ยนแปลง";
            await auditRepo.LogAsync(user.UserId, "UPDATE", "vehicle_types", id.ToString(), $"แก้ไขประเภทรถ '{req.Name}' [{diffText}]");

            return Results.Ok(ApiResponse<string>.Ok("แก้ไขประเภทรถเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("แก้ไขประเภทรถ (Update Vehicle Type)");

        group.MapDelete("/vehicle-types/{id:long}", async (long id, ICurrentUser user, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var typeName = await conn.ExecuteScalarAsync<string>("SELECT name FROM vehicle_types WHERE id = @id AND deleted_at IS NULL;", new { id });
            if (typeName == null)
                return Results.NotFound(ApiResponse<string>.Fail("ไม่พบประเภทรถนี้"));

            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM vehicles WHERE vehicle_type_id = @id AND deleted_at IS NULL;",
                new { id });

            if (count > 0)
                return Results.BadRequest(ApiResponse<string>.Fail("ไม่สามารถลบประเภทรถนี้ได้ เนื่องจากมีรถในระบบใช้งานประเภทรถนี้อยู่"));

            await conn.ExecuteAsync(
                "UPDATE vehicle_types SET deleted_at = @Now, deleted_by = @DeletedBy WHERE id = @id;",
                new { id, DeletedBy = user.UserId, Now = DateTime.UtcNow });

            await auditRepo.LogAsync(user.UserId, "DELETE", "vehicle_types", id.ToString(), $"ลบประเภทรถ: {typeName}");

            return Results.Ok(ApiResponse<string>.Ok("ลบประเภทรถเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ลบประเภทรถ (Delete Vehicle Type)");

        group.MapGet("/available-vehicles", async ([FromQuery] long? currentUserId, DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            // Get vehicles that are active and NOT bound to any active (non-deleted) user
            var sql = @"
                SELECT v.id,
                       v.plate_number AS ""plateNumber"",
                       v.model AS ""model"",
                       v.capacity AS ""capacity"",
                       v.is_active AS ""isActive"",
                       v.created_at AS ""createdAt"",
                       vt.name AS ""vehicleType"",
                       NULL::bigint AS ""assignedDriverId"",
                       NULL::text AS ""assignedDriverName"",
                       0::int AS ""activeJobsCount""
                FROM vehicles v
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                WHERE v.is_active = TRUE AND v.deleted_at IS NULL
                  AND (
                    NOT EXISTS (
                        SELECT 1 
                        FROM user_profiles p
                        INNER JOIN users u ON u.id = p.user_id AND u.deleted_at IS NULL
                        WHERE p.vehicle_id = v.id 
                          AND p.deleted_at IS NULL
                          AND (@currentUserId IS NULL OR p.user_id != @currentUserId)
                    )
                  )
                ORDER BY v.id DESC;";

            var list = await conn.QueryAsync<AdminVehicleListItemDto>(new CommandDefinition(sql, new { currentUserId }, cancellationToken: ct));
            return Results.Ok(ApiResponse<IEnumerable<AdminVehicleListItemDto>>.Ok(list));
        })
        .Produces<ApiResponse<IEnumerable<AdminVehicleListItemDto>>>(StatusCodes.Status200OK)
        .WithSummary("ดึงรายการรถที่ว่างพร้อมใช้งาน (Available Vehicles)");

        group.MapPost("/users", async ([FromBody] CreateUserDto req, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var userRepo = new UserRepository(db);
            var profileRepo = new UserProfileRepository(db);

            // 1. ตรวจสอบ ชื่อผู้ใช้งาน (Username) ซ้ำ
            var existingUser = await userRepo.GetByUsernameAsync(req.Username, ct);
            if (existingUser != null)
                return Results.BadRequest(ApiResponse<string>.Fail("ชื่อผู้ใช้งาน (Username) นี้มีอยู่ในระบบแล้ว"));

            if (req.DriverDetail != null)
            {
                var d = req.DriverDetail;

                // 2. ตรวจสอบ เลขบัตรประชาชน (ID Card No) ว่าต้องกรอก และเป็นตัวเลข 13 หลัก
                if (string.IsNullOrWhiteSpace(d.IdCardNo))
                    return Results.BadRequest(ApiResponse<string>.Fail("กรุณาระบุเลขบัตรประชาชน (13 หลัก)"));

                var idCardNo = d.IdCardNo.Trim();
                if (idCardNo.Length != 13 || !idCardNo.All(char.IsDigit))
                    return Results.BadRequest(ApiResponse<string>.Fail("เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก"));

                var checkIdCard = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM user_profiles WHERE id_card_no = @IdCardNo AND deleted_at IS NULL;",
                    new { IdCardNo = idCardNo });
                if (checkIdCard > 0)
                    return Results.BadRequest(ApiResponse<string>.Fail("เลขบัตรประชาชนนี้มีอยู่ในระบบแล้ว"));

                // 3. ตรวจสอบ รหัสพนักงาน (Employee Code) ซ้ำ
                if (!string.IsNullOrWhiteSpace(d.EmployeeCode))
                {
                    var checkEmpCode = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM user_profiles WHERE employee_code = @Code AND deleted_at IS NULL;",
                        new { Code = d.EmployeeCode.Trim() });
                    if (checkEmpCode > 0)
                        return Results.BadRequest(ApiResponse<string>.Fail("รหัสพนักงานนี้มีอยู่ในระบบแล้ว"));
                }

                // 4. ตรวจสอบ เบอร์โทรศัพท์ (Phone) ว่าต้องมี 10 หลัก และเป็นตัวเลขเท่านั้น
                if (string.IsNullOrWhiteSpace(d.Phone))
                    return Results.BadRequest(ApiResponse<string>.Fail("กรุณาระบุเบอร์โทรศัพท์ (10 หลัก)"));

                var phone = d.Phone.Trim();
                if (phone.Length != 10 || !phone.All(char.IsDigit))
                    return Results.BadRequest(ApiResponse<string>.Fail("เบอร์โทรศัพท์ต้องเป็นตัวเลข 10 หลัก"));

                var checkPhone = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM user_profiles WHERE phone = @Phone AND deleted_at IS NULL;",
                    new { Phone = phone });
                if (checkPhone > 0)
                    return Results.BadRequest(ApiResponse<string>.Fail("เบอร์โทรศัพท์นี้มีอยู่ในระบบแล้ว"));

                // 5. ตรวจสอบ รูปแบบอีเมล (Email Format ด้วย Regex) และความซ้ำซ้อน
                if (!string.IsNullOrWhiteSpace(d.Email))
                {
                    var email = d.Email.Trim();
                    var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
                    if (!emailRegex.IsMatch(email))
                        return Results.BadRequest(ApiResponse<string>.Fail("รูปแบบอีเมลไม่ถูกต้อง"));

                    var checkEmail = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM user_profiles WHERE email = @Email AND deleted_at IS NULL;",
                        new { Email = email });
                    if (checkEmail > 0)
                        return Results.BadRequest(ApiResponse<string>.Fail("อีเมลนี้มีอยู่ในระบบแล้ว"));
                }
            }

            var user = new User
            {
                Username = req.Username,
                PasswordHash = PasswordHasher.HashPassword(req.Password),
                Role = req.Role,
                IsActive = true,
                CreatedBy = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };

            var userId = await userRepo.CreateUserAsync(user, ct);

            if (req.DriverDetail != null)
            {
                var d = req.DriverDetail;
                var isAdmin = req.Role == "Admin";
                var profile = new UserProfile
                {
                    UserId = userId,
                    EmployeeCode = d.EmployeeCode,
                    FirstName = d.FirstName,
                    LastName = d.LastName,
                    Phone = d.Phone,
                    Email = d.Email,
                    IdCardNo = d.IdCardNo,
                    BirthDate = d.BirthDate,
                    LicenseNo = isAdmin ? null : d.LicenseNo,
                    LicenseIssueDate = isAdmin ? null : d.LicenseIssueDate,
                    LicenseExpirationDate = isAdmin ? null : d.LicenseExpirationDate,
                    VehicleId = isAdmin ? null : d.VehicleId,
                    IsActive = true,
                    CreatedBy = currentUser.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                await profileRepo.CreateUserProfileAsync(profile, ct);
            }

            await auditRepo.LogAsync(currentUser.UserId, "CREATE", "users", userId.ToString(), $"เพิ่มผู้ใช้งานใหม่ Username: {req.Username} Role: {req.Role}", ct: ct);

            return Results.Ok(ApiResponse<CreatedUserResponseDto>.Ok(new CreatedUserResponseDto(userId, req.Username, req.Role), "User created successfully"));
        })
        .Produces<ApiResponse<CreatedUserResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("เพิ่มผู้ใช้งาน / พนักงานใหม่ (Create User)");

        group.MapPut("/users/{userId:long}", async (long userId, [FromBody] UpdateUserDto req, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var userRepo = new UserRepository(db);

            var existingUser = await userRepo.GetByIdAsync(userId, ct);
            if (existingUser == null) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบผู้ใช้งานนี้"));

            var oldProfileSql = @"
                SELECT first_name AS ""firstName"", last_name AS ""lastName"", phone, email, 
                       id_card_no AS ""idCardNo"", birth_date AS ""birthDate"",
                       license_no AS ""licenseNo"", 
                       TO_CHAR(license_issue_date, 'YYYY-MM-DD') AS ""licenseIssueDate"",
                       TO_CHAR(license_expiration_date, 'YYYY-MM-DD') AS ""licenseExpirationDate"",
                       vehicle_id AS ""vehicleId""
                FROM user_profiles WHERE user_id = @userId AND deleted_at IS NULL;";
            var oldProfile = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(oldProfileSql, new { userId }, cancellationToken: ct));

            var now = DateTime.UtcNow;
            string? passwordHash = null;
            if (!string.IsNullOrWhiteSpace(req.Password))
            {
                passwordHash = PasswordHasher.HashPassword(req.Password);
            }

            var updateUserSql = @"
                UPDATE users
                SET role = @Role,
                    is_active = COALESCE(@IsActive, is_active),
                    password_hash = CASE WHEN @PasswordHash IS NOT NULL AND @PasswordHash != '' THEN @PasswordHash ELSE password_hash END,
                    updated_at = @Now,
                    updated_by = @UpdatedBy
                WHERE id = @userId AND deleted_at IS NULL;";

            await conn.ExecuteAsync(new CommandDefinition(updateUserSql, new
            {
                req.Role,
                IsActive = req.IsActive,
                PasswordHash = passwordHash,
                Now = now,
                UpdatedBy = currentUser.UserId,
                userId
            }, cancellationToken: ct));

            if (req.DriverDetail != null)
            {
                var d = req.DriverDetail;
                var isAdmin = req.Role == "Admin";

                if (!string.IsNullOrWhiteSpace(d.Phone))
                {
                    var phone = d.Phone.Trim();
                    if (phone.Length != 10 || !phone.All(char.IsDigit))
                        return Results.BadRequest(ApiResponse<string>.Fail("เบอร์โทรศัพท์ต้องเป็นตัวเลข 10 หลัก"));

                    var checkPhone = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM user_profiles WHERE phone = @Phone AND user_id != @userId AND deleted_at IS NULL;",
                        new { Phone = phone, userId });
                    if (checkPhone > 0)
                        return Results.BadRequest(ApiResponse<string>.Fail("เบอร์โทรศัพท์นี้มีอยู่ในระบบแล้ว"));
                }

                if (!string.IsNullOrWhiteSpace(d.Email))
                {
                    var email = d.Email.Trim();
                    var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
                    if (!emailRegex.IsMatch(email))
                        return Results.BadRequest(ApiResponse<string>.Fail("รูปแบบอีเมลไม่ถูกต้อง"));

                    var checkEmail = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM user_profiles WHERE email = @Email AND user_id != @userId AND deleted_at IS NULL;",
                        new { Email = email, userId });
                    if (checkEmail > 0)
                        return Results.BadRequest(ApiResponse<string>.Fail("อีเมลนี้มีอยู่ในระบบแล้ว"));
                }

                var hasProfile = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM user_profiles WHERE user_id = @userId AND deleted_at IS NULL;",
                    new { userId });

                if (hasProfile > 0)
                {
                    var updateProfileSql = @"
                        UPDATE user_profiles
                        SET first_name = @FirstName,
                            last_name = @LastName,
                            phone = @Phone,
                            email = @Email,
                            id_card_no = @IdCardNo,
                            birth_date = @BirthDate,
                            license_no = @LicenseNo,
                            license_issue_date = @LicenseIssueDate,
                            license_expiration_date = @LicenseExpirationDate,
                            vehicle_id = @VehicleId,
                            updated_at = @Now,
                            updated_by = @UpdatedBy
                        WHERE user_id = @userId AND deleted_at IS NULL;";

                    await conn.ExecuteAsync(new CommandDefinition(updateProfileSql, new
                    {
                        d.FirstName,
                        d.LastName,
                        d.Phone,
                        d.Email,
                        d.IdCardNo,
                        d.BirthDate,
                        LicenseNo = isAdmin ? null : d.LicenseNo,
                        LicenseIssueDate = isAdmin ? (DateTime?)null : d.LicenseIssueDate,
                        LicenseExpirationDate = isAdmin ? (DateTime?)null : d.LicenseExpirationDate,
                        VehicleId = isAdmin ? (long?)null : d.VehicleId,
                        Now = now,
                        UpdatedBy = currentUser.UserId,
                        userId
                    }, cancellationToken: ct));
                }
                else
                {
                    var profileRepo = new UserProfileRepository(db);
                    var profile = new UserProfile
                    {
                        UserId = userId,
                        EmployeeCode = d.EmployeeCode,
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        Phone = d.Phone,
                        Email = d.Email,
                        IdCardNo = d.IdCardNo,
                        BirthDate = d.BirthDate,
                        LicenseNo = isAdmin ? null : d.LicenseNo,
                        LicenseIssueDate = isAdmin ? null : d.LicenseIssueDate,
                        LicenseExpirationDate = isAdmin ? null : d.LicenseExpirationDate,
                        VehicleId = isAdmin ? null : d.VehicleId,
                        IsActive = true,
                        CreatedBy = currentUser.UserId,
                        CreatedAt = now
                    };
                    await profileRepo.CreateUserProfileAsync(profile, ct);
                }
            }

            // Calculate diffs
            var diffs = new List<string>();
            if (existingUser.Role != req.Role) diffs.Add($"สิทธิ์: '{existingUser.Role}' -> '{req.Role}'");
            if (req.IsActive.HasValue && existingUser.IsActive != req.IsActive.Value) diffs.Add($"สถานะการใช้งาน: '{existingUser.IsActive}' -> '{req.IsActive.Value}'");
            if (!string.IsNullOrWhiteSpace(req.Password)) diffs.Add("รหัสผ่าน: เปลี่ยนใหม่");

            if (req.DriverDetail != null && oldProfile != null)
            {
                var oldFirstName = (string)oldProfile.firstName ?? "";
                var oldLastName = (string)oldProfile.lastName ?? "";
                var oldPhone = (string)oldProfile.phone ?? "";
                var oldEmail = (string)oldProfile.email ?? "";
                var oldLicense = (string)oldProfile.licenseNo ?? "";
                var oldIssueDate = (string)oldProfile.licenseIssueDate ?? "";
                var oldExpDate = (string)oldProfile.licenseExpirationDate ?? "";
                var oldVehicleId = (long?)oldProfile.vehicleId;

                var d = req.DriverDetail;
                if (oldFirstName != d.FirstName || oldLastName != d.LastName)
                    diffs.Add($"ชื่อ-นามสกุล: '{oldFirstName} {oldLastName}' -> '{d.FirstName} {d.LastName}'");
                if (oldPhone != d.Phone) diffs.Add($"เบอร์โทร: '{oldPhone}' -> '{d.Phone}'");
                if (oldEmail != d.Email) diffs.Add($"อีเมล: '{oldEmail}' -> '{d.Email}'");
                if (oldLicense != d.LicenseNo) diffs.Add($"เลขใบขับขี่: '{oldLicense}' -> '{d.LicenseNo}'");
                
                var newIssueDate = d.LicenseIssueDate?.ToString("yyyy-MM-dd") ?? "";
                if (oldIssueDate != newIssueDate && !string.IsNullOrEmpty(newIssueDate))
                    diffs.Add($"วันออกใบขับขี่: '{oldIssueDate}' -> '{newIssueDate}'");

                var newExpDate = d.LicenseExpirationDate?.ToString("yyyy-MM-dd") ?? "";
                if (oldExpDate != newExpDate && !string.IsNullOrEmpty(newExpDate))
                    diffs.Add($"วันหมดอายุใบขับขี่: '{oldExpDate}' -> '{newExpDate}'");

                if (oldVehicleId != d.VehicleId) diffs.Add($"รถ: #{oldVehicleId ?? 0} -> #{d.VehicleId ?? 0}");
            }

            var userDisplayName = existingUser.Username;
            if (req.DriverDetail != null && (!string.IsNullOrWhiteSpace(req.DriverDetail.FirstName) || !string.IsNullOrWhiteSpace(req.DriverDetail.LastName)))
            {
                userDisplayName = $"{req.DriverDetail.FirstName} {req.DriverDetail.LastName}".Trim();
            }

            var diffText = diffs.Count > 0 ? string.Join(", ", diffs) : "ไม่มีการเปลี่ยนแปลง";
            await auditRepo.LogAsync(currentUser.UserId, "UPDATE", "users", userId.ToString(), $"แก้ไขข้อมูลผู้ใช้งาน: {userDisplayName} (Username: {existingUser.Username}) [{diffText}]");

            return Results.Ok(ApiResponse<string>.Ok("อัปเดตข้อมูลผู้ใช้งานเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("แก้ไขข้อมูลผู้ใช้งาน (Update User)");

        group.MapDelete("/users/{userId:long}", async (long userId, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            if (currentUser.UserId == userId)
            {
                return Results.BadRequest(ApiResponse<string>.Fail("ไม่สามารถลบบัญชีผู้ใช้งานของตนเองได้"));
            }

            using var conn = db.CreateConnection();
            var targetUserSql = @"
                SELECT u.username,
                       COALESCE(NULLIF(TRIM(p.first_name || ' ' || p.last_name), ''), u.username) AS name
                FROM users u
                LEFT JOIN user_profiles p ON p.user_id = u.id
                WHERE u.id = @userId AND u.deleted_at IS NULL;";
            var targetUser = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(targetUserSql, new { userId }, cancellationToken: ct));
            if (targetUser == null) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบผู้ใช้งานนี้"));

            var now = DateTime.UtcNow;

            // Soft delete user
            var deleteUserSql = @"
                UPDATE users
                SET deleted_at = @Now, deleted_by = @DeletedBy
                WHERE id = @userId AND deleted_at IS NULL;";
            await conn.ExecuteAsync(new CommandDefinition(deleteUserSql, new { Now = now, DeletedBy = currentUser.UserId, userId }, cancellationToken: ct));

            // Soft delete corresponding user profile if exists
            var deleteProfileSql = @"
                UPDATE user_profiles
                SET deleted_at = @Now, deleted_by = @DeletedBy
                WHERE user_id = @userId AND deleted_at IS NULL;";
            await conn.ExecuteAsync(new CommandDefinition(deleteProfileSql, new { Now = now, DeletedBy = currentUser.UserId, userId }, cancellationToken: ct));

            var userDisplayName = (string)targetUser.name;
            var username = (string)targetUser.username;
            await auditRepo.LogAsync(currentUser.UserId, "DELETE", "users", userId.ToString(), $"ลบผู้ใช้งาน: {userDisplayName} (Username: {username})");

            return Results.Ok(ApiResponse<string>.Ok("ลบผู้ใช้งานเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ลบผู้ใช้งาน (Delete User)");

        // Vehicles Endpoints
        group.MapGet("/vehicles", async ([FromQuery] string? search, DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var sql = @"
                SELECT v.id,
                       v.plate_number AS ""plateNumber"",
                       v.model AS ""model"",
                       v.capacity AS ""capacity"",
                       v.is_active AS ""isActive"",
                       v.created_at AS ""createdAt"",
                       vt.name AS ""vehicleType"",
                       p.user_id AS ""assignedDriverId"",
                       p.first_name || ' ' || p.last_name AS ""assignedDriverName"",
                       (
                           SELECT COUNT(1)
                           FROM jobs j
                           WHERE j.vehicle_id = v.id
                             AND j.status NOT IN ('Completed', 'Cancelled')
                             AND j.deleted_at IS NULL
                       ) AS ""activeJobsCount""
                FROM vehicles v
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                LEFT JOIN user_profiles p ON p.vehicle_id = v.id AND p.deleted_at IS NULL
                LEFT JOIN users u ON u.id = p.user_id AND u.deleted_at IS NULL
                WHERE v.deleted_at IS NULL
                  AND (@search IS NULL OR @search = '' OR v.plate_number ILIKE '%' || @search || '%' OR v.model ILIKE '%' || @search || '%')
                ORDER BY v.id DESC;";

            var list = await conn.QueryAsync<AdminVehicleListItemDto>(new CommandDefinition(sql, new { search }, cancellationToken: ct));
            return Results.Ok(ApiResponse<IEnumerable<AdminVehicleListItemDto>>.Ok(list));
        })
        .Produces<ApiResponse<IEnumerable<AdminVehicleListItemDto>>>(StatusCodes.Status200OK)
        .WithSummary("ดึงรายการยานพาหนะทั้งหมด (List Vehicles)");

        group.MapPost("/vehicles", async ([FromBody] CreateVehicleDto req, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.PlateNumber))
                return Results.BadRequest(ApiResponse<string>.Fail("กรุณากรอกทะเบียนรถ"));

            using var conn = db.CreateConnection();
            var checkPlate = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM vehicles WHERE plate_number = @PlateNumber AND deleted_at IS NULL;",
                new { req.PlateNumber });

            if (checkPlate > 0)
                return Results.BadRequest(ApiResponse<string>.Fail("ทะเบียนรถนี้มีอยู่ในระบบแล้ว"));

            long? vehicleTypeId = null;
            if (!string.IsNullOrWhiteSpace(req.VehicleType))
            {
                vehicleTypeId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM vehicle_types WHERE name = @Name LIMIT 1;",
                    new { Name = req.VehicleType });

                if (!vehicleTypeId.HasValue)
                {
                    var insertVtSql = "INSERT INTO vehicle_types (name, description) VALUES (@Name, 'เพิ่มใหม่') RETURNING id;";
                    vehicleTypeId = await conn.ExecuteScalarAsync<long>(insertVtSql, new { Name = req.VehicleType });
                }
            }

            var insertSql = @"
                INSERT INTO vehicles (plate_number, model, vehicle_type_id, capacity, is_active, created_by, created_at)
                VALUES (@PlateNumber, @Model, @VehicleTypeId, @Capacity, TRUE, @CreatedBy, @Now)
                RETURNING id;";

            var id = await conn.ExecuteScalarAsync<long>(insertSql, new
            {
                req.PlateNumber,
                Model = req.Model ?? "-",
                VehicleTypeId = vehicleTypeId,
                req.Capacity,
                CreatedBy = currentUser.UserId,
                Now = DateTime.UtcNow
            });

            await auditRepo.LogAsync(currentUser.UserId, "CREATE", "vehicles", id.ToString(), $"เพิ่มข้อมูลรถใหม่ ทะเบียน {req.PlateNumber}", ct: ct);

            return Results.Ok(ApiResponse<CreatedEntityResponseDto>.Ok(new CreatedEntityResponseDto(id, req.PlateNumber), "เพิ่มข้อมูลรถเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<CreatedEntityResponseDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("เพิ่มยานพาหนะใหม่ (Create Vehicle)");

        group.MapPut("/vehicles/{id:long}", async (long id, [FromBody] UpdateVehicleDto req, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var oldVehicleSql = @"
                SELECT v.plate_number AS ""plateNumber"", v.model, v.capacity, v.is_active AS ""isActive"", vt.name AS ""vehicleType""
                FROM vehicles v
                LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
                WHERE v.id = @id AND v.deleted_at IS NULL;";
            var oldVehicle = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(oldVehicleSql, new { id }, cancellationToken: ct));
            if (oldVehicle == null) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบข้อมูลรถที่ต้องการแก้ไข"));

            var checkPlate = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM vehicles WHERE plate_number = @PlateNumber AND id != @id AND deleted_at IS NULL;",
                new { req.PlateNumber, id });

            if (checkPlate > 0)
                return Results.BadRequest(ApiResponse<string>.Fail("ทะเบียนรถนี้ถูกใช้งานในคันอื่นแล้ว"));

            long? vehicleTypeId = null;
            if (!string.IsNullOrWhiteSpace(req.VehicleType))
            {
                vehicleTypeId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM vehicle_types WHERE name = @Name LIMIT 1;",
                    new { Name = req.VehicleType });

                if (!vehicleTypeId.HasValue)
                {
                    var insertVtSql = "INSERT INTO vehicle_types (name, description) VALUES (@Name, 'เพิ่มใหม่') RETURNING id;";
                    vehicleTypeId = await conn.ExecuteScalarAsync<long>(insertVtSql, new { Name = req.VehicleType });
                }
            }

            var updateSql = @"
                UPDATE vehicles
                SET plate_number = @PlateNumber,
                    model = @Model,
                    vehicle_type_id = @VehicleTypeId,
                    capacity = @Capacity,
                    is_active = @IsActive,
                    updated_at = @Now,
                    updated_by = @UpdatedBy
                WHERE id = @id AND deleted_at IS NULL;";

            var affected = await conn.ExecuteAsync(updateSql, new
            {
                req.PlateNumber,
                Model = req.Model ?? "-",
                VehicleTypeId = vehicleTypeId,
                req.VehicleType,
                req.Capacity,
                req.IsActive,
                Now = DateTime.UtcNow,
                UpdatedBy = currentUser.UserId,
                id
            });

            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบข้อมูลรถที่ต้องการแก้ไข"));

            // Calculate diffs
            var diffs = new List<string>();
            var oldPlate = (string)oldVehicle.plateNumber ?? "";
            var oldModel = (string)oldVehicle.model ?? "";
            var oldType = (string)oldVehicle.vehicleType ?? "";
            var oldCap = (double)oldVehicle.capacity;
            var oldActive = (bool)oldVehicle.isActive;

            if (oldPlate != req.PlateNumber) diffs.Add($"ทะเบียน: '{oldPlate}' -> '{req.PlateNumber}'");
            if (oldModel != req.Model) diffs.Add($"รุ่นรถ: '{oldModel}' -> '{req.Model}'");
            if (oldType != req.VehicleType) diffs.Add($"ประเภทรถ: '{oldType}' -> '{req.VehicleType}'");
            if (Math.Abs(oldCap - req.Capacity) > 0.001) diffs.Add($"ความจุ: '{oldCap}' -> '{req.Capacity}'");
            if (oldActive != req.IsActive) diffs.Add($"สถานะ: '{oldActive}' -> '{req.IsActive}'");

            var diffText = diffs.Count > 0 ? string.Join(", ", diffs) : "ไม่มีการเปลี่ยนแปลง";
            await auditRepo.LogAsync(currentUser.UserId, "UPDATE", "vehicles", id.ToString(), $"แก้ไขข้อมูลรถ ทะเบียน {req.PlateNumber} [{diffText}]");

            return Results.Ok(ApiResponse<string>.Ok("อัปเดตข้อมูลรถเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("แก้ไขข้อมูลยานพาหนะ (Update Vehicle)");

        group.MapDelete("/vehicles/{id:long}", async (long id, ICurrentUser currentUser, DbConnectionFactory db, AuditLogRepository auditRepo, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var plateNumber = await conn.ExecuteScalarAsync<string>("SELECT plate_number FROM vehicles WHERE id = @id;", new { id });
            var deleteSql = @"
                UPDATE vehicles
                SET deleted_at = @Now, deleted_by = @DeletedBy
                WHERE id = @id AND deleted_at IS NULL;";

            var affected = await conn.ExecuteAsync(deleteSql, new { Now = DateTime.UtcNow, DeletedBy = currentUser.UserId, id });
            if (affected == 0) return Results.NotFound(ApiResponse<string>.Fail("ไม่พบข้อมูลรถที่ต้องการลบ"));

            var plateText = plateNumber ?? id.ToString();
            await auditRepo.LogAsync(currentUser.UserId, "DELETE", "vehicles", id.ToString(), $"ลบข้อมูลรถ ทะเบียน {plateText}");

            return Results.Ok(ApiResponse<string>.Ok("ลบข้อมูลรถเรียบร้อยแล้ว"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status404NotFound)
        .WithSummary("ลบข้อมูลยานพาหนะ (Delete Vehicle)");

        // Dashboard Endpoint
        group.MapGet("/dashboard", async (DbConnectionFactory db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();

            var totalJobsToday = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL 
                  AND (created_at AT TIME ZONE 'Asia/Bangkok')::date = (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok')::date;");

            var pendingJobs = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL AND status = 'Pending';");

            var inProgressJobs = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL AND status IN ('Assigned', 'Started', 'Arrived', 'In Progress');");

            var completedJobs = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL AND status = 'Completed';");

            var cancelledJobs = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL AND status = 'Cancelled';");

            var availableDrivers = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM users u
                INNER JOIN user_profiles p ON p.user_id = u.id AND p.deleted_at IS NULL
                WHERE u.role = 'Driver' AND u.is_active = TRUE AND u.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM jobs j 
                      WHERE j.driver_id = u.id AND j.status IN ('Assigned', 'Started', 'Arrived', 'In Progress') AND j.deleted_at IS NULL
                  );");

            // Hourly Statistics Today
            var hourlyStatsSql = @"
                SELECT TO_CHAR(h.time_slot, 'HH24:00') AS time,
                       COUNT(CASE WHEN j.status = 'Completed' THEN 1 END)::int AS completed,
                       COUNT(CASE WHEN j.status IN ('Assigned', 'Started', 'Arrived', 'In Progress') THEN 1 END)::int AS inprogress,
                       COUNT(CASE WHEN j.status = 'Cancelled' THEN 1 END)::int AS cancelled
                FROM generate_series(
                    (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok')::date + INTERVAL '8 hours',
                    (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok')::date + INTERVAL '20 hours',
                    INTERVAL '2 hours'
                ) h(time_slot)
                LEFT JOIN jobs j ON j.deleted_at IS NULL 
                  AND (j.created_at AT TIME ZONE 'Asia/Bangkok') >= h.time_slot 
                  AND (j.created_at AT TIME ZONE 'Asia/Bangkok') < h.time_slot + INTERVAL '2 hours'
                GROUP BY h.time_slot
                ORDER BY h.time_slot ASC;";

            var hourlyStats = await conn.QueryAsync<ChartStatItemDto>(new CommandDefinition(hourlyStatsSql, cancellationToken: ct));

            // Monthly Statistics (Day by day for current month)
            var monthlyStatsSql = @"
                SELECT TO_CHAR(d.day_slot, 'DD/MM') AS time,
                       COUNT(CASE WHEN j.status = 'Completed' THEN 1 END)::int AS completed,
                       COUNT(CASE WHEN j.status IN ('Assigned', 'Started', 'Arrived', 'In Progress', 'Pending') THEN 1 END)::int AS inprogress,
                       COUNT(CASE WHEN j.status = 'Cancelled' THEN 1 END)::int AS cancelled
                FROM generate_series(
                    DATE_TRUNC('month', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok'),
                    DATE_TRUNC('month', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok') + INTERVAL '1 month' - INTERVAL '1 day',
                    INTERVAL '1 day'
                ) d(day_slot)
                LEFT JOIN jobs j ON j.deleted_at IS NULL 
                  AND (j.created_at AT TIME ZONE 'Asia/Bangkok')::date = d.day_slot::date
                GROUP BY d.day_slot
                ORDER BY d.day_slot ASC;";

            var monthlyStats = await conn.QueryAsync<ChartStatItemDto>(new CommandDefinition(monthlyStatsSql, cancellationToken: ct));

            // Yearly Statistics (Month by month for current year)
            var yearlyStatsSql = @"
                SELECT TO_CHAR(m.month_slot, 'Mon') AS time,
                       COUNT(CASE WHEN j.status = 'Completed' THEN 1 END)::int AS completed,
                       COUNT(CASE WHEN j.status IN ('Assigned', 'Started', 'Arrived', 'In Progress', 'Pending') THEN 1 END)::int AS inprogress,
                       COUNT(CASE WHEN j.status = 'Cancelled' THEN 1 END)::int AS cancelled
                FROM generate_series(
                    DATE_TRUNC('year', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok'),
                    DATE_TRUNC('year', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok') + INTERVAL '11 months',
                    INTERVAL '1 month'
                ) m(month_slot)
                LEFT JOIN jobs j ON j.deleted_at IS NULL 
                  AND DATE_TRUNC('month', j.created_at AT TIME ZONE 'Asia/Bangkok') = m.month_slot
                GROUP BY m.month_slot
                ORDER BY m.month_slot ASC;";

            var yearlyStats = await conn.QueryAsync<ChartStatItemDto>(new CommandDefinition(yearlyStatsSql, cancellationToken: ct));

            var totalJobsThisMonth = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL 
                  AND DATE_TRUNC('month', created_at AT TIME ZONE 'Asia/Bangkok') = DATE_TRUNC('month', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok');");

            var totalJobsThisYear = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM jobs 
                WHERE deleted_at IS NULL 
                  AND DATE_TRUNC('year', created_at AT TIME ZONE 'Asia/Bangkok') = DATE_TRUNC('year', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Bangkok');");

            var dashboardDto = new DashboardSummaryResponseDto(
                TotalJobsToday: totalJobsToday,
                TotalJobsThisMonth: totalJobsThisMonth,
                TotalJobsThisYear: totalJobsThisYear,
                PendingJobs: pendingJobs,
                InProgressJobs: inProgressJobs,
                CompletedJobs: completedJobs,
                CancelledJobs: cancelledJobs,
                AvailableDrivers: availableDrivers,
                HourlyStats: hourlyStats,
                MonthlyStats: monthlyStats,
                YearlyStats: yearlyStats
            );

            return Results.Ok(ApiResponse<DashboardSummaryResponseDto>.Ok(dashboardDto));
        })
        .Produces<ApiResponse<DashboardSummaryResponseDto>>(StatusCodes.Status200OK)
        .WithSummary("ดึงข้อมูลสรุปแดชบอร์ด (Dashboard Summary)");

        group.MapPost("/notifications/test", async ([FromBody] TestNotificationRequestDto req, ICurrentUser user, Infrastructure.Services.PushNotificationService pushNotificationService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            {
                return Results.BadRequest(ApiResponse<string>.Fail("กรุณาระบุ Title และ Body"));
            }

            var payload = new
            {
                type = req.Type ?? "SYSTEM_TEST",
                jobId = req.JobId?.ToString() ?? "",
                click_action = "FLUTTER_NOTIFICATION_CLICK"
            };

            if (!string.IsNullOrWhiteSpace(req.FcmToken))
            {
                var success = await pushNotificationService.SendFcmPushAsync(req.FcmToken, req.Title, req.Body, payload, ct);
                return Results.Ok(ApiResponse<string>.Ok(success ? "ส่งการแจ้งเตือนไปยัง Token สำเร็จ" : "ส่งการแจ้งเตือนไม่สำเร็จ"));
            }
            else if (req.UserId.HasValue && req.UserId.Value > 0)
            {
                var success = await pushNotificationService.SendNotificationToUserAsync(req.UserId.Value, req.Title, req.Body, payload, ct);
                return Results.Ok(ApiResponse<string>.Ok(success ? $"ส่งการแจ้งเตือนไปยัง User #{req.UserId} สำเร็จ" : "ส่งการแจ้งเตือนไม่สำเร็จ"));
            }

            return Results.BadRequest(ApiResponse<string>.Fail("กรุณาระบุ UserId หรือ FcmToken"));
        })
        .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
        .Produces<ApiResponse<string>>(StatusCodes.Status400BadRequest)
        .WithSummary("ทดสอบส่งการแจ้งเตือน Push Notification (Test Notification)");
    }
}
