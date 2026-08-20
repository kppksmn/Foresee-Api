using System.Text;
using System.Text.Json;
using Core.Entities;
using Dapper;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class PushNotificationService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly object _firebaseInitLock = new object();
    private static bool _firebaseInitialized = false;
    private static bool _firebaseAvailable = false;

    private readonly DbConnectionFactory _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        DbConnectionFactory db,
        IConfiguration config,
        ILogger<PushNotificationService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private void EnsureFirebaseInitialized()
    {
        if (_firebaseInitialized) return;

        lock (_firebaseInitLock)
        {
            if (_firebaseInitialized) return;

            try
            {
                if (FirebaseApp.DefaultInstance != null)
                {
                    _firebaseAvailable = true;
                    _firebaseInitialized = true;
                    return;
                }

                var credPath = _config["Firebase:CredentialsPath"] ?? "firebase-admin.json";
                var credJson = _config["Firebase:CredentialJson"];

                GoogleCredential? credential = null;

                if (!string.IsNullOrWhiteSpace(credJson))
                {
                    credential = GoogleCredential.FromJson(credJson);
                }
                else if (File.Exists(credPath))
                {
                    credential = GoogleCredential.FromFile(credPath);
                }
                else
                {
                    var envCred = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
                    if (!string.IsNullOrWhiteSpace(envCred) && File.Exists(envCred))
                    {
                        credential = GoogleCredential.FromFile(envCred);
                    }
                }

                if (credential != null)
                {
                    var projectId = _config["Firebase:ProjectId"];
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = credential,
                        ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId
                    });
                    _firebaseAvailable = true;
                    _logger.LogInformation("Firebase Admin SDK (FCM HTTP v1) initialized successfully with Service Account.");
                }
                else
                {
                    _logger.LogInformation("No Firebase Service Account JSON found. Falling back to Legacy ServerKey or Mock mode.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Firebase Admin SDK. Falling back to Legacy ServerKey or Mock mode.");
            }
            finally
            {
                _firebaseInitialized = true;
            }
        }
    }

    /// <summary>
    /// แจ้งเตือนเมื่อคนขับได้รับมอบหมายงานใหม่
    /// </summary>
    public async Task SendJobAssignedNotificationAsync(
        long driverUserId,
        long jobId,
        string jobNumber,
        string jobTitle,
        string? pickupLocation = null,
        CancellationToken ct = default)
    {
        var title = "คุณได้รับมอบหมายงานใหม่ 🚚";
        var body = $"เลขที่งาน: {jobNumber} - {jobTitle}";
        if (!string.IsNullOrWhiteSpace(pickupLocation))
        {
            body += $" (จุดรับ: {pickupLocation})";
        }

        var payload = new
        {
            type = "JOB_ASSIGNED",
            jobId = jobId.ToString(),
            jobNumber = jobNumber ?? "",
            title = jobTitle ?? "",
            pickupLocation = pickupLocation ?? "",
            sound = "default",
            click_action = "FLUTTER_NOTIFICATION_CLICK"
        };

        await SendNotificationToUserAsync(driverUserId, title, body, payload, ct);
    }

    /// <summary>
    /// แจ้งเตือนเมื่อคนขับถูกยกเลิกงาน
    /// </summary>
    public async Task SendJobCancelledNotificationAsync(
        long driverUserId,
        long jobId,
        string jobNumber,
        string jobTitle,
        string? reason = null,
        CancellationToken ct = default)
    {
        var title = "งานขนส่งถูกยกเลิก ⚠️";
        var body = $"เลขที่งาน: {jobNumber} - {jobTitle}";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            body += $" (เหตุผล: {reason})";
        }

        var payload = new
        {
            type = "JOB_CANCELLED",
            jobId = jobId.ToString(),
            jobNumber = jobNumber ?? "",
            title = jobTitle ?? "",
            reason = reason ?? "",
            sound = "default",
            click_action = "FLUTTER_NOTIFICATION_CLICK"
        };

        await SendNotificationToUserAsync(driverUserId, title, body, payload, ct);
    }

    /// <summary>
    /// แจ้งเตือนเมื่อข้อมูลงานมีการเปลี่ยนแปลง
    /// </summary>
    public async Task SendJobUpdatedNotificationAsync(
        long driverUserId,
        long jobId,
        string jobNumber,
        string jobTitle,
        string? pickupLocation = null,
        string? changesSummary = null,
        CancellationToken ct = default)
    {
        var title = "ข้อมูลงานมีการเปลี่ยนแปลง ✏️";
        var body = $"เลขที่งาน: {jobNumber} - {jobTitle}";
        if (!string.IsNullOrWhiteSpace(pickupLocation))
        {
            var shortLoc = pickupLocation.Length > 60 ? pickupLocation.Substring(0, 57) + "..." : pickupLocation;
            body += $"\nจุดรับ: {shortLoc}";
        }
        if (!string.IsNullOrWhiteSpace(changesSummary))
        {
            var shortChanges = changesSummary.Length > 120 ? changesSummary.Substring(0, 117) + "..." : changesSummary;
            body += $"\nแก้ไข: {shortChanges}";
        }

        var payload = new
        {
            type = "JOB_UPDATED",
            jobId = jobId.ToString(),
            jobNumber = jobNumber ?? "",
            title = jobTitle ?? "",
            pickupLocation = pickupLocation ?? "",
            sound = "default",
            click_action = "FLUTTER_NOTIFICATION_CLICK"
        };

        await SendNotificationToUserAsync(driverUserId, title, body, payload, ct);
    }

    /// <summary>
    /// ส่งการแจ้งเตือนโดยตรงไปยังผู้ใช้ (ค้นหา FCM Token และบันทึก Outbox)
    /// </summary>
    public async Task<bool> SendNotificationToUserAsync(
        long targetUserId,
        string title,
        string body,
        object? payloadData = null,
        CancellationToken ct = default)
    {
        var payloadJson = payloadData != null ? JsonSerializer.Serialize(payloadData) : "{}";

        try
        {
            using var conn = _db.CreateConnection();

            // Resolve actual users(id) in case targetUserId is a user_profiles.id
            var resolvedUserId = await conn.QueryFirstOrDefaultAsync<long?>(
                new CommandDefinition(@"
                    SELECT id FROM users WHERE id = @targetUserId AND deleted_at IS NULL
                    UNION
                    SELECT user_id FROM user_profiles WHERE id = @targetUserId AND deleted_at IS NULL
                    LIMIT 1;", new { targetUserId }, cancellationToken: ct)) ?? targetUserId;

            // 1. Get active FCM tokens for this user from user_devices table
            const string getTokensSql = @"
                SELECT DISTINCT ud.fcm_token 
                FROM user_devices ud 
                WHERE ud.user_id = @resolvedUserId 
                  AND ud.is_active = TRUE 
                  AND ud.deleted_at IS NULL 
                  AND ud.fcm_token IS NOT NULL 
                  AND TRIM(ud.fcm_token) != '';";

            var tokens = (await conn.QueryAsync<string>(new CommandDefinition(getTokensSql, new { resolvedUserId }, cancellationToken: ct))).ToList();

            bool hasSent = false;

            if (tokens.Count > 0)
            {
                foreach (var token in tokens)
                {
                    var success = await SendFcmPushAsync(token, title, body, payloadData ?? new { }, ct);
                    if (success)
                    {
                        hasSent = true;
                    }
                }
            }
            else
            {
                _logger.LogInformation("No active FCM tokens found in user_devices for user ID {UserId} (Resolved ID: {ResolvedUserId})", targetUserId, resolvedUserId);
                // Mark processed so outbox stores the notification history for in-app viewing
                hasSent = true;
            }

            // 2. Insert record into notification_outbox table
            const string outboxSql = @"
                INSERT INTO notification_outbox (user_id, title, body, payload_json, is_processed, processed_at, is_read, created_at)
                VALUES (@resolvedUserId, @title, @body, @payloadJson, @isProcessed, @processedAt, FALSE, CURRENT_TIMESTAMP);";

            await conn.ExecuteAsync(new CommandDefinition(outboxSql, new
            {
                resolvedUserId,
                title,
                body,
                payloadJson,
                isProcessed = hasSent,
                processedAt = hasSent ? DateTime.UtcNow : (DateTime?)null
            }, cancellationToken: ct));

            return hasSent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send or record notification for user #{TargetUserId}", targetUserId);
            return false;
        }
    }

    /// <summary>
    /// ส่ง FCM Push ไปยัง Token โดยตรง (รองรับ FCM HTTP v1, Legacy ServerKey, และ Mock Mode)
    /// </summary>
    public async Task<bool> SendFcmPushAsync(string fcmToken, string title, string body, object data, CancellationToken ct = default)
    {
        try
        {
            EnsureFirebaseInitialized();

            // 1. ส่งผ่าน FCM HTTP v1 (Service Account)
            if (_firebaseAvailable && FirebaseApp.DefaultInstance != null)
            {
                var dataDict = new Dictionary<string, string>();
                if (data != null)
                {
                    var json = JsonSerializer.Serialize(data);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name.Equals("notification", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var val = prop.Value.ValueKind == JsonValueKind.Object
                            ? prop.Value.GetRawText()
                            : (prop.Value.ToString() ?? "");

                        if (val.Length > 250) val = val.Substring(0, 247) + "...";
                        dataDict[prop.Name] = val;
                    }
                }

                // Truncate banner text to stay well under FCM 4KB limit
                var safeTitle = title.Length > 80 ? title.Substring(0, 77) + "..." : title;
                var safeBody = body.Length > 200 ? body.Substring(0, 197) + "..." : body;

                if (!dataDict.ContainsKey("title")) dataDict["title"] = safeTitle;
                if (!dataDict.ContainsKey("body")) dataDict["body"] = safeBody;
                if (!dataDict.ContainsKey("sound")) dataDict["sound"] = "default";
                if (!dataDict.ContainsKey("channel_id")) dataDict["channel_id"] = "high_importance_channel";
                if (!dataDict.ContainsKey("click_action")) dataDict["click_action"] = "FLUTTER_NOTIFICATION_CLICK";

                var message = new Message
                {
                    Token = fcmToken,
                    Notification = new Notification
                    {
                        Title = safeTitle,
                        Body = safeBody
                    },
                    Data = dataDict,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            Sound = "default",
                            ChannelId = "high_importance_channel",
                            ClickAction = "FLUTTER_NOTIFICATION_CLICK",
                            DefaultSound = true,
                            DefaultVibrateTimings = true
                        }
                    },
                    Apns = new ApnsConfig
                    {
                        Headers = new Dictionary<string, string>
                        {
                            { "apns-priority", "10" }
                        },
                        Aps = new Aps
                        {
                            Sound = "default",
                            Badge = 1,
                            ContentAvailable = true
                        }
                    }
                };

                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
                _logger.LogInformation("Firebase FCM HTTP v1 push sent successfully to token {Token}... Message ID: {MessageId}", 
                    fcmToken.Substring(0, Math.Min(10, fcmToken.Length)), response);
                return true;
            }

            // 2. ส่งผ่าน Legacy FCM ServerKey (Fallback)
            var serverKey = _config["Firebase:ServerKey"];
            if (!string.IsNullOrWhiteSpace(serverKey))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
                request.Headers.TryAddWithoutValidation("Authorization", $"key={serverKey}");

                var legacyPayload = new
                {
                    to = fcmToken,
                    priority = "high",
                    notification = new
                    {
                        title = title,
                        body = body,
                        sound = "default",
                        channel_id = "high_importance_channel",
                        click_action = "FLUTTER_NOTIFICATION_CLICK"
                    },
                    data = data
                };

                request.Content = new StringContent(
                    JsonSerializer.Serialize(legacyPayload),
                    Encoding.UTF8,
                    "application/json");

                var res = await _httpClient.SendAsync(request, ct);
                if (res.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Firebase Legacy Push Notification sent successfully to token {Token}", fcmToken.Substring(0, Math.Min(10, fcmToken.Length)) + "...");
                    return true;
                }
                else
                {
                    var err = await res.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Firebase push send failed: {StatusCode} - {Error}", res.StatusCode, err);
                }
            }
            else
            {
                _logger.LogInformation("[Firebase Push Mock] Notification dispatched to token {Token}: Title='{Title}', Body='{Body}', Data={Data}", 
                    fcmToken.Substring(0, Math.Min(10, fcmToken.Length)) + "...", title, body, JsonSerializer.Serialize(data));
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send FCM push notification to token {Token}", fcmToken);
        }

        return false;
    }
}
