!define PRODUCT_NAME "Axorith"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.0.0-dev"
!endif
!define PRODUCT_PUBLISHER "Axorith Labs"

!ifndef BUILD_ROOT
  !error "BUILD_ROOT must be defined!"
!endif

Name "${PRODUCT_NAME}"
OutFile "..\..\build\Installer\${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
InstallDirRegKey HKCU "Software\${PRODUCT_NAME}" ""
RequestExecutionLevel user
SetCompressor /SOLID lzma

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
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\Axorith.Client\icon.ico"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  
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