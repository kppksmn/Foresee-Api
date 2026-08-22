# Foresee Logix Backend API Update Summary

## 📌 รายการแก้ไขใน Foresee-Api

### 1. ถอด `nameEn` / `name_en` ออกจากระบบ 100%
- **Entities & DTOs**:
  - `src/Core/Entities/Entities.cs`: ลบ `NameEn` จาก `Menu` entity
  - `src/Core/DTOs/Dtos.cs`: ลบ `NameEn` จาก `MenuManagementUpsertMenuRequest`, `MenuManagementMenuResponse`, `UserNavMenuDto`, ฯลฯ
- **Repositories**:
  - `src/Infrastructure/Repositories/Repositories.cs`: ลบ `name_en` / `NameEn` จาก SQL Queries ทั้งหมด (SELECT, INSERT, UPDATE)
- **Endpoints**:
  - `src/API/APIs/v1/Admin/AdminEndpoints.cs`: ถอด `NameEn` ออกจากการตรวจสอบและการทำ Audit Log

### 2. ลบเมนูแผนที่ติดตาม (`/map`) & Seed Data
- `src/Infrastructure/Data/DbInitializer.cs`: ลบเมนู `แผนที่ติดตาม` (`/map`) ออกจาก SQL Seed Menu Data

### 3. ระบบป้องกันการลบเมนูหลักของระบบ (System Menu Protection)
- `src/API/APIs/v1/Admin/AdminEndpoints.cs`: เพิ่มการตรวจสอบป้องกันไม่ให้ลบเมนู `/menu-managements` และ `/menu-managements/permissions`
