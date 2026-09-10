using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace EAATrainingManager.Helpers;

public static class FilePickerHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName([In, Out] ref OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    /// <summary>
    /// Prompts the user to select an Excel workbook (.xlsx / .xls) with dual fallback support.
    /// </summary>
    public static async Task<string?> PickExcelFileAsync(Window? window = null)
    {
        IntPtr hwnd = IntPtr.Zero;
        try
        {
            var targetWindow = window ?? MainWindow.Current;
            if (targetWindow != null)
            {
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(targetWindow);
            }
        }
        catch { }

        // 1. Try modern Windows App SDK FileOpenPicker
        try
        {
            var picker = new FileOpenPicker();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");

            var file = await picker.PickSingleFileAsync();
            if (file != null && !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
            {
                return file.Path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FilePickerHelper] Modern picker failed: {ex.Message}. Falling back to Win32.");
        }

        // 2. Ultra-reliable Win32 GetOpenFileName fallback
        try
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrFilter = "ملفات إكسيل (*.xlsx;*.xls)\0*.xlsx;*.xls\0جميع الملفات (*.*)\0*.*\0";
            ofn.lpstrFile = new string(new char[1024]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            ofn.lpstrTitle = "اختر ملف سجل أوامر التدريب (Excel) - الأكاديمية المصرية لعلوم الطيران";
            ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            ofn.lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (GetOpenFileName(ref ofn))
            {
                string chosen = ofn.lpstrFile.TrimEnd('\0').Trim();
                if (!string.IsNullOrWhiteSpace(chosen) && File.Exists(chosen))
                {
                    return chosen;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FilePickerHelper] Win32 fallback failed: {ex.Message}");
        }

        return null;
    }
}
