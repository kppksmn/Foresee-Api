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

        mobileGroup.MapGet("/me", async (ICurrentUser currentUser, Infrastructure.Services.AuthService authService, CancellationToken ct) =>
        {
            var profile = await authService.GetProfileAsync(currentUser.UserId, currentUser.Role, ct);
            if (profile == null) return Results.NotFound();
            return Results.Ok(ApiResponse<MobileMeResponseDto>.Ok(profile));
        });

        var jobsGroup = mobileGroup.MapGroup("/jobs").RequireAuthorization("MobileDriver");

        jobsGroup.MapGet("/", async ([FromQuery] string? status, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentDriver.DriverId <= 0) return Results.BadRequest(ApiResponse<string>.Fail("Driver profile not found"));
            var jobs = await jobRepo.GetJobsForDriverAsync(currentDriver.DriverId, status, ct);
            return Results.Ok(ApiResponse<IEnumerable<JobDto>>.Ok(jobs));
        });

        jobsGroup.MapGet("/{jobId:long}", async (long jobId, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentDriver.DriverId <= 0) return Results.BadRequest(ApiResponse<string>.Fail("Driver profile not found"));
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));
            return Results.Ok(ApiResponse<Job>.Ok(job));
        });

        jobsGroup.MapPost("/{jobId:long}/start", async (long jobId, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentDriver.DriverId <= 0) return Results.BadRequest(ApiResponse<string>.Fail("Driver profile not found"));
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Assigned", "Started", currentDriver.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Assigned' status."));

            return Results.Ok(ApiResponse<string>.Ok("Job started successfully"));
        });

        jobsGroup.MapPost("/{jobId:long}/arrive", async (long jobId, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentDriver.DriverId <= 0) return Results.BadRequest(ApiResponse<string>.Fail("Driver profile not found"));
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Started", "Arrived", currentDriver.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Started' status."));

            return Results.Ok(ApiResponse<string>.Ok("Arrived at destination"));
        });

        jobsGroup.MapPost("/{jobId:long}/complete", async (long jobId, ICurrentDriver currentDriver, JobRepository jobRepo, CancellationToken ct) =>
        {
            if (currentDriver.DriverId <= 0) return Results.BadRequest(ApiResponse<string>.Fail("Driver profile not found"));
            var job = await jobRepo.GetByIdAndDriverAsync(jobId, currentDriver.DriverId, ct);
            if (job == null) return Results.NotFound(ApiResponse<string>.Fail("Job not found"));

            bool success = await jobRepo.UpdateStatusAtomicAsync(jobId, "Arrived", "Completed", currentDriver.UserId, DateTime.UtcNow, ct);
            if (!success) return Results.BadRequest(ApiResponse<string>.Fail("Invalid job status transition. Must be in 'Arrived' status."));

            return Results.Ok(ApiResponse<string>.Ok("Job completed successfully"));
        });
    }
}
