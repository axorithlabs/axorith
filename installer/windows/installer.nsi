!define PRODUCT_NAME "Axorith"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.0.0-dev"
!endif
!define PRODUCT_PUBLISHER "Axorith Labs"
!define PRODUCT_DESCRIPTION "Digital Life System"
!define PRODUCT_COPYRIGHT "Copyright (C) 2025 Axorith Labs"

!define DOTNET_DESKTOP_RUNTIME_VERSION "10.0.1"
!define DOTNET_ASPNET_RUNTIME_VERSION "10.0.1"
!define DOTNET_DESKTOP_RUNTIME_URL "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.1/windowsdesktop-runtime-10.0.1-win-x64.exe"
!define DOTNET_ASPNET_RUNTIME_URL "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.1/aspnetcore-runtime-10.0.1-win-x64.exe"

!ifndef BUILD_ROOT
  !error "BUILD_ROOT must be defined!"
!endif

Name "${PRODUCT_NAME}"
OutFile "..\..\build\Installer\${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
InstallDirRegKey HKCU "Software\${PRODUCT_NAME}" ""
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!macro ExtractNumericVersion VERSION_IN VERSION_OUT
    !searchparse /noerrors "${VERSION_IN}" "" _VERSION_NUM "-" _VERSION_SUFFIX
    !ifndef _VERSION_NUM
        !define _VERSION_NUM "${VERSION_IN}"
    !endif
    !searchparse /noerrors "${_VERSION_NUM}" _V_MAJOR "." _V_MINOR "." _V_PATCH
    !ifndef _V_PATCH
        !searchparse /noerrors "${_VERSION_NUM}" _V_MAJOR "." _V_MINOR
        !ifndef _V_MINOR
            !define _V_MAJOR "${_VERSION_NUM}"
            !define _V_MINOR "0"
        !endif
        !define _V_PATCH "0"
    !endif
    !ifndef _V_MAJOR
        !define _V_MAJOR "0"
    !endif
    !ifndef _V_MINOR
        !define _V_MINOR "0"
    !endif
    !ifndef _V_PATCH
        !define _V_PATCH "0"
    !endif
    !define ${VERSION_OUT} "${_V_MAJOR}.${_V_MINOR}.${_V_PATCH}.0"
    !undef _VERSION_NUM
    !ifdef _VERSION_SUFFIX
        !undef _VERSION_SUFFIX
    !endif
    !undef _V_MAJOR
    !undef _V_MINOR
    !undef _V_PATCH
!macroend

!insertmacro ExtractNumericVersion "${PRODUCT_VERSION}" PRODUCT_VERSION_NUMERIC

VIProductVersion "${PRODUCT_VERSION_NUMERIC}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${PRODUCT_COPYRIGHT}"
VIAddVersionKey "FileDescription" "${PRODUCT_DESCRIPTION}"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "OriginalFilename" "${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"
!include "x64.nsh"

!define MUI_ICON "assets\icon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_ABORTWARNING
!insertmacro MUI_LANGUAGE "English"

Var DesktopRuntimeInstalled
Var AspNetRuntimeInstalled

Function StrContainsFunc
    Exch $R1
    Exch
    Exch $R2
    Push $R3
    Push $R4
    Push $R5
    StrLen $R3 $R1
    StrCpy $R4 0
    loop:
        StrCpy $R5 $R2 $R3 $R4
        StrCmp $R5 $R1 found
        StrCmp $R5 "" notfound
        IntOp $R4 $R4 + 1
        Goto loop
    found:
        StrCpy $R1 $R5
        Goto done
    notfound:
        StrCpy $R1 ""
    done:
    Pop $R5
    Pop $R4
    Pop $R3
    Pop $R2
    Exch $R1
FunctionEnd

!macro StrContains ResultVar String SubString
    Push "${String}"
    Push "${SubString}"
    Call StrContainsFunc
    Pop "${ResultVar}"
!macroend

Function CheckDotNetDesktopRuntime
    StrCpy $DesktopRuntimeInstalled "0"
    nsExec::ExecToStack 'dotnet --list-runtimes'
    Pop $0
    Pop $1
    StrCmp $0 "0" 0 done_desktop
    !insertmacro StrContains $0 $1 "Microsoft.WindowsDesktop.App 10."
    StrCmp $0 "" done_desktop
    StrCpy $DesktopRuntimeInstalled "1"
    done_desktop:
FunctionEnd

Function CheckDotNetAspNetRuntime
    StrCpy $AspNetRuntimeInstalled "0"
    nsExec::ExecToStack 'dotnet --list-runtimes'
    Pop $0
    Pop $1
    StrCmp $0 "0" 0 done_aspnet
    !insertmacro StrContains $0 $1 "Microsoft.AspNetCore.App 10."
    StrCmp $0 "" done_aspnet
    StrCpy $AspNetRuntimeInstalled "1"
    done_aspnet:
FunctionEnd

Function InstallDotNetDesktopRuntime
    DetailPrint "Downloading .NET Desktop Runtime ${DOTNET_DESKTOP_RUNTIME_VERSION}..."
    SetOutPath "$TEMP"
    inetc::get /SILENT "${DOTNET_DESKTOP_RUNTIME_URL}" "$TEMP\dotnet-desktop-runtime.exe" /END
    Pop $0
    StrCmp $0 "OK" +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to download .NET Desktop Runtime. Please install it manually from https://dotnet.microsoft.com/download"
    Return
    DetailPrint "Installing .NET Desktop Runtime ${DOTNET_DESKTOP_RUNTIME_VERSION}..."
    ExecWait '"$TEMP\dotnet-desktop-runtime.exe" /install /quiet /norestart' $0
    IntCmp $0 0 +2
    ExecWait '"$TEMP\dotnet-desktop-runtime.exe" /install /passive /norestart' $0
    Delete "$TEMP\dotnet-desktop-runtime.exe"
FunctionEnd

Function InstallDotNetAspNetRuntime
    DetailPrint "Downloading ASP.NET Core Runtime ${DOTNET_ASPNET_RUNTIME_VERSION}..."
    SetOutPath "$TEMP"
    inetc::get /SILENT "${DOTNET_ASPNET_RUNTIME_URL}" "$TEMP\aspnetcore-runtime.exe" /END
    Pop $0
    StrCmp $0 "OK" +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to download ASP.NET Core Runtime. Please install it manually from https://dotnet.microsoft.com/download"
    Return
    DetailPrint "Installing ASP.NET Core Runtime ${DOTNET_ASPNET_RUNTIME_VERSION}..."
    ExecWait '"$TEMP\aspnetcore-runtime.exe" /install /quiet /norestart' $0
    IntCmp $0 0 +2
    ExecWait '"$TEMP\aspnetcore-runtime.exe" /install /passive /norestart' $0
    Delete "$TEMP\aspnetcore-runtime.exe"
FunctionEnd

Section "Prerequisites" SEC_PREREQ
    SetShellVarContext all
    
    DetailPrint "Checking .NET Desktop Runtime..."
    Call CheckDotNetDesktopRuntime
    StrCmp $DesktopRuntimeInstalled "1" skip_desktop
    Call InstallDotNetDesktopRuntime
    Goto done_desktop_section
    skip_desktop:
    DetailPrint ".NET Desktop Runtime 10.x is already installed"
    done_desktop_section:
    
    DetailPrint "Checking ASP.NET Core Runtime..."
    Call CheckDotNetAspNetRuntime
    StrCmp $AspNetRuntimeInstalled "1" skip_aspnet
    Call InstallDotNetAspNetRuntime
    Goto done_aspnet_section
    skip_aspnet:
    DetailPrint "ASP.NET Core Runtime 10.x is already installed"
    done_aspnet_section:
SectionEnd

Section "MainSection" SEC_INSTALL
    SetShellVarContext current

    ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString"
    ${If} $0 != ""
        DetailPrint "Found existing installation, uninstalling..."
        
        nsExec::ExecToStack 'taskkill /F /IM Axorith.Client.exe'
        Pop $1
        nsExec::ExecToStack 'taskkill /F /IM Axorith.Host.exe'
        Pop $1
        
        Sleep 1000
        
        StrCpy $1 $0 -1 1
        ExecWait '$1 /S _?=$INSTDIR'
        
        Sleep 500
    ${EndIf}

    SetOutPath "$INSTDIR"
  
    SetOverwrite on
    SetDetailsPrint textonly
  
    File /r "${BUILD_ROOT}\*"
  
    SetDetailsPrint listonly

    SetOutPath "$INSTDIR\Axorith.Client"
    CreateShortCut "$smprograms\${PRODUCT_NAME}.lnk" "$INSTDIR\Axorith.Client\Axorith.Client.exe" "" "$INSTDIR\Axorith.Client\Assets\icon.ico"
    SetOutPath "$INSTDIR"
  
    WriteRegExpandStr HKCU "Environment" "AXORITH_HOST_PATH" "$INSTDIR\Axorith.Host\Axorith.Host.exe"
  
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\Axorith.Client\Assets\icon.ico"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Comments" "${PRODUCT_DESCRIPTION}"

    System::Call 'USER32::SendMessageTimeoutA(i ${HWND_BROADCAST},i ${WM_SETTINGCHANGE},i 0,t "Environment",i 0x0002,i .r0)'
  
    WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    Delete "$smprograms\${PRODUCT_NAME}.lnk"
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
    DeleteRegKey /ifempty HKCU "Software\Mozilla\NativeMessagingHosts\axorith"
    DeleteRegKey /ifempty HKCU "Software\Mozilla\NativeMessagingHosts\axorith.dev"
    DeleteRegKey /ifempty HKCU "Software\Google\Chrome\NativeMessagingHosts\axorith"
    DeleteRegKey /ifempty HKCU "Software\Google\Chrome\NativeMessagingHosts\axorith.dev"
    DeleteRegKey /ifempty HKCU "Software\Chromium\NativeMessagingHosts\axorith"
    DeleteRegKey /ifempty HKCU "Software\Chromium\NativeMessagingHosts\axorith.dev"
    DeleteRegKey /ifempty HKCU "Software\Microsoft\Edge\NativeMessagingHosts\axorith"
    DeleteRegKey /ifempty HKCU "Software\Microsoft\Edge\NativeMessagingHosts\axorith.dev"
    DeleteRegValue HKCU "Environment" "AXORITH_HOST_PATH"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
SectionEnd
