!ifndef AXORITH_WINDOWS_DEVICE_IDENTITY
!define AXORITH_WINDOWS_DEVICE_IDENTITY

!macro DefineWindowsDeviceIdentityFunction Prefix
Function ${Prefix}GetWindowsDeviceIdentity
    StrCpy $1 0
    StrCpy $2 0
    StrCpy $3 0
    StrCpy $4 0
    StrCpy $5 0
    StrCpy $9 ""
    System::Call 'combase::RoInitialize(i 0) i.r0'
    ${If} $0 < 0
    ${AndIf} $0 != 0x80010106
        Goto identity_cleanup
    ${EndIf}
    StrLen $6 "Windows.System.Profile.SystemIdentification"
    System::Call 'combase::WindowsCreateString(w "Windows.System.Profile.SystemIdentification", i r6, *p.r1) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    System::Call 'combase::RoGetActivationFactory(p r1, g "{5581F42A-D3DF-4D93-A37D-C41A616C6D01}", *p.r2) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    ; ISystemIdentificationStatics::GetSystemIdForPublisher, exactly as in Shared.
    System::Call '$2->6(*p.r3) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    System::Call '$3->7(*i.r8) i.r6'
    ${If} $6 < 0
    ${OrIf} $8 == 0
        Goto identity_cleanup
    ${EndIf}
    System::Call '$3->6(*p.r4) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    System::Call '$4->7(*i.r8) i.r6'
    ${If} $6 < 0
    ${OrIf} $8 <= 0
        Goto identity_cleanup
    ${EndIf}
    System::Call '$4->0(g "{905A0FEF-BC53-11DF-8C49-001E4FC686DA}", *p.r5) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    System::Call '$5->3(*p.r7) i.r6'
    ${If} $6 < 0
        Goto identity_cleanup
    ${EndIf}
    ; CRYPT_STRING_HEXRAW | CRYPT_STRING_NOCRLF preserves every ID byte.
    System::Call 'crypt32::CryptBinaryToStringW(p r7, i r8, i 0x4000000c, w .r9, *i ${NSIS_MAX_STRLEN}) i.r6'
    ${If} $6 == 0
        StrCpy $9 ""
    ${EndIf}
    identity_cleanup:
    ${If} $5 != 0
        System::Call '$5->2()'
    ${EndIf}
    ${If} $4 != 0
        System::Call '$4->2()'
    ${EndIf}
    ${If} $3 != 0
        System::Call '$3->2()'
    ${EndIf}
    ${If} $2 != 0
        System::Call '$2->2()'
    ${EndIf}
    ${If} $1 != 0
        System::Call 'combase::WindowsDeleteString(p r1)'
    ${EndIf}
    ${If} $0 >= 0
        System::Call 'combase::RoUninitialize()'
    ${EndIf}
    !if "${Prefix}" == "un."
        ${UnStrCase} $9 $9 "L"
    !else
        ${StrCase} $9 $9 "L"
    !endif
    Push $9
FunctionEnd
!macroend

!endif
