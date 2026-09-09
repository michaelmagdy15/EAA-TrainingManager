using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace EAATrainingManager.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event Action? LanguageChanged;

    private bool _isEnglish = false;
    public bool IsEnglish
    {
        get => _isEnglish;
        set
        {
            if (_isEnglish != value)
            {
                _isEnglish = value;
                LanguageChanged?.Invoke();
            }
        }
    }

    public FlowDirection CurrentFlowDirection => _isEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    public void ToggleLanguage()
    {
        IsEnglish = !IsEnglish;
    }

    public string Text(string arabic, string english)
    {
        return _isEnglish ? english : arabic;
    }

    // Common system translations
    public string AppTitle => Text(
        "الأكاديمية المصرية لعلوم الطيران – الكلية المصرية للطيران – إدارة التدريب",
        "Egyptian Aviation Academy – Civil Aviation College – Flight Training Directorate");

    public string WindowTitle => Text(
        "منظومة إدارة أوامر التدريب – الأكاديمية المصرية لعلوم الطيران",
        "EAA Flight Training Operations Management System");

    public string DirectorateName => Text("إدارة التدريب", "Training Directorate");
    public string AcademySubTitle => Text("EAA Training Manager", "Egyptian Aviation Academy");

    public string OfflineBadge => Text("نظام محلي يعمل 100% بدون إنترنت", "100% Offline Local System (No Internet Required)");

    // Navigation Menu
    public string NavDashboard => Text("الرئيسية والمؤشرات", "Dashboard & KPIs");
    public string NavStreamsHeader => Text("المسارات التدريبية", "Training Streams");
    public string NavPart61 => Text("النظام الحر - 61 (Part 61)", "Part 61 (Modular)");
    public string NavPart141 => Text("الدفعات المعتمدة - 141 (Part 141)", "Part 141 (Batches)");
    public string NavATP => Text("خط جوي - ATP", "ATP Ground School");
    public string NavTypeRating => Text("تجديد طراز وبناء ساعات", "Type Rating & Hours");
    public string NavEvaluation => Text("التقييم والمعادلات", "Equivalency & Evaluations");
    public string NavStudents => Text("سجل الطلبة والمتدربين", "Trainee Directory");
    public string NavOrders => Text("كافة أوامر التدريب", "Training Orders Log");
    public string NavExcelSync => Text("استيراد وتصدير الذكي", "Smart Excel Sync");
}
