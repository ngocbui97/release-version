# Hướng dẫn sử dụng Release Preparation Tool

**Release Preparation Tool** là ứng dụng tự động hóa các khâu chuẩn bị release version mới cho CSDL PostgreSQL và thư mục mã nguồn (đặc biệt là các file cấu hình backend appsettings.json, file env).

## Cấu trúc thư mục đầu ra
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
│   ├── note.txt                    # File note về các thông tin thay đổi (.env, appsettings.json)
```

---

## Các Bước Sử Dụng Chuẩn (Workflow 8-Tab)

Vui lòng tuân thủ quy trình đi từ trái sang phải trên giao diện Tabs của công cụ để áp dụng chuẩn mực Release Management.

### Bước 1: Khởi tạo thông tin (Tab 1. Global Setup)
- Điền các thông tin Server (Host, Port, User, Password) kết nối Database.
- Xác định `Old Database Name` (DB mô phỏng bản hiện hành của người dùng) và `New Database Name` (Data release chuẩn bị).
- Nhập **Product Name** và **Release Version** để hình thành cấu trúc folder.
- *Lưu ý: Ứng dụng tự động Ghi nhớ (Lưu trữ) cấu hình của tab này. Lần sau mở máy chạy Tool, các ô sẽ hiển thị lại thông tin cũ.*
- Nhấn **Initialize Services** để móc nối các hệ thống dịch vụ và khóa luồng bảo vệ.

### Bước 2: Chuẩn bị Môi trường Data DB (Tab 2. Restore Databases)
*(Cả 2 database phiên bản cũ và mới sẽ được setup giả định trên 1 local server cho tiện thao tác)*
- Bấm **Restore Old DB from File**: Chọn 1 file `.backup` hoặc `.sql` mẫu của version hiện tại để nạp CSDL lên.
- Bấm **Restore New DB from File**: Chạy restore lên New Database bằng bản backup release do Dev cung cấp.
*(Tính năng này hoàn toàn có thể chạy đi chạy lại n-lần nếu bước 4 Execute bị hỏng cần reset Data).*

### Bước 3: Phân rã Khác biệt CSDL (Tab 3. Compare Database)
Quá trình tách dữ liệu Schema và Data làm 2 luồng độc lập:
1. Bạn hãy bấm **Load Common Tables** để hệ thống liệt kê các Tables chung mặt trên Database.
2. Bấm **Generate Schema Script**: Phần mềm tự động dò sự thay đổi ở định nghĩa Cột, Kiểu dữ liệu, Bảng (ALTER, CREATE, DROP). Lệnh sẽ xuất file vào `/schema/`.
3. Bấm **Generate Data Script**: Hãy tích chọn cụ thể các bảng chứa "Dữ liệu cấu hình" hoặc "Danh mục cần update cứng", ứng dụng tự tạo lệnh INSERT/UPDATE/DELETE. Lệnh xuất vào `/data/`.

### Bước 4: Đồng bộ CSDL Thử nghiệm (Tab 4. Execute Sync)
- Môi trường thử nghiệm đồng bộ là *Old Database*. Tool sẽ mô phỏng quá trình update bằng cách chạy lần lượt các file `{databasename}_schema/data.sql` vừa sinh ra lên Old Database.
- Bấm **Execute Schema Sync** và **Execute Data Sync**. Code hoạt động trên nền móng ADO.NET Transaction -> Bất kỳ lệnh SQL lỗi nào cũng sẽ được ROLLBACK lại không để rác.
- **Nếu lỗi SQL**: Quay lại Tab 2 Restore Old DB -> Sửa file Script -> Quay lại Tab 4 Run tiếp. Vòng lặp liên tục cho đến khi Pass toàn bộ Script.
- Bấm **Verify Sync Status**: Để ứng dụng tự Double-Check xem Old DB đã giống New DB 100% về cả Data được tick và Data schema chưa.

### Bước 5: Kiểm duyệt Dữ liệu Rác (Tab 5. Clean Junk)
- Ở mục Database vừa sync có thể vương vãi các data dummy (data test, log lỗi dev sinh ra, keyword lạ).
- Gõ các từ khóa (như `test`, `dev`, `http://localhost`) → Nhấn **Scan Junk Data**.
- Review grid trên màn hình, chọn các row không hợp lệ và nhấn **Delete Selected Rows** để xóa sạch tận gốc Primary Key.

### Bước 6: Đóng hộp CSDL Cuối (Tab 6. Export Final Release DB)
- Sau khi CSDL Old giả lập đã sync update trơn tru và quét sạch data rác.
- Bấm nút **Export Old DB (Backup + SQL)** để ứng dụng gói cục Old DB này thành 2 file định dạng `.backup` vật lý và `_full.sql` đẩy vào ngay các thư mục `database/backup` và `database/script_full`. Đây sẽ là Asset Delivery vứt cho khách hàng, hoặc lên Cloud.

### Bước 7: Phân tích Source code Config (Tab 7. Compare Config)
- Trỏ đường dẫn đến file `.env` hoặc `appsettings.json` của code bản Cũ và Mới để clone.
- Bấm **Compare & Generate Note**: Tool đọ nội dung và log vào `note.txt` ở file gốc (`/source_code/note.txt`). Kỹ sư DevOps thao tác theo file note này để cập nhật Cloud config.
- Đồng thời Tool tự Clean/làm rỗng các biến Data môi trường nhạy cảm lộ Token, Mật khẩu trên file config clone mới.

### Bước 8: Trí tuệ Nhân tạo (Tab 8. AI Review)
- Tính năng AI Check tự chấm điểm các truy vấn SQL hoặc chênh lệch JSON. Bấm Test Review để đảm bảo Tool Update không chứa các lệnh độc hại `DROP DATABASE` hoặc Drop Credential Column, hoặc file config không dính Hard-code domain ngớ ngẩn (Cần nhập AI Key).
