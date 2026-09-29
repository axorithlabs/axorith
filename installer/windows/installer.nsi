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

Name "${PRODUCT_NAME}"
OutFile "..\..\build\Installer\axorith-setup.exe"
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
VIAddVersionKey "OriginalFilename" "axorith-setup.exe"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"

${StrCase}
${UnStrCase}

!define MUI_ICON "assets\icon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_ABORTWARNING
!define MUI_CUSTOMFUNCTION_ABORT "InstallerUserAbort"
!define MUI_CUSTOMFUNCTION_UNABORT "un.InstallerUserAbort"
!insertmacro MUI_LANGUAGE "English"

Var TelemetryEnabled
Var InstallAttemptId
Var InstallerDistinctId
Var InstallationId
Var InstallationIdPersisted
Var InstallMode
Var CurrentVersion
Var PreviousVersion
Var TelemetryEventName
Var TelemetryEventProperties
Var TelemetryFailureStage
Var TelemetryFailureReported
Var DesktopRuntimeInstalled
Var AspNetRuntimeInstalled

!macro CheckCommittedSession Action AllowedLabel TelemetrySender
    IfFileExists "$INSTDIR\Axorith.Host\Axorith.Host.exe" 0 ${AllowedLabel}
    nsExec::ExecToStack '"$INSTDIR\Axorith.Host\Axorith.Host.exe" --check-committed-session'
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

!macro DefineTelemetryGuidFunction Prefix
Function ${Prefix}GenerateTelemetryGuid
    System::Call 'ole32::CoCreateGuid(g .s)'
    Pop $0
    StrCpy $0 $0 36 1
    !if "${Prefix}" == "un."
        ${UnStrCase} $0 $0 "L"
    !else
        ${StrCase} $0 $0 "L"
    !endif
    Push $0
FunctionEnd
!macroend

!macro DefineTelemetryInitFunction Prefix Mode
Function ${Prefix}InitializeTelemetry
    StrCpy $TelemetryEnabled "0"
    StrCpy $InstallationIdPersisted "0"
    StrCpy $TelemetryFailureReported "0"
    StrCpy $TelemetryFailureStage "installer_launch"
    StrCpy $TelemetryEventProperties ""
    SetShellVarContext current
    !if "${Mode}" == "uninstall"
        StrCpy $InstallAttemptId ""
        ReadRegStr $CurrentVersion HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion"
        StrCpy $PreviousVersion ""
        StrCpy $InstallMode "uninstall"
    !else
        ReadRegStr $PreviousVersion HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion"
        StrCpy $CurrentVersion "${PRODUCT_VERSION}"
        StrCmp $PreviousVersion "" telemetry_fresh_install
        StrCpy $InstallMode "update"
        Goto telemetry_mode_done
        telemetry_fresh_install:
        StrCpy $InstallMode "fresh"
        telemetry_mode_done:
        Call ${Prefix}GenerateTelemetryGuid
        Pop $InstallAttemptId
    !endif

    CreateDirectory "$APPDATA\Axorith\config"
    !if "${Mode}" != "uninstall"
        Delete "$APPDATA\Axorith\config\pending-install.json"
    !endif
    !if "${Mode}" == "uninstall"
        IfFileExists "$APPDATA\Axorith\config\installation-id.txt" 0 telemetry_uninstall_generate_id
        FileOpen $1 "$APPDATA\Axorith\config\installation-id.txt" r
        IfErrors telemetry_uninstall_generate_id
        FileRead $1 $InstallationId
        FileClose $1
        StrCpy $InstallationId $InstallationId 36
        StrLen $2 $InstallationId
        IntCmp $2 36 telemetry_uninstall_valid_id telemetry_uninstall_generate_id telemetry_uninstall_generate_id
        telemetry_uninstall_valid_id:
        StrCpy $InstallerDistinctId $InstallationId
        StrCpy $InstallationIdPersisted "1"
        Goto telemetry_identity_done
        telemetry_uninstall_generate_id:
        Call ${Prefix}GenerateTelemetryGuid
        Pop $InstallerDistinctId
    !else
        StrCmp $InstallMode "fresh" telemetry_generate_installation_id
        IfFileExists "$APPDATA\Axorith\config\installation-id.txt" 0 telemetry_generate_installation_id
        FileOpen $1 "$APPDATA\Axorith\config\installation-id.txt" r
        IfErrors telemetry_generate_installation_id
        FileRead $1 $InstallationId
        FileClose $1
        StrCpy $InstallationId $InstallationId 36
        StrLen $2 $InstallationId
        IntCmp $2 36 telemetry_installation_id_loaded telemetry_generate_installation_id telemetry_generate_installation_id
        telemetry_installation_id_loaded:
        StrCpy $InstallerDistinctId $InstallationId
        StrCpy $InstallationIdPersisted "1"
        Goto telemetry_identity_done
        telemetry_generate_installation_id:
        Call ${Prefix}GenerateTelemetryGuid
        Pop $InstallationId
        StrCpy $InstallerDistinctId $InstallationId
        ClearErrors
        FileOpen $1 "$APPDATA\Axorith\config\installation-id.txt" w
        IfErrors telemetry_identity_done
        FileWrite $1 "$InstallationId"
        FileClose $1
        IfErrors telemetry_identity_done
        StrCpy $InstallationIdPersisted "1"
    !endif
    telemetry_identity_done:
    !if "${POSTHOG_API_KEY}" != ""
        StrCmp $InstallationIdPersisted "1" 0 telemetry_preference_done
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
    StrCmp $TelemetryEnabled "1" 0 telemetry_send_done
    StrCpy $1 ""
    StrCmp $TelemetryEventProperties "" telemetry_extra_done
    StrCpy $1 ",$TelemetryEventProperties"
    telemetry_extra_done:
    StrCpy $2 "$\"application$\":$\"Axorith.Installer$\",$\"source$\":$\"installer$\",$\"platform$\":$\"windows$\",$\"architecture$\":$\"x64$\""
    StrCmp $InstallMode "uninstall" telemetry_no_install_attempt_id
    StrCpy $2 "$2,$\"installAttemptId$\":$\"$InstallAttemptId$\""
    telemetry_no_install_attempt_id:
    StrCpy $0 "{$\"api_key$\":$\"${POSTHOG_API_KEY}$\",$\"event$\":$\"$TelemetryEventName$\",$\"distinct_id$\":$\"$InstallerDistinctId$\",$\"properties$\":{$2,$\"installMode$\":$\"$InstallMode$\",$\"currentVersion$\":$\"$CurrentVersion$\",$\"previousVersion$\":$\"$PreviousVersion$\"$1}}"
    inetc::post /HEADER "Content-Type: application/json" "$0" /CONNECTTIMEOUT 1 /RECEIVETIMEOUT 1 /SILENT /NOCANCEL "${POSTHOG_API_HOST}/capture" "$TEMP\axorith-telemetry-$InstallerDistinctId.txt" /END
    Pop $5
    Delete "$TEMP\axorith-telemetry-$InstallerDistinctId.txt"
    telemetry_send_done:
    Pop $5
    Pop $4
    Pop $3
    Pop $2
    Pop $1
    Pop $0
FunctionEnd
!macroend

!insertmacro DefineTelemetryGuidFunction ""
!insertmacro DefineTelemetryGuidFunction "un."
!insertmacro DefineTelemetryInitFunction "" "install"
!insertmacro DefineTelemetryInitFunction "un." "uninstall"
!insertmacro DefineTelemetrySenderFunction ""
!insertmacro DefineTelemetrySenderFunction "un."

Function .onInit
    Call InitializeTelemetry
    StrCpy $TelemetryEventName "InstallerStarted"
    Call SendTelemetryEvent
    !insertmacro CheckCommittedSession "Installation or update" on_init_allowed SendTelemetryEvent
FunctionEnd

Function un.onInit
    Call un.InitializeTelemetry
    ${GetParameters} $0
    ${GetOptions} $0 "/UPDATE" $1
    StrCmp $1 "" un_user_uninstall
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
    CreateDirectory "$APPDATA\Axorith\config"
    StrCmp $TelemetryEnabled "1" 0 install_pending_written
    ClearErrors
    FileOpen $0 "$APPDATA\Axorith\config\pending-install.json" w
    IfErrors install_pending_written
    FileWrite $0 "{$\"installationId$\":$\"$InstallationId$\",$\"installAttemptId$\":$\"$InstallAttemptId$\",$\"installMode$\":$\"$InstallMode$\",$\"currentVersion$\":$\"$CurrentVersion$\",$\"previousVersion$\":$\"$PreviousVersion$\"}"
    FileClose $0
    install_pending_written:
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
    StrCpy $TelemetryFailureStage "prerequisite_download"
    SetOutPath "$TEMP"
    inetc::get /SILENT "${DOTNET_DESKTOP_RUNTIME_URL}" "$TEMP\dotnet-desktop-runtime.exe" /END
    Pop $0
    StrCmp $0 "OK" +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to download .NET Desktop Runtime. Please install it manually from https://dotnet.microsoft.com/download"
    Return
    DetailPrint "Installing .NET Desktop Runtime ${DOTNET_DESKTOP_RUNTIME_VERSION}..."
    StrCpy $TelemetryFailureStage "prerequisite_install"
    ExecWait '"$TEMP\dotnet-desktop-runtime.exe" /install /quiet /norestart' $0
    IntCmp $0 0 +2
    ExecWait '"$TEMP\dotnet-desktop-runtime.exe" /install /passive /norestart' $0
    Delete "$TEMP\dotnet-desktop-runtime.exe"
FunctionEnd

Function InstallDotNetAspNetRuntime
    DetailPrint "Downloading ASP.NET Core Runtime ${DOTNET_ASPNET_RUNTIME_VERSION}..."
    StrCpy $TelemetryFailureStage "prerequisite_download"
    SetOutPath "$TEMP"
    inetc::get /SILENT "${DOTNET_ASPNET_RUNTIME_URL}" "$TEMP\aspnetcore-runtime.exe" /END
    Pop $0
    StrCmp $0 "OK" +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to download ASP.NET Core Runtime. Please install it manually from https://dotnet.microsoft.com/download"
    Return
    DetailPrint "Installing ASP.NET Core Runtime ${DOTNET_ASPNET_RUNTIME_VERSION}..."
    StrCpy $TelemetryFailureStage "prerequisite_install"
    ExecWait '"$TEMP\aspnetcore-runtime.exe" /install /quiet /norestart' $0
    IntCmp $0 0 +2
    ExecWait '"$TEMP\aspnetcore-runtime.exe" /install /passive /norestart' $0
    Delete "$TEMP\aspnetcore-runtime.exe"
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
    ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString"
    ${If} $0 != ""
        DetailPrint "Found existing installation, uninstalling..."
        
        nsExec::ExecToStack 'taskkill /F /IM Axorith.Client.exe'
        Pop $1
        nsExec::ExecToStack 'taskkill /F /IM Axorith.Host.exe'
        Pop $1
        
        Sleep 1000
        
        StrCpy $1 $0 -1 1
        ExecWait '$1 /S /UPDATE _?=$INSTDIR'
        
        Sleep 500
    ${EndIf}

    StrCpy $TelemetryFailureStage "files_copy"
    SetOutPath "$INSTDIR"
  
    SetOverwrite on
    SetDetailsPrint textonly
  
    File /r "${BUILD_ROOT}\*"
  
    SetDetailsPrint listonly

    StrCpy $TelemetryFailureStage "registration"
    SetOutPath "$INSTDIR\Axorith.Client"
    CreateShortCut "$smprograms\${PRODUCT_NAME}.lnk" "$INSTDIR\Axorith.Client\Axorith.Client.exe" "" "$INSTDIR\Axorith.Client\Assets\icon.ico" 0 SW_SHOWNORMAL "" "Launch ${PRODUCT_NAME}"
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
    !insertmacro CheckCommittedSession "Uninstall" uninstall_section_allowed un.SendTelemetryEvent
    StrCpy $TelemetryFailureStage "uninstall_cleanup"
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
