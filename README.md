# Egyptian Aviation Academy (EAA) Training Management System (EAA-TMS)
### منظومة إدارة وتتبع عمليات التدريب الجوي – الأكاديمية المصرية لعلوم الطيران (وزارة الطيران المدني)

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-0078D6?logo=windows&logoColor=white)](https://github.com/michaelmagdy15/EAA-TrainingManager)
[![Framework](https://img.shields.io/badge/Framework-.NET%209%20%7C%20C%23%2013-512BD4?logo=dotnet&logoColor=white)](https://github.com/michaelmagdy15/EAA-TrainingManager)
[![UI Engine](https://img.shields.io/badge/UI-WinUI%203%20(Windows%20App%20SDK)-0078D4)](https://github.com/michaelmagdy15/EAA-TrainingManager)
[![Database](https://img.shields.io/badge/Database-SQLite%20(WAL%20Mode)-003B57?logo=sqlite&logoColor=white)](https://github.com/michaelmagdy15/EAA-TrainingManager)
[![Mode](https://img.shields.io/badge/Architecture-100%25%20Offline--First-success)](https://github.com/michaelmagdy15/EAA-TrainingManager)
[![Tests](https://img.shields.io/badge/Verification%20Tests-14%2F14%20Passing%20(100%25)-brightgreen)](https://github.com/michaelmagdy15/EAA-TrainingManager)

---

## 📖 Overview | نظرة عامة

**EAA-TMS** is a mission-critical, 100% offline-first Windows 11 desktop management system engineered specifically for the Flight Training Directorate of the **Egyptian Aviation Academy (الأكاديمية المصرية لعلوم الطيران)** under the Ministry of Civil Aviation.

It replaces legacy, error-prone multi-sheet Excel workbooks (`61 (ج نظام حر)`, `141 (ا نظام)`, `تقييم (د)`) with an enterprise-grade digital aviation terminal. The application provides two-way operational management, student identity deduplication, demographic intelligence, and automatic background data protection.

> **100% Offline Standalone Executable**: Operates in zero-internet flight line dispatch bunkers and airfield ops rooms at 6th of October Airport (HEOC). Zero external runtimes, zero cloud subscriptions, and zero installation wizards required.

---

## 🌟 Key Features | المميزات الرئيسية

### 1. Dynamic Smart Entry Form (`نافذة الإدخال الذكية المتغيرة`)
- **Live Identity Deduplication**: As you type a trainee's name, the system performs sub-millisecond Arabic fuzzy matching (`ArabicTextHelper`). If the student already exists (e.g., holds a PPL and is now enrolling in CPL/IR), it links the new order to their unified profile without duplicating human headcount.
- **Adaptive Stream Fields**: Selecting a training stream automatically morphs the form:
  - **Part 61 (نظام حر)**: Course picker (`PPL`, `CPL/IR`, `IR`, `CPL`), order #, enrollment & optional completion dates.
  - **Part 141 (دفعات نظامية)**: Program name, batch number (`رقم الدفعة`), and intake dates.
  - **Type Rating & Hours (تجديد طراز وبناء ساعات)**: Activity type, fleet presets (**C172, C172 Gas, B58, G36, Cessna Centurion, BE-76, PA-28/34**), and flight hours.
  - **Equivalency & Evaluation (التقييم والمعادلات)**: Foreign license conversion types (`ATP`, `PPL`, `IR/CPL`, level assessment).

### 2. One-Click Course Progression (`إتمام الكورس والتخريج`)
- Dedicated **"إتمام الكورس ✓"** action in both the main orders table and the student 360° trajectory timeline.
- Instantly logs official graduation dates and transitions active cadets to **"Graduated / خريج"**.

### 3. Automated Background Data Protection & Excel Mirroring
- **Real-Time Excel Mirror**: Every addition, edit, or status change asynchronously mirrors to `EAA_Master_Mirror.xlsx` on the user's Desktop and LocalAppData in seconds.
- **Hot SQLite Online Snapshots**: Automated, ACID-compliant database snapshots (`eaa_backup_YYYY-MM-DD_HHmm.db`) with 30-version rolling retention.
- **Anti-Deletion Safeguard**: Non-destructive soft deletes (`IsArchived = 1`) with an **Archive & Trash Bin** dialog featuring 1-click restoration.

### 4. International Cadets Demographics Engine
- Automatic standardization of foreign nationalities (Saudi, Emirati, Libyan, Sudanese, Jordanian, etc.).
- One-click export of the **Consular International Students Roster** formatted with official 4-line ministerial headers for civil aviation authorities and foreign cultural attachés.

### 5. Multi-Year Scoping & Instant RTL/LTR Switching
- Dedicated academic year selector (`2026`, `2025`, `2024`, or All Years).
- Instant live bilingual switching between **Right-to-Left (Arabic)** and **Left-to-Right (English)**.

---

## 🚀 Quick Start | التشغيل السريع

### Option A: Run the Standalone Single-File `.exe` (Recommended)
1. Download `EAATrainingManager.exe` from the latest [GitHub Release](https://github.com/michaelmagdy15/EAA-TrainingManager/releases/latest).
2. Double-click `EAATrainingManager.exe` to run.
3. The app starts immediately. Data is stored safely in `%LocalAppData%\EAA_TrainingManager\`.

### Option B: Build from Source
Ensure you have [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and Windows 10/11:

```bash
# Clone the repository
git clone https://github.com/michaelmagdy15/EAA-TrainingManager.git
cd EAA-TrainingManager

# Run the 14 Automated Verification Test Suites
dotnet run --project EAATrainingManager.Tests/EAATrainingManager.Tests.csproj

# Build the WinUI 3 Desktop Application
dotnet build EAATrainingManager/EAATrainingManager.csproj -c Release -p:Platform=x64

# Publish the Self-Contained Single-File Executable
dotnet publish EAATrainingManager/EAATrainingManager.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "./Publish"
```

---

## 🧪 Verification Test Suites (100% Pass)

The project includes an automated test harness covering all domain algorithms:

```text
[TEST SUITE 1]  ArabicTextHelper Normalization & BiDi Wrapping .......... PASSED ✔
[TEST SUITE 2]  Student Decoupling & Headcount vs Course Volume ......... PASSED ✔
[TEST SUITE 3]  Pipeline Automation & 360° Trajectory Engine ............ PASSED ✔
[TEST SUITE 4]  Excel Ingestion & Sheet Normalization .................. PASSED ✔
[TEST SUITE 5]  Official Ministry RTL Spreadsheet Generation ............ PASSED ✔
[TEST SUITE 6]  Demographics Engine & Foreign Nationalities ............. PASSED ✔
[TEST SUITE 7]  Multi-Year Scoping & 5 Training Streams ................. PASSED ✔
[TEST SUITE 8]  Consular International Students Roster Export .......... PASSED ✔
[TEST SUITE 9]  In-App Dynamic Manual Order Entry ....................... PASSED ✔
[TEST SUITE 10] Deduplication & Cross-Order Trainee Linking ............. PASSED ✔
[TEST SUITE 11] Course Progression & 1-Click Graduation ................ PASSED ✔
[TEST SUITE 12] Hot SQLite Online Snapshot Backup Engine ............... PASSED ✔
[TEST SUITE 13] Non-Destructive Soft Delete & 1-Click Restore ........... PASSED ✔
[TEST SUITE 14] Background Excel Mirroring Engine ....................... PASSED ✔

All 14 test suites completed with 100% success!
```

---

## 🔄 Updates & Maintenance

- **Delta Updates (2–5 MB)**: Click **"تحديثات / Updates"** in the app's title bar to check for differential patches against [`update_manifest.json`](update_manifest.json).
- **Offline USB Update**: Simply replace `EAATrainingManager.exe` with the new version. Existing databases and records are preserved automatically.

---

## 📄 Documentation

For full system architecture, user manuals, and the WhatsApp communication kit, see [`MASTER.md`](MASTER.md).

---

## 🏛️ Copyright & Acknowledgments
Developed for the **Egyptian Aviation Academy (الأكاديمية المصرية لعلوم الطيران)** – Ministry of Civil Aviation, Arab Republic of Egypt.
