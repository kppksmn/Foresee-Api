# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. แก้ไขสิทธิ์ API ดึงรายละเอียดงานตาม ID (`GET /api/v1/admin/jobs/{id}`)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - **อนุญาตสิทธิ์เมนู "งานของฉัน" (`/my-jobs`)**: เพิ่มการเช็กสิทธิ์ `HasMenuPermissionAsync` สำหรับเมนู `/my-jobs` ใน `GET /jobs/{id:long}`
  - **อนุญาตสิทธิ์พนักงานผู้ได้รับมอบหมาย (Assigned Driver/Companion Check)**: หากผู้ใช้ไม่มีสิทธิ์ในเมนูส่วนกลาง แต่เป็นพนักงานขับรถ (`driver_id`) หรือผู้ร่วมเดินทาง (`companion_id`) ที่ได้รับมอบหมายในงานนั้นๆ ระบบจะอนุญาตให้ดึงข้อมูลรายละเอียดงานได้
  - ปัญหาก่อนแก้ไข: เมื่อพนักงานที่มีเฉพาะสิทธิ์เมนู "งานของฉัน" พยายามดึงรายละเอียดงาน Backend จะคืนค่า `403 Forbidden` ทำให้หน้าจอแสดงผลข้อมูลว่างเปล่า (ข้อมูลไม่ขึ้น)
  - หลังการแก้ไข: พนักงานสามารถดึงข้อมูลรายละเอียดงานในรูปแบบ JSON มาแสดงผลบนหน้าจอได้อย่างสมบูรณ์ 100%

### 2. เพิ่ม API ดึงรายการ "งานของฉัน" (`GET /api/v1/auth/me/jobs`)
- `src/API/APIs/v1/Auth/AuthEndpoints.cs`:
  - เพิ่ม Endpoint `GET /api/v1/auth/me/jobs` สำหรับดึงรายการงานขนส่งเฉพาะของผู้ใช้งานที่ล็อกอินอยู่ (จับคู่ `driver_id` หรือ `companion_id`)
  - รองรับพารามิเตอร์ `date` (`[FromQuery] string? date`) เพื่อกรองงานตามวันที่นัดหมายเฉพาะวัน
  - รองรับการค้นหาข้อความ (`search`) และสถานะงาน (`status`)
- `src/Infrastructure/Data/DbInitializer.cs`:
  - เพิ่มการ Seed เมนู `งานของฉัน` (`/my-jobs`) และกำหนดสิทธิ์การใช้งานให้อัตโนมัติ

### 3. เพิ่มการตรวจสอบสิทธิ์ในประเภทรถ (Vehicle Types Permission Enforcements)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - ใน `POST /vehicle-types`, `PUT /vehicle-types/{id}`, และ `DELETE /vehicle-types/{id}` เพิ่มการตรวจสอบสิทธิ์ `HasMenuPermissionAsync` สำหรับเมนู `/vehicle-types`
  - หากผู้ใช้งานมีเพียงสิทธิ์อ่าน (`read`) การพยายามส่งคำสั่งสร้าง แก้ไข หรือลบ ผ่าน API จะถูกบล็อกด้วย `403 Forbidden (Permission Denied)` ทันที

### 4. รองรับการกรองรายการงานด้วยวันที่นัดหมาย (Scheduled Date Filter)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - ใน `GET /jobs` เพิ่มพารามิเตอร์ `date` (`[FromQuery] string? date`)
  - เพิ่มเงื่อนไข SQL: `AND (@date IS NULL OR @date = '' OR TO_CHAR(j.scheduled_start_at AT TIME ZONE 'Asia/Bangkok', 'YYYY-MM-DD') = @date)`

### 5. บล็อกการมอบหมายงานให้คนขับที่ใบขับขี่หมดอายุ (License Expiration Assignment Blocking)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - ใน `POST /jobs` (สร้างงาน), `POST /jobs/{id}/assign` (มอบหมายงาน), และ `PUT /jobs/{id}` (แก้ไขงาน) เพิ่มการตรวจสอบวันหมดอายุใบขับขี่
  - หาก `LicenseExpirationDate < DateTime.UtcNow.Date` ระบบจะไม่อนุญาตให้มอบหมายงาน และจะคืนค่า `400 Bad Request` พร้อมข้อความเตือน
