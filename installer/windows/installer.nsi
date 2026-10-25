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
!ifndef POSTHOG_API_KEY
  !define POSTHOG_API_KEY ""
!endif
!ifndef POSTHOG_API_HOST
  !define POSTHOG_API_HOST "https://us.i.posthog.com"
!endif
!ifndef BUILD_SHA
  !define BUILD_SHA ""
!endif

Name "${PRODUCT_NAME}"
OutFile "..\..\build\Installer\axorith-setup.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
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
VIAddVersionKey "OriginalFilename" "axorith-setup.exe"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"

${StrCase}
${StrStr}
${UnStrCase}

!include "WindowsDeviceIdentity.nsh"
!insertmacro DefineWindowsDeviceIdentityFunction ""
!insertmacro DefineWindowsDeviceIdentityFunction "un."

!define MUI_ICON "assets\icon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_ABORTWARNING
!define MUI_CUSTOMFUNCTION_ABORT "InstallerUserAbort"
!define MUI_CUSTOMFUNCTION_UNABORT "un.InstallerUserAbort"
!insertmacro MUI_LANGUAGE "English"

Var TelemetryEnabled
Var InstallerDistinctId
Var InstallAttemptId
Var UninstallAttemptId
Var CommonAppData
Var InstallMode
Var CurrentVersion
Var PreviousVersion
Var TelemetryEventName
Var TelemetryEventProperties
Var TelemetryFailureStage
Var TelemetryFailureReported
Var DesktopRuntimeInstalled
Var AspNetRuntimeInstalled
Var MaintenanceDirectory
Var MaintenanceHost

!macro DefineProtectedRootFunction Prefix
Function ${Prefix}PrepareProtectedRoot
    ; Override /D and _? arguments: privileged payloads must stay under Program Files.
    StrCpy $INSTDIR "$PROGRAMFILES64\${PRODUCT_NAME}"
    System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
    IntOp $1 $0 & 0x400
    ${If} $0 != -1
    ${AndIf} $1 != 0
        MessageBox MB_OK|MB_ICONSTOP "The Axorith installation directory cannot be a reparse point."
        Abort
    ${EndIf}
    CreateDirectory "$INSTDIR"
    System::Call 'advapi32::ConvertStringSecurityDescriptorToSecurityDescriptorW(w "O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)", i 1, *p.r0, p 0) i.r1'
    ${If} $1 == 0
        MessageBox MB_OK|MB_ICONSTOP "Could not protect the Axorith installation directory."
        Abort
    ${EndIf}
    System::Call 'advapi32::SetFileSecurityW(w "$INSTDIR", i 0x80000005, p r0) i.r1'
    System::Call 'kernel32::LocalFree(p r0)'
    ${If} $1 == 0
        MessageBox MB_OK|MB_ICONSTOP "Could not protect the Axorith installation directory."
        Abort
    ${EndIf}
FunctionEnd
!macroend

!insertmacro DefineProtectedRootFunction ""
!insertmacro DefineProtectedRootFunction "un."

!macro CheckCommittedSession Action AllowedLabel TelemetrySender
    IfFileExists "$INSTDIR\Axorith.Host\Axorith.Host.exe" ${AllowedLabel}_check
    IfFileExists "$LOCALAPPDATA\Programs\${PRODUCT_NAME}\Axorith.Host\Axorith.Host.exe" ${AllowedLabel}_check
    IfFileExists "$APPDATA\Axorith\config\committed-session.json" ${AllowedLabel}_check
    IfFileExists "$APPDATA\Axorith\config\strict-protection.json" ${AllowedLabel}_check
    IfFileExists "$CommonAppData\Axorith\CommittedSessions\*.*" ${AllowedLabel}_check ${AllowedLabel}
${AllowedLabel}_check:
    nsExec::ExecToStack '"$MaintenanceHost" --check-committed-session --environment Production --contentRoot "$MaintenanceDirectory" --Persistence:ConfigPath "$APPDATA\Axorith\config"'
    Pop $0
    Pop $1
    StrCmp $0 "0" ${AllowedLabel}
    ${If} $1 == ""
        StrCpy $1 "Axorith could not verify whether a committed session is active."
    ${EndIf}
    StrCpy $TelemetryFailureStage "installer_launch"
    !if "${Action}" == "Uninstall"
        StrCpy $TelemetryEventName "UninstallFailed"
    !else
        StrCpy $TelemetryEventName "InstallerFailed"
    !endif
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"$TelemetryFailureStage$\""
    Call ${TelemetrySender}
    StrCpy $TelemetryFailureReported "1"
    MessageBox MB_OK|MB_ICONSTOP "$1 ${Action} is blocked."
    Abort
${AllowedLabel}:
!macroend

Function GenerateMaintenanceSuffix
    System::Call 'ole32::CoCreateGuid(g .s)'
    Pop $0
    StrCpy $0 $0 36 1
    ${StrCase} $0 $0 "L"
    Push $0
FunctionEnd

!macro DefineTelemetryInitFunction Prefix Mode
Function ${Prefix}InitializeTelemetry
    StrCpy $TelemetryEnabled "0"
    StrCpy $TelemetryFailureReported "0"
    StrCpy $TelemetryFailureStage "installer_launch"
    StrCpy $TelemetryEventProperties ""
    SetShellVarContext current
    SetShellVarContext all
    StrCpy $CommonAppData $APPDATA
    SetShellVarContext current
    !if "${Mode}" == "uninstall"
        System::Call 'ole32::CoCreateGuid(g .s)'
        Pop $UninstallAttemptId
        StrCpy $UninstallAttemptId $UninstallAttemptId 36 1
        ReadRegStr $CurrentVersion HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion"
        StrCpy $PreviousVersion ""
        StrCpy $InstallMode "uninstall"
    !else
        System::Call 'ole32::CoCreateGuid(g .s)'
        Pop $InstallAttemptId
        StrCpy $InstallAttemptId $InstallAttemptId 36 1
        ${StrCase} $InstallAttemptId $InstallAttemptId "L"
        ReadRegStr $PreviousVersion HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion"
        StrCpy $CurrentVersion "${PRODUCT_VERSION}"
        StrCmp $PreviousVersion "" telemetry_fresh_install
        StrCpy $InstallMode "update"
        Goto telemetry_mode_done
        telemetry_fresh_install:
        StrCpy $InstallMode "fresh"
        telemetry_mode_done:
    !endif

    Call ${Prefix}GetWindowsDeviceIdentity
    Pop $InstallerDistinctId
    !if "${POSTHOG_API_KEY}" != ""
        StrCmp $InstallerDistinctId "" telemetry_preference_done
        StrCpy $TelemetryEnabled "1"
        IfFileExists "$APPDATA\Axorith\config\telemetry-preference.txt" 0 telemetry_preference_done
        FileOpen $0 "$APPDATA\Axorith\config\telemetry-preference.txt" r
        IfErrors telemetry_preference_done
        FileRead $1 $0
        FileClose $0
        StrCmp $1 "false" 0 telemetry_preference_done
        StrCpy $TelemetryEnabled "0"
        telemetry_preference_done:
    !endif
FunctionEnd
!macroend

!macro DefineTelemetrySenderFunction Prefix
Function ${Prefix}SendTelemetryEvent
    Push $0
    Push $1
    Push $2
    Push $3
    Push $4
    Push $5
    Push $6
    StrCmp $TelemetryEnabled "1" 0 telemetry_send_done
    ${GetTime} "" "LS" $0 $1 $2 $3 $4 $5 $6
    StrCpy $4 "$2-$1-$0T$4:$5:$6Z"
    ; INetC can resend an accepted HTTPS POST after a receive timeout.
    System::Call 'ole32::CoCreateGuid(g .s)'
    Pop $3
    StrCpy $3 $3 36 1
    StrCpy $1 ""
    StrCmp $TelemetryEventProperties "" telemetry_extra_done
    StrCpy $1 ",$TelemetryEventProperties"
    telemetry_extra_done:
    StrCpy $2 "$\"application$\":$\"Axorith.Installer$\",$\"source$\":$\"installer$\",$\"platform$\":$\"windows$\",$\"architecture$\":$\"x64$\",$\"$$geoip_disable$\":true"
    StrCpy $2 "$2,$\"axorith_version$\":$\"$CurrentVersion$\",$\"release_version$\":$\"$CurrentVersion$\""
    !if "${BUILD_SHA}" != ""
        StrCpy $2 "$2,$\"build_sha$\":$\"${BUILD_SHA}$\""
    !endif
    StrCmp $InstallMode "uninstall" telemetry_uninstall_attempt
    StrCpy $2 "$2,$\"installAttemptId$\":$\"$InstallAttemptId$\""
    Goto telemetry_attempt_done
    telemetry_uninstall_attempt:
    StrCpy $2 "$2,$\"uninstallAttemptId$\":$\"$UninstallAttemptId$\""
    telemetry_attempt_done:
    StrCpy $0 "{$\"api_key$\":$\"${POSTHOG_API_KEY}$\",$\"uuid$\":$\"$3$\",$\"timestamp$\":$\"$4$\",$\"event$\":$\"$TelemetryEventName$\",$\"distinct_id$\":$\"$InstallerDistinctId$\",$\"properties$\":{$2,$\"installMode$\":$\"$InstallMode$\",$\"currentVersion$\":$\"$CurrentVersion$\",$\"previousVersion$\":$\"$PreviousVersion$\"$1}}"
    InitPluginsDir
    inetc::post "$0" /HEADER "Content-Type: application/json" /CONNECTTIMEOUT 1 /RECEIVETIMEOUT 1 /SILENT /NOCANCEL "${POSTHOG_API_HOST}/capture" "$PLUGINSDIR\telemetry-response.txt" /END
    Pop $5
    Delete "$PLUGINSDIR\telemetry-response.txt"
    telemetry_send_done:
    Pop $6
    Pop $5
    Pop $4
    Pop $3
    Pop $2
    Pop $1
    Pop $0
FunctionEnd
!macroend

!insertmacro DefineTelemetryInitFunction "" "install"
!insertmacro DefineTelemetryInitFunction "un." "uninstall"
!insertmacro DefineTelemetrySenderFunction ""
!insertmacro DefineTelemetrySenderFunction "un."

Function .onInit
    Call PrepareProtectedRoot
    Call InitializeTelemetry
    Call GenerateMaintenanceSuffix
    Pop $0
    StrCpy $MaintenanceDirectory "$INSTDIR\.setup-$0"
    CreateDirectory "$MaintenanceDirectory"
    SetOutPath "$MaintenanceDirectory"
    File /r "${BUILD_ROOT}\Axorith.Host\*"
    StrCpy $MaintenanceHost "$MaintenanceDirectory\Axorith.Host.exe"
    StrCpy $TelemetryEventName "InstallerStarted"
    Call SendTelemetryEvent
    !insertmacro CheckCommittedSession "Installation or update" on_init_allowed SendTelemetryEvent
FunctionEnd

Function un.onInit
    Call un.PrepareProtectedRoot
    StrCpy $MaintenanceDirectory "$INSTDIR\Axorith.Host"
    StrCpy $MaintenanceHost "$MaintenanceDirectory\Axorith.Host.exe"
    Call un.InitializeTelemetry
    ${GetParameters} $0
    ClearErrors
    ${GetOptions} $0 "/UPDATE" $1
    IfErrors un_user_uninstall
    StrCpy $TelemetryEnabled "0"
    Goto un_start_event_done
    un_user_uninstall:
    StrCpy $TelemetryEventName "UninstallStarted"
    Call un.SendTelemetryEvent
    un_start_event_done:
    !insertmacro CheckCommittedSession "Uninstall" un_on_init_allowed un.SendTelemetryEvent
FunctionEnd

Function InstallerUserAbort
    StrCpy $TelemetryEventName "InstallerCancelled"
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"$TelemetryFailureStage$\""
    Call SendTelemetryEvent
FunctionEnd

Function .onGUIEnd
    SetOutPath "$TEMP"
    ${GetParent} "$MaintenanceDirectory" $0
    ${GetFileName} "$MaintenanceDirectory" $1
    StrCpy $1 $1 7
    ${If} $0 == $INSTDIR
    ${AndIf} $1 == ".setup-"
        RMDir /r "$MaintenanceDirectory"
    ${EndIf}
FunctionEnd

Function un.InstallerUserAbort
    StrCpy $TelemetryEventName "UninstallCancelled"
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"installer_launch$\""
    Call un.SendTelemetryEvent
FunctionEnd

Function .onInstFailed
    StrCmp $TelemetryFailureReported "1" inst_failed_done
    StrCpy $TelemetryEventName "InstallerFailed"
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"$TelemetryFailureStage$\""
    Call SendTelemetryEvent
    inst_failed_done:
FunctionEnd

Function un.onUninstFailed
    StrCmp $TelemetryFailureReported "1" un_inst_failed_done
    StrCpy $TelemetryEventName "UninstallFailed"
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"$TelemetryFailureStage$\""
    Call un.SendTelemetryEvent
    StrCpy $TelemetryFailureReported "1"
    un_inst_failed_done:
FunctionEnd

Function .onInstSuccess
    Call CheckDotNetDesktopRuntime
    Call CheckDotNetAspNetRuntime
    StrCpy $TelemetryFailureStage "registration"
    StrCmp $DesktopRuntimeInstalled "1" desktop_runtime_present
    StrCpy $0 "false"
    Goto desktop_runtime_result
    desktop_runtime_present:
    StrCpy $0 "true"
    desktop_runtime_result:
    StrCmp $AspNetRuntimeInstalled "1" aspnet_runtime_present
    StrCpy $1 "false"
    Goto aspnet_runtime_result
    aspnet_runtime_present:
    StrCpy $1 "true"
    aspnet_runtime_result:
    StrCpy $TelemetryEventName "InstallerCompleted"
    StrCpy $TelemetryEventProperties "$\"hasDesktopRuntime$\":$0,$\"hasAspNetRuntime$\":$1"
    Call SendTelemetryEvent
FunctionEnd

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
    nsExec::ExecToStack '"$PROGRAMFILES64\dotnet\dotnet.exe" --list-runtimes'
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
    nsExec::ExecToStack '"$PROGRAMFILES64\dotnet\dotnet.exe" --list-runtimes'
    Pop $0
    Pop $1
    StrCmp $0 "0" 0 done_aspnet
    !insertmacro StrContains $0 $1 "Microsoft.AspNetCore.App 10."
    StrCmp $0 "" done_aspnet
    StrCpy $AspNetRuntimeInstalled "1"
    done_aspnet:
FunctionEnd

!macro InstallDotNetRuntime RuntimeName RuntimeVersion RuntimeUrl InstallerFile
    DetailPrint "Downloading ${RuntimeName} ${RuntimeVersion}..."
    StrCpy $TelemetryFailureStage "prerequisite_download"
    SetOutPath "$MaintenanceDirectory"
    inetc::get /SILENT "${RuntimeUrl}" "$MaintenanceDirectory\${InstallerFile}" /END
    Pop $0
    ${If} $0 != "OK"
        Delete "$MaintenanceDirectory\${InstallerFile}"
        MessageBox MB_OK|MB_ICONSTOP "Failed to download ${RuntimeName}. Axorith installation cannot continue. Please install it manually from https://dotnet.microsoft.com/download" /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    DetailPrint "Installing ${RuntimeName} ${RuntimeVersion}..."
    StrCpy $TelemetryFailureStage "prerequisite_install"
    ClearErrors
    ExecWait '"$MaintenanceDirectory\${InstallerFile}" /install /quiet /norestart' $0
    ${IfNot} ${Errors}
    ${AndIf} $0 != 0
    ${AndIf} $0 != 3010
        ExecWait '"$MaintenanceDirectory\${InstallerFile}" /install /passive /norestart' $0
    ${EndIf}
    ${If} ${Errors}
        StrCpy $0 "launch failed"
    ${EndIf}
    Delete "$MaintenanceDirectory\${InstallerFile}"
    ${If} $0 == 3010
        SetRebootFlag true
    ${ElseIf} $0 != 0
        MessageBox MB_OK|MB_ICONSTOP "Failed to install ${RuntimeName} ($0). Axorith installation cannot continue." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
!macroend

Function InstallDotNetDesktopRuntime
    !insertmacro InstallDotNetRuntime ".NET Desktop Runtime" "${DOTNET_DESKTOP_RUNTIME_VERSION}" "${DOTNET_DESKTOP_RUNTIME_URL}" "dotnet-desktop-runtime.exe"
FunctionEnd

Function InstallDotNetAspNetRuntime
    !insertmacro InstallDotNetRuntime "ASP.NET Core Runtime" "${DOTNET_ASPNET_RUNTIME_VERSION}" "${DOTNET_ASPNET_RUNTIME_URL}" "aspnetcore-runtime.exe"
FunctionEnd

Section "Prerequisites" SEC_PREREQ
    SetShellVarContext all
    StrCpy $TelemetryFailureStage "prerequisite_download"
    
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
    !insertmacro CheckCommittedSession "Installation or update" install_section_allowed SendTelemetryEvent

    StrCpy $TelemetryFailureStage "existing_version_removal"
    IfFileExists "$APPDATA\Axorith\config\clientsettings.json" migrate_client_settings_done
    IfFileExists "$LOCALAPPDATA\Programs\${PRODUCT_NAME}\Axorith.Client\clientsettings.json" 0 migrate_client_settings_done
    CreateDirectory "$APPDATA\Axorith\config"
    ClearErrors
    CopyFiles /SILENT "$LOCALAPPDATA\Programs\${PRODUCT_NAME}\Axorith.Client\clientsettings.json" "$APPDATA\Axorith\config"
    ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "Could not preserve your Axorith settings. Please close Axorith and try again." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    migrate_client_settings_done:

    nsExec::ExecToStack '"$SYSDIR\taskkill.exe" /F /IM Axorith.Client.exe'
    Pop $0
    Pop $1
    nsExec::ExecToStack '"$SYSDIR\taskkill.exe" /F /IM Axorith.Host.exe'
    Pop $0
    Pop $1

    StrCpy $TelemetryFailureStage "files_copy"
    SetOutPath "$INSTDIR"
  
    SetOverwrite on
    SetDetailsPrint textonly
  
    ClearErrors
    File /r "${BUILD_ROOT}\*"
    ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "Could not copy the Axorith files. Please close Axorith and try again." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    ${GetFileVersion} "$INSTDIR\Axorith.Client\Axorith.Client.dll" $0
    ${GetFileVersion} "$INSTDIR\Axorith.Host\Axorith.Host.dll" $1
    ${If} $0 != "${PRODUCT_VERSION_NUMERIC}"
    ${OrIf} $1 != "${PRODUCT_VERSION_NUMERIC}"
        MessageBox MB_OK|MB_ICONSTOP "The installed Axorith files do not match this update. Please close Axorith and run the installer again." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    SetOutPath "$INSTDIR"
    RMDir /r "$MaintenanceDirectory"
  
    SetDetailsPrint listonly

    StrCpy $TelemetryFailureStage "registration"
    SetOutPath "$INSTDIR\Axorith.Client"
    CreateShortCut "$smprograms\${PRODUCT_NAME}.lnk" "$INSTDIR\Axorith.Client\Axorith.Client.exe" "" "$INSTDIR\Axorith.Client\Assets\icon.ico" 0 SW_SHOWNORMAL "" "Launch ${PRODUCT_NAME}"
    SetOutPath "$INSTDIR"
  
    WriteRegExpandStr HKCU "Environment" "AXORITH_HOST_PATH" "$INSTDIR\Axorith.Host\Axorith.Host.exe"
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
    ${If} $0 != ""
        ${StrStr} $1 $0 "--tray"
        ${If} $1 != ""
            WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}" '"$INSTDIR\Axorith.Client\Axorith.Client.exe" --tray'
        ${Else}
            WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}" '"$INSTDIR\Axorith.Client\Axorith.Client.exe"'
        ${EndIf}
    ${EndIf}
  
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\Axorith.Client\Assets\icon.ico"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Comments" "${PRODUCT_DESCRIPTION}"

    System::Call 'USER32::SendMessageTimeoutW(p ${HWND_BROADCAST}, i ${WM_SETTINGCHANGE}, p 0, w "Environment", i 0x0002, i 5000, *p .r0) p .r1'
  
    WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
    !insertmacro CheckCommittedSession "Uninstall" uninstall_section_allowed un.SendTelemetryEvent
    StrCpy $TelemetryFailureStage "uninstall_cleanup"
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    Delete "$smprograms\${PRODUCT_NAME}.lnk"
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
    IfFileExists "$INSTDIR\Axorith.Shim\Axorith.Shim.exe" 0 uninstaller_skip_legacy_firefox_policy
    nsExec::ExecToStack '"$INSTDIR\Axorith.Shim\Axorith.Shim.exe" --remove-legacy-firefox-policy'
    Pop $0
    uninstaller_skip_legacy_firefox_policy:
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
    IfFileExists "$INSTDIR\Axorith.Client\Axorith.Client.exe" uninstall_cleanup_failed
    IfFileExists "$INSTDIR\Axorith.Host\Axorith.Host.exe" uninstall_cleanup_failed
    StrCpy $TelemetryEventName "UninstallCompleted"
    StrCpy $TelemetryEventProperties ""
    Call un.SendTelemetryEvent
    Goto uninstall_telemetry_done
    uninstall_cleanup_failed:
    StrCpy $TelemetryEventName "UninstallFailed"
    StrCpy $TelemetryEventProperties "$\"stage$\":$\"$TelemetryFailureStage$\""
    Call un.SendTelemetryEvent
    StrCpy $TelemetryFailureReported "1"
    uninstall_telemetry_done:
SectionEnd
