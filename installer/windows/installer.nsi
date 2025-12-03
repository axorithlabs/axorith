!define PRODUCT_NAME "Axorith"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.0.0-dev"
!endif
!define PRODUCT_PUBLISHER "Axorith Labs"
!define PRODUCT_DESCRIPTION "Productivity OS that helps you automate your focus rituals"
!define PRODUCT_COPYRIGHT "Copyright (C) 2025 Axorith Labs"

!ifndef BUILD_ROOT
  !error "BUILD_ROOT must be defined!"
!endif

Name "${PRODUCT_NAME}"
OutFile "..\..\build\Installer\${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
InstallDirRegKey HKCU "Software\${PRODUCT_NAME}" ""
RequestExecutionLevel user
SetCompressor /SOLID lzma

# Extract numeric version for VIProductVersion (strip pre-release suffixes like -alpha, -beta)
# VIProductVersion requires format X.Y.Z.W where all parts are numeric
!macro ExtractNumericVersion VERSION_IN VERSION_OUT
    # Extract numeric part before any dash (pre-release suffix)
    !searchparse /noerrors "${VERSION_IN}" "" _VERSION_NUM "-" _VERSION_SUFFIX
    !ifndef _VERSION_NUM
        !define _VERSION_NUM "${VERSION_IN}"
    !endif
    # Ensure we have at least 3 parts (X.Y.Z), pad with .0 if needed
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
    !undef _VERSION_SUFFIX
    !undef _V_MAJOR
    !undef _V_MINOR
    !undef _V_PATCH
!macroend

# Extract numeric version for VIProductVersion
!insertmacro ExtractNumericVersion "${PRODUCT_VERSION}" PRODUCT_VERSION_NUMERIC

# Version Information
VIProductVersion "${PRODUCT_VERSION_NUMERIC}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${PRODUCT_COPYRIGHT}"
VIAddVersionKey "FileDescription" "${PRODUCT_DESCRIPTION}"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "OriginalFilename" "${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"

!include "MUI2.nsh"
!define MUI_ICON "assets\icon.ico"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_ABORTWARNING
!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC_INSTALL
  SetShellVarContext current

  SetOutPath "$INSTDIR"
  
  SetOverwrite on
  SetDetailsPrint textonly
  
  File /r "${BUILD_ROOT}\*"
  
  SetDetailsPrint listonly

  SetOutPath "$INSTDIR\Axorith.Client"
  CreateShortCut "$smprograms\${PRODUCT_NAME}.lnk" "$INSTDIR\Axorith.Client\Axorith.Client.exe" "" "$INSTDIR\Axorith.Client\Assets\icon.ico"
  SetOutPath "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}" '"$INSTDIR\Axorith.Client\Axorith.Client.exe" --tray'
  WriteRegExpandStr HKCU "Environment" "AXORITH_HOST_PATH" "$INSTDIR\Axorith.Host\Axorith.Host.exe"
  
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\Axorith.Client\Assets\icon.ico"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Comments" "${PRODUCT_DESCRIPTION}"
  
  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  Delete "$smprograms\${PRODUCT_NAME}.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
  DeleteRegKey /ifempty HKCU "Software\Mozilla\NativeMessagingHosts\axorith"
  DeleteRegValue HKCU "Environment" "AXORITH_HOST_PATH"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
SectionEnd