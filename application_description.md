# Release version Tool

**Release version Tool** là một công cụ máy để bàn chuyên dụng (Desktop Application) được xây dựng trên nền tảng **.NET 10.0 (Windows Forms)**. Công cụ này được thiết kế để hỗ trợ các kỹ sư phát triển phần mềm (Developers), kỹ sư vận hành (DevOps/SRE) và quản trị viên cơ sở dữ liệu (DBAs) trong việc chuẩn bị, so sánh, tối ưu hóa và đồng bộ hóa cấu trúc (schema) cũng như dữ liệu giữa các môi trường cơ sở dữ liệu PostgreSQL (như từ môi trường Phát triển - Dev lên môi trường Sản xuất - Prod) một cách an toàn và tự động.

---

## 🚀 Các Tính Năng Chính

### 1. So Sánh và Đồng Bộ Cấu Trúc (Compare Schema)
*   **So sánh sâu (Deep Comparison)**: Phát hiện sự khác biệt giữa các đối tượng cơ sở dữ liệu bao gồm: Bảng (Tables), Cột (Columns), Khóa ngoại/Khóa chính (Constraints), Chỉ mục (Indexes), Trigger, Hàm/Thủ tục (Routines), View, Materialized View, Sequence, Enum và Composite Types.
*   **Tự động sinh script DDL**: Tự động tạo mã SQL để cập nhật cấu trúc cơ sở dữ liệu từ phiên bản cũ lên phiên bản mới một cách đồng bộ.
*   **Hỗ trợ Tuning Script**: Tùy chọn sinh mã DDL an toàn (sử dụng `IF NOT EXISTS`, `DROP IF EXISTS`, v.v.) để tránh gây lỗi khi thực thi lại nhiều lần.

### 2. So Sánh và Đồng Bộ Dữ Liệu (Compare Data)
*   **Phân tích sự khác biệt dữ liệu**: So sánh từng dòng dữ liệu giữa hai database dựa trên khóa chính hoặc **khóa chính ghép (Composite Primary Key)**.
*   **Quản lý thay đổi trực quan**: Phân loại rõ ràng số lượng bản ghi thêm mới (`INSERT`), cập nhật (`UPDATE`), hoặc bị xóa (`DELETE`).
*   **Tự động sinh script DML**: Hỗ trợ sinh script đồng bộ dữ liệu thông thường hoặc sử dụng cú pháp **`UPSERT` (`ON CONFLICT DO UPDATE`)**.
*   **Tính năng an toàn**: Hỗ trợ chạy thử nghiệm (**Dry Run**) tự động Rollback giao dịch nếu có lỗi phát sinh.

### 3. Phân Tích và Dọn Dẹp Dữ Liệu Rác (Clean Junk)
*   **Quét thông minh**: Tự động phân tích các bảng và cột chứa dữ liệu thừa, bảng tạm (`temp_*`), hoặc các cột không được sử dụng để tối ưu hóa dung lượng lưu trữ.
*   **Sinh script dọn dẹp**: Tự động đề xuất các câu lệnh `DROP` hoặc `TRUNCATE` an toàn.

### 4. Tích Hợp AI Đánh Giá (AI Review)
*   **Đánh giá cấu trúc**: Sử dụng các mô hình ngôn ngữ lớn (LLMs như Gemini, OpenAI, Claude) để kiểm tra thiết kế database, phát hiện các lỗi thiết kế, thiếu chỉ mục cần thiết hoặc các vấn đề về chuẩn hóa dữ liệu.
*   **Đánh giá cấu hình**: Đưa ra các khuyến nghị tối ưu cấu hình PostgreSQL (`postgresql.conf`) dựa trên tài nguyên hệ thống.

---

## 🛠️ Công Nghệ Sử Dụng
*   **Ngôn ngữ**: C# 13 / .NET 10.0 Windows Forms.
*   **Thư viện kết nối**: `Npgsql` (PostgreSQL Client cho .NET).
*   **So sánh văn bản**: `DiffPlex` (Sử dụng để hiển thị trực quan các dòng DDL khác biệt giữa Source và Target).
*   **Kiểm thử tự động**: `NUnit` kết hợp với `FlaUI` cho việc kiểm thử giao diện tự động (UI Automation) và các ca kiểm thử tích hợp (Integration Tests) với database thật.

---

## 📖 Hướng Dẫn Sử Dụng Nhanh
1.  **Cấu hình kết nối**: Tại tab **1. Global Setup**, chọn kết nối tới Database Source (Dev) và Database Target (Prod). Nhấn **Initialize Release Session**.
2.  **So sánh cấu trúc**: Sang tab **3. Compare Schema**, chọn schema cần so sánh và nhấn **Load**. Chọn các đối tượng khác biệt để xem DDL chi tiết và sinh script đồng bộ.
3.  **So sánh dữ liệu**: Sang tab **4. Compare Data**, chọn bảng cần so sánh dữ liệu, thiết lập cột bỏ qua hoặc điều kiện lọc (nếu có) và bấm **Compare**. Nhấp **Generate Script** để tạo mã SQL đồng bộ hóa dữ liệu.
4.  **Thực thi đồng bộ**: Sang tab **5. Execute Sync**, dán mã SQL đã sinh và chọn chế độ chạy (có thể bật **Dry Run** để chạy thử nghiệm rollback trước).
