🚢 AutoCAD .NET Plugin Template
Template chuẩn hóa dành cho việc phát triển các công cụ trên nền tảng AutoCAD .NET API. Bộ khung này được xây dựng dựa trên kiến trúc Module hóa và nguyên lý Clean Code, giúp bóc tách dữ liệu hình học (MTO) chính xác và nhanh chóng.

Tên chuẩn Projects:
Cấu trúc thư mục:
Autocad: MCGCadPlubin - Folder: C:\CustomTools\Autocad
Inventor: MCGInventorPlugin - - Folder: C:\CustomTools\Inventor

🏗️ Cấu trúc dự án (Architecture)
Dự án được phân chia thành các Module độc lập để dễ dàng bảo trì và mở rộng:

📁 Models: Chứa các lớp định nghĩa đối tượng (POCO). Tách biệt hoàn toàn với thư viện AutoCAD.

📁 Services: "Trái tim" của Plugin. Chứa ExtractionService thực hiện logic

📁 UI: Giao diện người dùng dựa trên WPF tích hợp vào AutoCAD PaletteSet.

📁 Utilities: Các hàm tiện ích dùng chung (Tính COG, đổi đơn vị, định dạng tọa độ WCS).

📁 Commands: Nơi đăng ký lệnh (CommandMethod) và quản lý vòng đời của Palette (Singleton).
Sẽ sử dụng tiền tố: MCG_Lệnh (MCG_AdvanceBoundary)

Test quá trình checkin = Commit (luôn phải có comments xem là nội dung thay đổi là gì)
Quá trình checkout = Pull (kéo dự án từ Github về local)

B1: Trước khi làm thì luôn PULL về để đồng bộ hóa với hệ thống
B2: Khi kết thúc phiên làm việc thì cần Commit & Sync để đồng bộ lên hệ thống


