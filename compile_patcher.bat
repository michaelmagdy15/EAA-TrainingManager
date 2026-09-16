@echo off
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:"EAA_Patch_v2.2.7_Setup.exe" /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:"EAA_Delta_Patch_v2.2.7.zip",Patch.zip "patcher.cs"
