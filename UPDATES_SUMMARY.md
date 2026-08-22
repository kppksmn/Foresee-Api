# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. เพิ่มการรองรับการกรองช่วงวันที่ (Date Range Filter: startDate & endDate)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs` & `src/API/APIs/v1/Auth/AuthEndpoints.cs`:
  - ใน `GET /api/v1/admin/jobs` และ `GET /api/v1/auth/me/jobs` เพิ่มพารามิเตอร์ `[FromQuery] string? startDate` และ `[FromQuery] string? endDate`
  - เพิ่มเงื่อนไข SQL:
    ```sql
    AND (@startDate IS NULL OR @startDate = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') >= @startDate)
    AND (@endDate IS NULL OR @endDate = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') <= @endDate)
    ```
  - รองรับการกรองข้อมูลช่วงวันที่ได้อย่างแม่นยำและรวดเร็ว

### 2. แก้ไขสิทธิ์ API ดึงรายการงาน `GET /api/v1/admin/jobs?mode=active` สำหรับผู้ใช้เมนูงานของฉัน
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - อนุญาตให้ผู้ใช้ที่มีสิทธิ์ในเมนู `/my-jobs` สามารถดึงข้อมูลงาน Active ได้โดยไม่โดนบล็อกด้วย 403 Forbidden

### 3. แก้ไขสิทธิ์ API ดึงรายละเอียดงานตาม ID (`GET /api/v1/admin/jobs/{id}`)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - อนุญาตสิทธิ์อ่านสำหรับเมนู `/my-jobs` และตรวจสอบการเป็นพนักงานที่ได้รับมอบหมาย (`driver_id` หรือ `companion_id`)

### 4. เพิ่ม API ดึงรายการ "งานของฉัน" (`GET /api/v1/auth/me/jobs`)
- `src/API/APIs/v1/Auth/AuthEndpoints.cs`:
  - เพิ่ม Endpoint `GET /api/v1/auth/me/jobs` พร้อมรองรับ Date Range Filtering
