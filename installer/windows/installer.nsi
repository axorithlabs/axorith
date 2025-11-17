!define PRODUCT_NAME "Axorith"
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.0.0-dev"
!endif
!define PRODUCT_PUBLISHER "Axorith Labs"

!ifndef BUILD_ROOT
  !error "BUILD_ROOT must be defined!"
!endif

OutFile "..\..\build\installer\${PRODUCT_NAME}-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
Icon "assets\icon.ico"
UninstallIcon "assets\icon.ico"

!include "MUI2.nsh"
!include "StrFunc.nsh"
${Using:StrFunc} StrRep

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC_INSTALL
  SetOutPath "$INSTDIR"
  SetOverwrite on
  SetDetailsPrint textonly

  File /r "${BUILD_ROOT}\*"

  SetDetailsPrint listonly

  CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\Axorith.Client\Axorith.Client.exe"

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}" '"$INSTDIR\Axorith.Client\Axorith.Client.exe" --tray'

  CreateDirectory "$INSTDIR\Axorith.Shim"

  Var /GLOBAL NATIVE_HOST_PATH
  StrCpy $NATIVE_HOST_PATH "$INSTDIR\Axorith.Shim\Axorith.Shim.exe"

  Var /GLOBAL ESCAPED_PATH
  ${StrRep} $ESCAPED_PATH $NATIVE_HOST_PATH "\" "\\"

  FileOpen $0 "$INSTDIR\Axorith.Shim\axorith.json" "w"
  IfErrors 0 +2
    Abort "Error creating Native Messaging host manifest!"

  FileWrite $0 '{$\r$\n'
  FileWrite $0 '  "name": "axorith",$\r$\n'
  FileWrite $0 '  "description": "Native messaging host for the Axorith Deep Work OS.",$\r$\n'
  FileWrite $0 '  "path": "$ESCAPED_PATH",$\r$\n'
  FileWrite $0 '  "type": "stdio",$\r$\n'
  FileWrite $0 '  "allowed_extensions": [$\r$\n'
  FileWrite $0 '    "mail@axorithlabs.com"$\r$\n'
  FileWrite $0 '  ]$\r$\n'
  FileWrite $0 '}'
  
  FileClose $0
  
  WriteRegStr HKCU "Software\Mozilla\NativeMessagingHosts\axorith" "" "$INSTDIR\Axorith.Shim\axorith.json"
  
  WriteRegExpandStr HKCU "Environment" "AXORITH_HOST_PATH" "$INSTDIR\Axorith.Host\Axorith.Host.exe"

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayIcon" "$INSTDIR\Axorith.Client\icon.ico"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" '"$INSTDIR\uninstall.exe"'

  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
  
  DeleteRegKey HKCU "Software\Mozilla\NativeMessagingHosts\axorith"

  DeleteRegValue HKCU "Environment" "AXORITH_HOST_PATH"
  
  RMDir /r "$INSTDIR"
  
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
SectionEnd