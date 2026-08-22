# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. ปรับมาตรฐานสถานะงานขนส่งทั้งหมดเป็น 5 สถานะหลัก
- `src/Infrastructure/Data/DbInitializer.cs`:
  - ปรับข้อมูล Seed Data สำหรับงานขนส่งทั้งหมดให้อยู่ใน 5 สถานะหลักตามข้อกำหนด:
    1. `Pending` (รอมอบหมาย)
    2. `Assigned` (มอบหมายแล้ว)
    3. `Started` (เริ่มงานแล้ว)
    4. `Completed` (เสร็จสิ้น)
    5. `Cancelled` (ยกเลิก)
- `src/API/APIs/v1/Mobile/MobileEndpoints.cs`:
  - ปรับปรุง Endpoint การปิดงาน (`/complete`) ให้รองรับการสลับสถานะจาก `Started` หรือ `Assigned` ตรงไปยัง `Completed` (เสร็จสิ้น) ได้ทันที

### 2. เพิ่มการรองรับการกรองช่วงวันที่ (Date Range Filter: startDate & endDate)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs` & `src/API/APIs/v1/Auth/AuthEndpoints.cs`:
  - รองรับพารามิเตอร์ `startDate` และ `endDate` ใน `GET /jobs` และ `GET /me/jobs`

### 3. แก้ไขสิทธิ์ API ดึงรายการงาน `GET /api/v1/admin/jobs?mode=active` สำหรับผู้ใช้เมนูงานของฉัน
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`: อนุญาตให้ผู้ใช้ที่มีสิทธิ์ `/my-jobs` สามารถดึงข้อมูลงาน Active ได้

### 4. แก้ไขสิทธิ์ API ดึงรายละเอียดงานตาม ID (`GET /api/v1/admin/jobs/{id}`)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`: อนุญาตสิทธิ์อ่านสำหรับ `/my-jobs` และตรวจสอบพนักงานที่ได้รับมอบหมาย
