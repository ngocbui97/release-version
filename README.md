# Hướng dẫn sử dụng Release Preparation Tool

**Release Preparation Tool** là ứng dụng tự động hóa các khâu chuẩn bị release version mới cho CSDL PostgreSQL và thư mục mã nguồn (đặc biệt là các file cấu hình backend appsettings.json, file env).

---

## 📂 Cấu trúc thư mục đầu ra
Khi bạn thao tác, hệ thống sẽ tự động tạo cấu trúc thư mục chuẩn tại Output Path để dễ dàng lưu trữ và package bàn giao:
```text
/[product]_version_[2.0]/
├── 📂 database/                    # Quản lý toàn bộ tài nguyên DB
│   ├── 📂 backup/                  # File sao lưu vật lý (.backup)
│   │   └── {databasename}.backup
│   ├── 📂 script_full/             # Script cài đặt mới (Schema + Data)
│   │   └── {databasename}_full.sql
│   └── 📂 script_update/           # Script nâng cấp từ version cũ
│       ├── 📂 schema/
│       │   └── {databasename}_schema.sql
│       └── 📂 data/
│           └── {databasename}_data.sql
├── 📂 source_code/                 # Mã nguồn ứng dụng (Build artifacts)
│   └── note.txt                    # File note về các thông tin thay đổi (.env, appsettings.json)
```

---

## ⚙️ Quy trình xử lý chuẩn (Workflow 10-Tab)

Vui lòng tuân thủ quy trình đi từ trái sang phải trên giao diện các Tabs của công cụ để áp dụng chuẩn mực Release Management:

### 1. Khởi tạo thông tin (Tab 1. Global Setup)
*   Thiết lập thông tin máy chủ PostgreSQL (Host, Port, Username, Password) cho môi trường Source (Dev) và Target (Prod).
*   Nhập tên cơ sở dữ liệu nguồn (`Source/New DB`) và đích (`Target/Old DB`).
*   Nhập **Product Name** và **Release Version** để thiết lập cấu trúc thư mục đầu ra.
*   Nhấn **Initialize Release Session** để kiểm tra kết nối và khởi tạo phiên làm việc.
*   *(Giao diện tự động ghi nhớ cấu hình để thuận tiện sử dụng cho lần tiếp theo)*.

### 2. Thiết lập Môi trường CSDL Thử nghiệm (Tab 2. Restore Databases)
*   Mô phỏng cả hai phiên bản CSDL cũ và mới trên máy chủ thử nghiệm.
*   **Restore Target (Prod)**: Chọn file `.backup` hoặc `.sql` hiện tại của khách hàng để tạo môi trường CSDL phiên bản cũ.
*   **Restore Source (Dev)**: Chọn file backup phiên bản release mới do đội phát triển cung cấp.
*   Hỗ trợ chạy các file SQL bổ sung sau khi restore hoàn tất.

### 3. So Sánh và Sinh Schema Script (Tab 3. Compare Schema)
*   Chọn cơ sở dữ liệu Source và Target cùng schema tương ứng để so sánh cấu trúc.
*   Bấm **Load** để so sánh sự khác biệt (Bảng, Cột, Khóa chính/Khóa ngoại, Index, Trigger, View, Function, Sequence...).
*   Bật/tắt tùy chọn **Tuning Script (Safe Run)** để tự động chèn `IF NOT EXISTS` hoặc `DROP IF EXISTS` vào script đầu ra.
*   Bấm **Generate Script** để xuất mã SQL nâng cấp cấu trúc cơ sở dữ liệu vào thư mục `database/script_update/schema/`.

### 4. So Sánh và Sinh Data Script (Tab 4. Compare Data)
*   So sánh dữ liệu dòng giữa các bảng của database Source và Target.
*   Hỗ trợ so sánh với **Khóa chính ghép (Composite Primary Key)**.
*   Cho phép thiết lập danh sách cột cần bỏ qua khi so sánh (ví dụ: `updated_at`) hoặc lọc theo điều kiện `WHERE`.
*   Bấm **Compare** để phân tích chênh lệch (`INSERT`, `UPDATE`, `DELETE`).
*   Bấm **Generate Script** để xuất file đồng bộ dữ liệu vào thư mục `database/script_update/data/` (hỗ trợ định dạng `UPSERT`).

### 5. Thực Thi Đồng Bộ Thử Nghiệm (Tab 5. Execute Sync)
*   Thử nghiệm chạy các script vừa được sinh ra lên database Target.
*   Bấm **Schema Sync** và **Data Sync** để chạy nâng cấp.
*   Sử dụng cơ chế Transaction an toàn: Có thể bật **Dry Run (Rollback)** để chạy thử nghiệm xem có lỗi cú pháp không mà không ảnh hưởng tới dữ liệu thật.
*   Bấm **Verify Sync Status** để phần mềm tự động so sánh lại, đảm bảo database Target sau khi đồng bộ đã khớp 100% cấu trúc và dữ liệu được chọn với database Source.

### 6. Quét và Dọn Dẹp Dữ Liệu Rác (Tab 6. Clean Junk)
*   Quét và phân tích các đối tượng rác trong database (bảng tạm, dữ liệu log dư thừa, keyword kiểm thử, domain local...).
*   Nhập danh sách từ khóa quét và chọn các bảng/schema đích, sau đó bấm **Analyze Junk**.
*   Xem kết quả phân tích trực quan dưới dạng cây (Structure) và bảng (Data). Chọn bản ghi rác và bấm **Purge Junk** để dọn dẹp triệt để.

### 7. Xuất Cơ Sở Dữ Liệu Release (Tab 7. Final Export)
*   Đóng gói CSDL đích sau khi đã được nâng cấp và dọn dẹp sạch sẽ.
*   Bấm **Export (Backup + Sql)** để xuất file sao lưu vật lý `.backup` và script đầy đủ dạng `_full.sql` vào thư mục `database/backup/` và `database/script_full/`. Đây là tài nguyên bàn giao cuối cùng.

### 8. So Sánh Tệp Cấu Hình (Tab 8. Config Compare)
*   So sánh sự khác biệt giữa các tệp cấu hình ứng dụng (`appsettings.json`, `.env`) của phiên bản cũ và mới.
*   Tự động phát hiện các thuộc tính bị thêm, xóa hoặc thay đổi giá trị.
*   Bấm **Analyze Configuration Differences** để xuất ghi chú thay đổi vào file `source_code/note.txt`. Tự động làm sạch các thông tin nhạy cảm (mật khẩu, token) trong tệp config clone mới.

### 9. Kiểm Duyệt Với Trí Tuệ Nhân Tạo (Tab 9. AI Review)
*   Sử dụng mô hình AI (Gemini, Claude, OpenAI) để kiểm duyệt an toàn.
*   **Review Schema Changes**: Kiểm tra xem script DDL có chứa lệnh nguy hiểm như `DROP COLUMN`, lỗi thiết kế cấu trúc hoặc thiếu Index không.
*   **Review Data Changes**: Phân tích rủi ro mất mát dữ liệu từ script đồng bộ dữ liệu.
*   **Audit Configuration Diff**: Đánh giá sự an toàn của thay đổi cấu hình (ngăn chặn hardcode thông tin nhạy cảm).

### 10. Tiện Ích Bổ Sung (Tab 10. Other)
*   Công cụ **SQL Script Converter** giúp bạn chuyển đổi nhanh các file SQL lớn.
*   Hỗ trợ tối ưu hóa an toàn (Safe-tuning syntax).
*   Hỗ trợ bộ lọc nhanh để loại bỏ các khai báo không tương thích khi di chuyển giữa các máy chủ PostgreSQL khác nhau (như loại bỏ Owner, Privilege, Tablespace, Comments, publications, subscriptions, v.v.).
