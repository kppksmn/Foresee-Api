# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. บล็อกการมอบหมายงานให้คนขับที่ใบขับขี่หมดอายุ (License Expiration Assignment Blocking)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`:
  - ใน `POST /jobs` (สร้างงาน), `POST /jobs/{id}/assign` (มอบหมายงาน), และ `PUT /jobs/{id}` (แก้ไขงาน) เพิ่มการตรวจสอบวันหมดอายุใบขับขี่
  - หาก `LicenseExpirationDate < DateTime.UtcNow.Date` ระบบจะไม่อนุญาตให้มอบหมายงาน และจะคืนค่า `400 Bad Request` พร้อมข้อความเตือน

### 2. ถอด `nameEn` / `name_en` ออกจากระบบ 100%
- `src/Core/Entities/Entities.cs`: ลบ `NameEn` จาก `Menu` entity
- `src/Core/DTOs/Dtos.cs`: ลบ `NameEn` จาก Request/Response DTOs ทั้งหมด
- `src/Infrastructure/Repositories/Repositories.cs`: ลบ `name_en` ออกจาก SQL Queries
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`: ถอด `NameEn` ออกจากระบบ

### 3. ลบเมนูแผนที่ติดตาม (`/map`) & Seed Data
- `src/Infrastructure/Data/DbInitializer.cs`: ลบเมนู `แผนที่ติดตาม` (`/map`) ออกจาก SQL Seed Menu Data

### 4. ระบบป้องกันการลบเมนูหลักของระบบ (System Menu Protection)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`: เพิ่มการตรวจสอบป้องกันไม่ให้ลบเมนู `/menu-managements` และ `/menu-managements/permissions`
