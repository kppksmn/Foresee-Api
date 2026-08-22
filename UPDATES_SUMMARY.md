# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. แก้ไขสิทธิ์ API ดึงรายการงาน `GET /api/v1/admin/jobs?mode=active` สำหรับผู้ใช้เมนูงานของฉัน
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - **อนุญาตสิทธิ์เมนู "งานของฉัน" (`/my-jobs`) ใน `GET /jobs`**:
    แก้ไขตรรกะใน `GET /api/v1/admin/jobs`: หากผู้ใช้งานมีสิทธิ์ในเมนู `/my-jobs` และเรียกดูรายการงานสถานะ Active (`mode != "history"`) ระบบจะอนุญาตให้เข้าถึงข้อมูลรายการงานได้
  - ปัญหาก่อนแก้ไข: เมื่อพนักงานที่มีเฉพาะสิทธิ์เมนู `/my-jobs` เปิดหน้าดูรายละเอียดงาน แล้วหน้าจอพยายามดึงข้อมูลรายการงาน Active ระบบตอบกลับด้วย `403 Forbidden`
  - หลังการแก้ไข: ปราศจากข้อผิดพลาด `403 Forbidden` และส่งคืนข้อมูลให้หน้าจอได้อย่างราบรื่น

### 2. แก้ไขสิทธิ์ API ดึงรายละเอียดงานตาม ID (`GET /api/v1/admin/jobs/{id}`)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - เพิ่มการเช็กสิทธิ์ `HasMenuPermissionAsync` สำหรับเมนู `/my-jobs` และเช็กว่าผู้ใช้เป็นคนขับ (`driver_id`) หรือผู้ร่วมเดินทาง (`companion_id`) ในงานนั้นๆ

### 3. เพิ่ม API ดึงรายการ "งานของฉัน" (`GET /api/v1/auth/me/jobs`)
- `src/API/APIs/v1/Auth/AuthEndpoints.cs`:
  - เพิ่ม Endpoint `GET /api/v1/auth/me/jobs` สำหรับดึงรายการงานขนส่งเฉพาะของผู้ใช้งานที่ล็อกอินอยู่

### 4. เพิ่มการตรวจสอบสิทธิ์ในประเภทรถ (Vehicle Types Permission Enforcements)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - ตรวจสอบสิทธิ์ `HasMenuPermissionAsync` ใน `POST`, `PUT`, `DELETE` สำหรับ `/vehicle-types`

### 5. รองรับการกรองรายการงานด้วยวันที่นัดหมาย (Scheduled Date Filter)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - เพิ่มพารามิเตอร์ `date` (`[FromQuery] string? date`) ใน `GET /jobs`

### 6. บล็อกการมอบหมายงานให้คนขับที่ใบขับขี่หมดอายุ (License Expiration Assignment Blocking)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - บล็อกการมอบหมายงานหากคนขับหรือผู้ติดตามใบขับขี่หมดอายุ (`LicenseExpirationDate < DateTime.UtcNow.Date`)
