# Hướng dẫn sử dụng Release Preparation Tool

Tài liệu này hướng dẫn cách sử dụng công cụ **Release Preparation Tool** để tự động hóa quá trình so sánh và đồng bộ hóa cơ sở dữ liệu giữa môi trường Phát triển (New/Dev) và Sản xuất (Old/Prod).

## 1. Thiết lập chung (Global Setup)
Tab đầu tiên dùng để cấu hình kết nối và các thông số cơ bản:
- **Source Connection (New/Dev)**: Chọn kết nối đến cơ sở dữ liệu nguồn (thường là bản cập nhật mới).
- **Target Connection (Old/Prod)**: Chọn kết nối đến cơ sở dữ liệu đích (thường là bản cần được nâng cấp).
- **General Settings**: 
    - Nhập đường dẫn thư mục `bin` của PostgreSQL (nếu cần dùng `pg_dump`/`pg_restore`).
    - Nhập tên sản phẩm, phiên bản và đường dẫn xuất báo cáo.
- Nhấn **Initialize Services** để bắt đầu.

## 2. Khôi phục cơ sở dữ liệu (Restore Databases)
Nếu bạn có file backup (.backup hoặc .sql):
- Chọn **Restore Old DB from File** để đưa dữ liệu vào database đích trước khi so sánh.
- Theo dõi quá trình qua khung Log bên dưới.

## 3. So sánh cấu trúc (Compare Schema)
Dùng để tìm sự khác biệt về bảng, view, function, v.v.
- Chọn Database nguồn và đích từ danh sách thả xuống. Nhấn **Refresh Databases** nếu không thấy.
- Nhấn **Load Diffs** để quét sự khác biệt.
- Các đối tượng sẽ hiện trong cây (TreeView):
    - **[NEW] (Màu xanh)**: Chỉ có ở nguồn.
    - **[REMOVED] (Màu đỏ)**: Chỉ có ở đích.
    - **[DIFF] (Màu xanh dương)**: Có ở cả hai nhưng khác nội dung.
- Chọn từng mục để xem so sánh mã DDL (Source vs Target).
- Nhấn **Export Schema Script** để tạo file SQL đồng bộ cấu trúc.

## 4. So sánh dữ liệu (Compare Data)
Dùng để đồng bộ dữ liệu giữa các bảng có cùng tên.
- Chọn Database nguồn và đích. 
- **Tự động tải**: Danh sách tất cả các bảng sẽ tự động hiện ra ngay sau khi bạn chọn xong cả hai database.
- **Chọn bảng**: Sử dụng các nút **Select All** hoặc **Select None** để nhanh chóng chọn các bảng cần so sánh dữ liệu. Chỉ các bảng tồn tại ở cả hai bên (Common) mới có thể chọn.
- Nhấn **Start Comparison** để bắt đầu quá trình so sánh chi tiết từng dòng dữ liệu cho các bảng đã chọn.
    - **Different**: Số dòng bị cập nhật.
    - **Only in Source**: Số dòng mới cần thêm vào.
    - **Only in Target**: Số dòng cũ cần xóa đi.
- **Mẹo**: Nháy đúp (double-click) vào một dòng để xem chi tiết từng bản ghi khác biệt trong cửa sổ mới.
- Nhấn **Export Sync Script** để tạo file SQL đồng bộ dữ liệu.

## 5. Thực thi đồng bộ (Execute Sync)
- **Execute Schema Sync**: Chạy script cấu trúc đã tạo lên database đích.
- **Execute Data Sync**: Chạy script dữ liệu đã tạo.
- **Verify Sync Status**: Kiểm tra lại xem sau khi chạy, hai database đã khớp nhau chưa.

## 6. Các tính năng khác
- **6. Clean Junk**: Quét và xóa các dữ liệu rác (ví dụ dữ liệu test) dựa trên từ khóa.
- **7. Export Final**: Sao lưu cơ sở dữ liệu đích sau khi đã đồng bộ hoàn tất.
- **8. Compare Config**: So sánh các file cấu hình (.json, .env) và tạo ghi chú thay đổi.
- **9. AI Review**: Sử dụng AI để rà soát lại các script SQL hoặc cấu hình (yêu cầu cấu hình AI Key ở Tab 1).

> [!IMPORTANT]  
> Luôn thực hiện sao lưu (Backup) cơ sở dữ liệu đích trước khi thực hiện các lệnh đồng bộ (Sync) để tránh mất mát dữ liệu không mong muốn.
