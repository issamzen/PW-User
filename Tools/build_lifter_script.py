# -*- coding: utf-8 -*-
"""
Builds LifterStudio.CATScript (VBScript, runs INSIDE CATIA through
SystemService.ExecuteScript) from the original CATVBA macro script.vba,
and generates the C# file that embeds it.

Pipeline:
  1. hand-written VBScript blocks replace the VBA blocks that cannot be
     translated mechanically (Win32 declares, On Error GoTo handlers,
     Collections, Print #/Open file I/O, Shell, Format$, DoEvents...)
  2. the rest of the macro goes through a mechanical VBA -> VBScript pass
  3. a small runtime library (FSO / WScript.Shell helpers) is appended
"""
import re, io, os

SRC = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Scripts', 'LifterStudio.catvba')
OUT_VBS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Scripts', 'LifterStudio.CATScript')
OUT_CS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'LifterStudioScript.cs')

text = open(SRC, encoding='utf-8').read().replace(chr(13), '')

PATCHES = []


def patch(old, new):
    PATCHES.append((old.strip('\n'), new.strip('\n')))


# ---------------------------------------------------------------- header
patch('''
Option Explicit

#If VBA7 Then
Private Declare PtrSafe Function FindWindow Lib "user32" Alias "FindWindowA" (ByVal c As String, ByVal n As String) As LongPtr
Private Declare PtrSafe Function SetWindowPos Lib "user32" (ByVal h As LongPtr, ByVal a As LongPtr, ByVal x As Long, ByVal y As Long, ByVal w As Long, ByVal z As Long, ByVal f As Long) As Long
#Else
Private Declare Function FindWindow Lib "user32" Alias "FindWindowA" (ByVal c As String, ByVal n As String) As Long
Private Declare Function SetWindowPos Lib "user32" (ByVal H As Long, ByVal a As Long, ByVal x As Long, ByVal y As Long, ByVal W As Long, ByVal z As Long, ByVal f As Long) As Long
#End If
''', '''
'==================================================================
' LifterStudio.CATScript - "CATIA Lifter Parameters" dashboard
'------------------------------------------------------------------
' VBScript edition of the CATVBA macro, executed INSIDE CATIA by the
' PW-User dashboard (SystemService.ExecuteScript). Same HTA user
' interface, same parameter names, same VBA <-> HTA file protocol,
' same PowerCopy hand-off.
'
' CATMain returns to the dashboard:
'   POWERCOPY:<linked>:<drafts>  the user asked for the PowerCopy
'   CLOSED:<linked>:<drafts>     the user closed the dashboard
'   CANCELLED                    first-run setup was cancelled
'   ERROR:<reason>               nothing could be done
'==================================================================

Dim gFso, gShell
Set gFso = CreateObject("Scripting.FileSystemObject")
Set gShell = CreateObject("WScript.Shell")
''')

# ---------------------------------------------------------------- CATMain
old_catmain = text[text.index('Public Sub CATMain()'):text.index('End Sub', text.index('Public Sub CATMain()')) + len('End Sub')]
patch(old_catmain, '''
Function CATMain()
    On Error Resume Next

    Dim doc, part
    Dim token, windowTitle
    Dim hta, cmd, rsp
    Dim setupHta, setupCmd, setupRsp
    Dim data, action
    Dim launchPowerCopy
    Dim booleanAction
    Dim linkedCount, draftCount

    launchPowerCopy = False
    CATMain = "ERROR:The lifter dashboard could not start."

    If CATIA.Documents.Count = 0 Then
        MsgBox "Open a CATPart first.", vbExclamation, APP_TITLE
        CATMain = "ERROR:No document is open in CATIA - open the destination CATPart first."
        Exit Function
    End If
    If TypeName(CATIA.ActiveDocument) <> "PartDocument" Then
        MsgBox "The active document must be a CATPart.", vbExclamation, APP_TITLE
        CATMain = "ERROR:The active CATIA document must be a CATPart."
        Exit Function
    End If

    Set doc = CATIA.ActiveDocument
    Set part = doc.Part
    If Err.Number <> 0 Then
        CATMain = "ERROR:The destination CATPart is not accessible."
        Exit Function
    End If

    Randomize
    token = StampText("yyyymmdd_hhnnss") & CStr(Int(Rnd() * 999999))
    windowTitle = APP_TITLE & " - " & token
    hta = TempDir() & "\\Lifter_" & token & ".hta"
    cmd = TempDir() & "\\LifterCmd_" & token & ".txt"
    rsp = TempDir() & "\\LifterRsp_" & token & ".txt"
    setupHta = TempDir() & "\\LifterSetup_" & token & ".hta"
    setupCmd = TempDir() & "\\LifterSetupCmd_" & token & ".txt"
    setupRsp = TempDir() & "\\LifterSetupRsp_" & token & ".txt"
    DeleteFile hta
    DeleteFile cmd
    DeleteFile rsp
    DeleteFile setupHta
    DeleteFile setupCmd
    DeleteFile setupRsp

    ' ===== First run: modern one-time setup =====
    ' When "STROKE_Distance" does not exist yet, the main body is
    ' auto-detected and measured inside a branded setup panel.
    If Not StrokeParameterExists(part) Then
        If Not FirstRunSetup(part, setupHta, setupCmd, setupRsp, windowTitle & " - Setup") Then
            CATMain = "CANCELLED"
            Exit Function
        End If
    End If

    ' ===== Single source of truth for STROKE_Distance =====
    linkedCount = LinkStrokeToMainBody(part)
    draftCount = CreateDraftParameters(part)

    BuildDashboard hta, cmd, rsp, doc.Name, part, windowTitle, linkedCount, draftCount
    ShellRun "mshta.exe " & QuoteText(hta)
    MakeTopmost windowTitle

    Do
        data = WaitCommand(cmd, WAIT_SECONDS)
        If Len(data) = 0 Then Exit Do
        DeleteFile cmd
        DeleteFile rsp          ' never let the dashboard read a stale reply
        action = UCase(ReadKey(data, "COMMAND"))
        If action = "CLOSE" Then Exit Do
        If action = "REFRESH" Then SendValues part, rsp, ReadInstance(data)
        If action = "UPDATE" Then UpdateValues part, data, rsp, ReadInstance(data)
        booleanAction = ""
        If action = "SELECTBODY" Then booleanAction = action
        If action = "REMOVEONE" Then booleanAction = action
        If Len(booleanAction) > 0 Then HandleBooleanCommand part, booleanAction, data, rsp, windowTitle
        If action = "POWERCOPY" Then
            launchPowerCopy = True
            Exit Do
        End If
    Loop

    DeleteFile cmd
    DeleteFile rsp
    If launchPowerCopy Then
        StartPowerCopy
        CATMain = "POWERCOPY:" & CStr(linkedCount) & ":" & CStr(draftCount)
    Else
        DeleteFile hta
        CATMain = "CLOSED:" & CStr(linkedCount) & ":" & CStr(draftCount)
    End If
End Function
''')

# ------------------------------------------------- LinkStrokeToMainBody
patch('''
    Set relations = part.relations
    Set sets = GetHeadSets(part)
    cnt = 0

    For i = 1 To sets.count
        Set s = sets.item(i)
        On Error Resume Next
        Set strokeP = s.DirectParameters.item(P_STROKE)
        On Error GoTo 0
        If Not strokeP Is Nothing Then
            On Error Resume Next
            Err.Clear
            relations.CreateFormula "", "", strokeP, rootName
            errNo = Err.Number
            On Error GoTo 0
            If errNo = 0 Then cnt = cnt + 1
        End If
    Next i

    part.Update
    LinkStrokeToMainBody = cnt
    Exit Function

Failed:
    LinkStrokeToMainBody = 0
End Function
''', '''
    Set relations = part.Relations
    Set sets = GetHeadSets(part)
    cnt = 0

    For i = 1 To sets.Count
        Set s = sets.Item(i)
        Set strokeP = Nothing
        Err.Clear
        Set strokeP = s.DirectParameters.Item(P_STROKE)
        Err.Clear
        If Not strokeP Is Nothing Then
            Err.Clear
            relations.CreateFormula "", "", strokeP, rootName
            errNo = Err.Number
            Err.Clear
            If errNo = 0 Then cnt = cnt + 1
        End If
    Next

    part.Update
    Err.Clear
    LinkStrokeToMainBody = cnt
End Function
''')

# ------------------------------------------------ CreateDraftParameters
old = text[text.index('Private Function CreateDraftParameters(ByVal part As Object) As Long'):]
old = old[:old.index('End Function') + len('End Function')]
patch(old, '''
Function CreateDraftParameters(part)
    On Error Resume Next

    Dim relations
    Dim sets, s
    Dim undercutP, strokeP, draftP
    Dim undercutName, strokeName, formulaText
    Dim i, cnt, errNo
    Dim oldRel

    Set relations = part.Relations
    Set sets = GetHeadSets(part)
    cnt = 0

    For i = 1 To sets.Count
        Set s = sets.Item(i)

        Set undercutP = Nothing
        Set strokeP = Nothing
        Set draftP = Nothing
        Err.Clear
        Set undercutP = s.DirectParameters.Item(P5)
        Set strokeP = s.DirectParameters.Item(P_STROKE)
        Err.Clear

        If (Not undercutP Is Nothing) And (Not strokeP Is Nothing) Then

            ' Get or create the Draft Angle parameter inside this LIFTER_HEAD set.
            Set draftP = s.DirectParameters.Item(P_DRAFT)
            Err.Clear
            If draftP Is Nothing Then
                Set draftP = s.DirectParameters.CreateDimension(P_DRAFT, "ANGLE", 0)
                Err.Clear
            End If

            If Not draftP Is Nothing Then
                ' Robust paths to the two inputs.
                undercutName = part.Parameters.GetNameToUseInRelation(undercutP)
                strokeName = part.Parameters.GetNameToUseInRelation(strokeP)
                Err.Clear

                formulaText = "min( floor( atan( ( " & undercutName & " + 5mm ) / " & strokeName _
                              & " ) * 180 / PI + 0.5 ), " & CStr(DRAFT_MAX_DEG) & " ) * 1deg"

                ' Replace any existing formula driving this Draft parameter so the
                ' current rule (with +5mm) always wins over an older formula.
                Set oldRel = GetFormulaFor(relations, draftP)
                If Not oldRel Is Nothing Then
                    relations.Remove oldRel.Name
                    Err.Clear
                End If

                ' Create the formula (idempotent in effect: one formula per parameter).
                Err.Clear
                relations.CreateFormula "", "", draftP, formulaText
                errNo = Err.Number
                Err.Clear
                If errNo = 0 Then cnt = cnt + 1
            End If
        End If
    Next

    part.Update
    Err.Clear
    CreateDraftParameters = cnt
End Function
''')

# --------------------------------------------- LinkStrokeAndCreateDraft
patch('''
    MsgBox "Linked " & linked & " instance STROKE_Distance parameter(s) to the main body." & vbCrLf & _
           "Created/refreshed " & drafts & " Draft parameter(s).", vbInformation, APP_TITLE
    Exit Sub

Failed:
    MsgBox "Fix-up stopped." & vbCrLf & Err.Description, vbCritical, APP_TITLE
End Sub
''', '''
    If Err.Number <> 0 Then
        MsgBox "Fix-up stopped." & vbCrLf & Err.Description, vbCritical, APP_TITLE
        Err.Clear
        Exit Sub
    End If

    MsgBox "Linked " & linked & " instance STROKE_Distance parameter(s) to the main body." & vbCrLf & _
           "Created/refreshed " & drafts & " Draft parameter(s).", vbInformation, APP_TITLE
End Sub
''')

# ------------------------------------------- CalculateSTROKEDistance
patch('''
    CalculateSTROKEDistance = MeasureStrokeOnBody(part, mainBody)
    Exit Function

Failed:
    CalculateSTROKEDistance = False
End Function
''', '''
    If Err.Number <> 0 Then
        Err.Clear
        CalculateSTROKEDistance = False
        Exit Function
    End If

    CalculateSTROKEDistance = MeasureStrokeOnBody(part, mainBody)
End Function
''')

# ------------------------------------------------ MeasureStrokeOnBody
old = text[text.index('Private Function MeasureStrokeOnBody(ByVal part As Object, ByVal mainBody As Object) As Boolean'):]
old = old[:old.index('End Function') + len('End Function')]
patch(old, '''
Function MeasureStrokeOnBody(part, mainBody)
    On Error Resume Next

    Dim hsf, spaWB, gs, refBody
    Dim lineX, lineY, lineZ
    Dim extWmax, extWmin, planeWmax, planeWmin
    Dim m, W, params, p

    MeasureStrokeOnBody = False

    Set hsf = part.HybridShapeFactory
    Set spaWB = part.Parent.GetWorkbench("SPAWorkbench")
    If Err.Number <> 0 Then
        Err.Clear
        Exit Function
    End If

    '----- Temporary set (deleted at start AND at the end) -----
    DeleteGeometricalSet part, "TEMP_STROKE"
    Err.Clear
    Set gs = part.HybridBodies.Add
    gs.Name = "TEMP_STROKE"

    Set refBody = part.CreateReferenceFromObject(mainBody)
    If Err.Number <> 0 Then
        Err.Clear
        DeleteGeometricalSet part, "TEMP_STROKE"
        Exit Function
    End If

    '----- STANDARD box axes (choice "1" of the BBox macro) -----
    Set lineX = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(-10000, 0, 0), hsf.AddNewPointCoord(10000, 0, 0))
    Set lineY = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(0, -10000, 0), hsf.AddNewPointCoord(0, 10000, 0))
    Set lineZ = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(0, 0, -10000), hsf.AddNewPointCoord(0, 0, 10000))
    gs.AppendHybridShape lineX
    gs.AppendHybridShape lineY
    gs.AppendHybridShape lineZ

    '----- The 2 extremums of W (direction Y), BBox-macro style -----
    Set extWmax = Build_BoxExtremum(hsf, refBody, 1, lineY, lineZ, lineX)   ' max along Y
    Set extWmin = Build_BoxExtremum(hsf, refBody, 0, lineY, lineZ, lineX)   ' min along Y
    gs.AppendHybridShape extWmax
    gs.AppendHybridShape extWmin

    '----- The 2 limit planes of W -----
    Set planeWmax = hsf.AddNewPlaneNormal(lineY, extWmax)
    Set planeWmin = hsf.AddNewPlaneNormal(lineY, extWmin)
    gs.AppendHybridShape planeWmax
    gs.AppendHybridShape planeWmin
    part.Update

    If Err.Number <> 0 Then
        Err.Clear
        DeleteGeometricalSet part, "TEMP_STROKE"
        Exit Function
    End If

    '----- W = distance between the 2 planes (standard measure) -----
    Set m = spaWB.GetMeasurable(planeWmax)
    W = m.GetMinimumDistance(planeWmin)
    If Err.Number <> 0 Or Not IsNumeric(W) Then
        Err.Clear
        DeleteGeometricalSet part, "TEMP_STROKE"
        Exit Function
    End If

    '----- Keep ONLY W: parameter -----
    Set params = part.Parameters
    Set p = Nothing
    Err.Clear
    Set p = params.Item(P_STROKE)
    Err.Clear
    If p Is Nothing Then
        Set p = params.CreateDimension(P_STROKE, "LENGTH", W)
    Else
        p.Value = W
    End If
    If Err.Number <> 0 Then
        Err.Clear
        DeleteGeometricalSet part, "TEMP_STROKE"
        Exit Function
    End If

    '----- Remove the TEMP_STROKE geometrical set entirely -----
    DeleteGeometricalSet part, "TEMP_STROKE"
    part.Update
    Err.Clear

    MeasureStrokeOnBody = True
End Function
''')

# ----------------------------------------------------- DetectMainBody
patch('''
Private Function DetectMainBody(ByVal part As Object) As Object
    Dim bodies As Object
    On Error Resume Next
    Set DetectMainBody = part.mainBody
    On Error GoTo 0
    If Not DetectMainBody Is Nothing Then Exit Function

    On Error Resume Next
    Set bodies = part.bodies
    If Not bodies Is Nothing Then
        If bodies.count > 0 Then Set DetectMainBody = bodies.item(1)
    End If
    On Error GoTo 0
End Function
''', '''
Function DetectMainBody(part)
    On Error Resume Next
    Dim bodies, found
    Set found = Nothing
    Set found = part.MainBody
    Err.Clear
    If found Is Nothing Then
        Set bodies = Nothing
        Set bodies = part.Bodies
        Err.Clear
        If Not bodies Is Nothing Then
            If bodies.Count > 0 Then Set found = bodies.Item(1)
        End If
        Err.Clear
    End If
    Set DetectMainBody = found
End Function
''')

# ------------------------------------------------------ SetupResponse
patch('''
Private Sub SetupResponse(ByVal path As String, ByVal result As String, ByVal value As String)
    Dim f As Integer
    DeleteFile path
    f = FreeFile
    Open path For Output As #f
    Print #f, "RESULT=" & result
    Print #f, "VALUE=" & value
    Close #f
End Sub
''', '''
Sub SetupResponse(path, result, value)
    On Error Resume Next
    Dim f
    DeleteFile path
    Set f = NewWriter(path)
    If f Is Nothing Then Exit Sub
    f.WriteLine "RESULT=" & result
    f.WriteLine "VALUE=" & value
    f.Close
    Err.Clear
End Sub
''')

# ------------------------------------------------------- FirstRunSetup
patch('''
Private Function FirstRunSetup(ByVal part As Object, ByVal hta As String, ByVal cmd As String, ByVal rsp As String, ByVal setupTitle As String) As Boolean
    On Error GoTo Failed
''', '''
Function FirstRunSetup(part, hta, cmd, rsp, setupTitle)
    On Error Resume Next
''')
patch('''
CleanExit:
    DeleteFile cmd
    DeleteFile rsp
    DeleteFile hta
    Exit Function

Failed:
    MsgBox "Setup failed." & vbCrLf & Err.Description, vbCritical, APP_TITLE
    FirstRunSetup = False
    Resume CleanExit
End Function
''', '''
    ' Errors are never fatal here: the setup panel already shows the
    ' FAIL state and offers Retry / Choose another body / Cancel.
    Err.Clear

    DeleteFile cmd
    DeleteFile rsp
    DeleteFile hta
    Err.Clear
End Function
''')
patch('''
    BuildSetupHta hta, cmd, rsp, setupTitle, detectText, detectOk
    Shell "mshta.exe " & QuoteText(hta), vbNormalFocus
''', '''
    BuildSetupHta hta, cmd, rsp, setupTitle, detectText, detectOk
    ShellRun "mshta.exe " & QuoteText(hta)
''')

# --------------------------------------------- HTA writers: file handles
patch('''
Private Sub BuildSetupHta(ByVal hta As String, ByVal cmd As String, ByVal rsp As String, ByVal title As String, ByVal detectText As String, ByVal detectOk As Boolean)
    Dim f As Integer
    f = FreeFile
    Open hta For Output As #f
''', '''
Sub BuildSetupHta(hta, cmd, rsp, title, detectText, detectOk)
    On Error Resume Next
    Dim f
    Set f = NewWriter(hta)
    If f Is Nothing Then Exit Sub
''')
patch('''
    W f, "</body></html>"
    Close #f
End Sub


'------------------------------------------------------------------
' Setup panel CSS (same visual language as the dashboard)
''', '''
    W f, "</body></html>"
    f.Close
End Sub


'------------------------------------------------------------------
' Setup panel CSS (same visual language as the dashboard)
''')

patch('''
    fileNumber = FreeFile
    Open hta For Output As #fileNumber
''', '''
    Set fileNumber = NewWriter(hta)
    If fileNumber Is Nothing Then Exit Sub
''')
patch('''
    W fileNumber, "</body></html>"
    Close #fileNumber
End Sub
''', '''
    W fileNumber, "</body></html>"
    fileNumber.Close
End Sub
''')

# ------------------------------------------------- body storage (Collection)
old = text[text.index('Private Sub EnsureBodyCollections()'):]
old = old[:old.index('Private Function StoredInstanceName')]
patch(old, '''
Sub EnsureBodyCollections()
    On Error Resume Next
    If Not IsObject(gCopyBodyObjects) Then Set gCopyBodyObjects = NewStore()
    If Not IsObject(gTargetBodyObjects) Then Set gTargetBodyObjects = NewStore()
    If Not IsObject(gCopyBodyNames) Then Set gCopyBodyNames = NewStore()
    If Not IsObject(gTargetBodyNames) Then Set gTargetBodyNames = NewStore()
    If gCopyBodyObjects Is Nothing Then Set gCopyBodyObjects = NewStore()
    If gTargetBodyObjects Is Nothing Then Set gTargetBodyObjects = NewStore()
    If gCopyBodyNames Is Nothing Then Set gCopyBodyNames = NewStore()
    If gTargetBodyNames Is Nothing Then Set gTargetBodyNames = NewStore()
    Err.Clear
End Sub

Function NewStore()
    Set NewStore = CreateObject("Scripting.Dictionary")
End Function

Function StoredKey(instanceIndex)
    StoredKey = Pad3(instanceIndex)
End Function

Function InstanceTag(instanceIndex)
    InstanceTag = Pad3(instanceIndex)
End Function

'------------------------------------------------------------------
' The stores keep body NAMES, not COM references: the application
' calls this script once per dashboard command, so nothing survives
' in memory between two commands. A name is resolved to the live
' body on every use.
'------------------------------------------------------------------
Function GetStoredObject(store, instanceIndex)
    On Error Resume Next
    Set GetStoredObject = Nothing
    If store Is Nothing Then Exit Function
    Dim nm
    nm = GetStoredText(store, instanceIndex)
    If Len(nm) = 0 Then Exit Function
    Set GetStoredObject = ResolveBodyByName(nm)
    Err.Clear
End Function

Sub SetStoredObject(store, instanceIndex, item)
    On Error Resume Next
    Dim nm
    nm = ""
    If Not item Is Nothing Then nm = CStr(item.Name)
    SetStoredText store, instanceIndex, nm
    Err.Clear
End Sub

Function ResolveBodyByName(nm)
    On Error Resume Next
    Set ResolveBodyByName = Nothing
    If gPart Is Nothing Then Exit Function
    Set ResolveBodyByName = gPart.Bodies.Item(nm)
    Err.Clear
End Function

Function GetStoredText(store, instanceIndex)
    On Error Resume Next
    GetStoredText = ""
    If store Is Nothing Then Exit Function
    If store.Exists(StoredKey(instanceIndex)) Then GetStoredText = CStr(store.Item(StoredKey(instanceIndex)))
    Err.Clear
End Function

Sub SetStoredText(store, instanceIndex, text)
    On Error Resume Next
    If store Is Nothing Then Exit Sub
    If store.Exists(StoredKey(instanceIndex)) Then store.Remove StoredKey(instanceIndex)
    store.Add StoredKey(instanceIndex), text
    Err.Clear
End Sub

'------------------------------------------------------------------
' Drops the stored reference AND the displayed name for one instance,
' so the dashboard slot goes back to "not selected".
'------------------------------------------------------------------
Sub ClearStoredBody(objects, names, instanceIndex)
    On Error Resume Next
    If Not objects Is Nothing Then
        If objects.Exists(StoredKey(instanceIndex)) Then objects.Remove StoredKey(instanceIndex)
    End If
    If Not names Is Nothing Then
        If names.Exists(StoredKey(instanceIndex)) Then names.Remove StoredKey(instanceIndex)
    End If
    Err.Clear
End Sub

''')

# ------------------------------------------------- ChooseInstanceBody handler
patch('''
    On Error GoTo Failed

    EnsureBodyCollections
''', '''
    On Error Resume Next

    EnsureBodyCollections
''')
patch('''
        SendBooleanResponse rsp, "OK", instanceIndex, "", pickedName, "", "Target body stored for Instance " & InstanceTag(instanceIndex) & "."
    End If
    Exit Sub

Failed:
    SendBooleanResponse rsp, "FAILED", instanceIndex, "", "", "", "Body selection failed: " & Err.Description
End Sub
''', '''
        SendBooleanResponse rsp, "OK", instanceIndex, "", pickedName, "", "Target body stored for Instance " & InstanceTag(instanceIndex) & "."
    End If

    If Err.Number <> 0 Then
        SendBooleanResponse rsp, "FAILED", instanceIndex, "", "", "", "Body selection failed: " & Err.Description
        Err.Clear
    End If
End Sub
''')

# --------------------------------------------------- RunBooleanRemoveCore
old = text[text.index('Private Function RunBooleanRemoveCore(ByVal part1 As Object, _'):]
old = old[:old.index('End Function') + len('End Function')]
patch(old, '''
Function RunBooleanRemoveCore(part1, instanceIndex, ByRef statusText)
    On Error Resume Next

    EnsureBodyCollections

    Dim sourceBody, targetBody, resultBody
    Dim bodiesCollection, selectionObject, shapeFactoryObject
    Dim removeFeature, savedWorkObject
    Dim bodyCountBefore, bodyCountAfter
    Dim toolName, removeName, featureName, tag

    RunBooleanRemoveCore = False
    tag = InstanceTag(instanceIndex)
    Set savedWorkObject = Nothing

    Set sourceBody = GetStoredObject(gCopyBodyObjects, instanceIndex)
    Set targetBody = GetStoredObject(gTargetBodyObjects, instanceIndex)

    If sourceBody Is Nothing Then
        statusText = "No copy body is stored for Instance " & tag & ". Select the copy body first."
        Exit Function
    End If

    If targetBody Is Nothing Then
        statusText = "No target body is stored for Instance " & tag & ". Select the target body first."
        Exit Function
    End If

    If AreSameCATIAObject(part1, sourceBody, targetBody) Then
        statusText = "Copy body and target body cannot be the same body (Instance " & tag & ")."
        Exit Function
    End If

    If Not IsBodyObjectUsable(part1, sourceBody) Then
        ClearStoredBody gCopyBodyObjects, gCopyBodyNames, instanceIndex
        statusText = "The stored copy body of Instance " & tag & " is no longer available. Select the copy body again."
        Exit Function
    End If

    If Not IsBodyObjectUsable(part1, targetBody) Then
        ClearStoredBody gTargetBodyObjects, gTargetBodyNames, instanceIndex
        statusText = "The stored target body of Instance " & tag & " is no longer available. Select the target body again."
        Exit Function
    End If

    Err.Clear
    Set bodiesCollection = part1.Bodies
    Set selectionObject = CATIA.ActiveDocument.Selection
    bodyCountBefore = bodiesCollection.Count
    If Err.Number <> 0 Then
        statusText = "Boolean Remove failed for Instance " & tag & ": " & Err.Description
        Err.Clear
        Exit Function
    End If

    ' ----- copy the tool body -----
    selectionObject.Clear
    selectionObject.Add sourceBody
    selectionObject.Copy

    ' ----- paste it into the target as a standalone result -----
    selectionObject.Clear
    selectionObject.Add targetBody
    If Not PasteBodyAsResult(selectionObject) Then
        statusText = "Boolean Remove failed for Instance " & tag & ": CATIA could not paste the selected copy body as a result."
        RestoreWorkObject part1, savedWorkObject
        Err.Clear
        Exit Function
    End If

    part1.Update
    Err.Clear
    bodyCountAfter = bodiesCollection.Count
    If bodyCountAfter <= bodyCountBefore Then
        statusText = "Boolean Remove failed for Instance " & tag & ": no new result body was created by Paste Special."
        RestoreWorkObject part1, savedWorkObject
        Err.Clear
        Exit Function
    End If

    Set resultBody = FindNewBodyAfterPaste(bodiesCollection, bodyCountBefore, sourceBody, targetBody, selectionObject)
    If resultBody Is Nothing Then
        statusText = "Boolean Remove failed for Instance " & tag & ": the pasted result body could not be identified."
        RestoreWorkObject part1, savedWorkObject
        Err.Clear
        Exit Function
    End If

    ' ----- name the tool body, keep the CATIA name if renaming fails -----
    toolName = GetUniqueBodyName(bodiesCollection, "REMOVE_TOOL_I" & tag)
    Err.Clear
    resultBody.Name = toolName
    If Err.Number <> 0 Then
        Err.Clear
        toolName = CStr(resultBody.Name)
        Err.Clear
    End If

    ' ----- cut it out of the target body -----
    Set savedWorkObject = part1.InWorkObject
    Err.Clear
    part1.InWorkObject = targetBody

    Set shapeFactoryObject = part1.ShapeFactory
    Set removeFeature = shapeFactoryObject.AddNewRemove(resultBody)
    If Err.Number <> 0 Or removeFeature Is Nothing Then
        statusText = "Boolean Remove failed for Instance " & tag & ": " & Err.Description
        Err.Clear
        RestoreWorkObject part1, savedWorkObject
        CATIA.ActiveDocument.Selection.Clear
        Err.Clear
        Exit Function
    End If

    removeName = GetUniqueFeatureName(targetBody, "Remove_I" & tag)
    featureName = removeName
    Err.Clear
    removeFeature.Name = removeName
    If Err.Number <> 0 Then
        Err.Clear
        featureName = "Remove_I" & tag & "_" & StampText("hhnnss")
        removeFeature.Name = featureName
        Err.Clear
    End If

    ' ----- leave the part the way it was found -----
    RestoreWorkObject part1, savedWorkObject

    part1.Update
    Err.Clear

    selectionObject.Clear
    selectionObject.Add removeFeature
    Err.Clear

    statusText = "Boolean Remove completed for Instance " & tag & _
                 ". Source: " & CStr(sourceBody.Name) & _
                 " | Target: " & CStr(targetBody.Name) & _
                 " | Feature: " & featureName
    Err.Clear
    RunBooleanRemoveCore = True
End Function

'------------------------------------------------------------------
' Puts the in-work object back the way it was found, whatever
' happened during the Boolean Remove.
'------------------------------------------------------------------
Sub RestoreWorkObject(part1, savedWorkObject)
    On Error Resume Next
    If Not savedWorkObject Is Nothing Then part1.InWorkObject = savedWorkObject
    Err.Clear
End Sub
''')

# --------------------------------------------------- SendBooleanResponse
patch('''
    Dim f As Integer
    DeleteFile path
    f = FreeFile
    Open path For Output As #f
    Print #f, "BSTATUS=" & statusText
    Print #f, "BINST=" & CStr(instanceIndex)
    Print #f, "BCOPY=" & copyName
    Print #f, "BTARGET=" & targetName
    Print #f, "BFEATURE=" & featureName
    Print #f, "BMSG=" & messageText
    Close #f
End Sub
''', '''
    On Error Resume Next
    Dim f
    DeleteFile path
    Set f = NewWriter(path)
    If f Is Nothing Then Exit Sub
    f.WriteLine "BSTATUS=" & statusText
    f.WriteLine "BINST=" & CStr(instanceIndex)
    f.WriteLine "BCOPY=" & copyName
    f.WriteLine "BTARGET=" & targetName
    f.WriteLine "BFEATURE=" & featureName
    f.WriteLine "BMSG=" & messageText
    f.Close
    Err.Clear
End Sub
''')

# ---------------------------------------------------------- ReleaseTopmost
old = text[text.index("Private Sub ReleaseTopmost(ByVal title As String)"):]
old = old[:old.index('End Sub') + len('End Sub')]
patch(old, '''
Sub ReleaseTopmost(title)
    On Error Resume Next
    ' VBScript cannot call SetWindowPos, so the CATIA window is pulled to
    ' the front instead: the user can reach the graphics area for the
    ' native selection prompt, and MakeTopmost brings the HTA back after.
    gShell.AppActivate "CATIA"
    SleepMs 120
    Err.Clear
End Sub
''')

# ------------------------------------------------------------- MakeTopmost
old = text[text.index("Private Sub MakeTopmost(ByVal title As String)"):]
old = old[:old.index('End Sub') + len('End Sub')]
patch(old, '''
Sub MakeTopmost(title)
    On Error Resume Next
    Dim index
    For index = 1 To 40
        If gShell.AppActivate(title) Then
            Err.Clear
            Exit Sub
        End If
        SleepMs 120
    Next
    Err.Clear
End Sub
''')

# ----------------------------------------------------------- ReadInstance
patch('''
Private Function ReadInstance(ByVal data As String) As Long
    Dim text As String
    text = ReadKey(data, "INSTANCE")
    If IsNumeric(text) Then ReadInstance = CLng(text)
    If ReadInstance < 1 Then ReadInstance = 1
End Function
''', '''
Function ReadInstance(data)
    On Error Resume Next
    Dim text, value
    value = 0
    text = ReadKey(data, "INSTANCE")
    If IsNumeric(text) Then value = CLng(text)
    If value < 1 Then value = 1
    ReadInstance = value
    Err.Clear
End Function
''')

# ------------------------------------------------------------ UpdateValues
old = text[text.index('Private Sub UpdateValues(ByVal part As Object, ByVal data As String, ByVal rsp As String, ByVal instanceIndex As Long)'):]
old = old[:old.index('End Sub') + len('End Sub')]
patch(old, '''
Sub UpdateValues(part, data, rsp, instanceIndex)
    On Error Resume Next
    Dim undercutSet, headSet
    Dim a, b, c, d, e
    Dim problem

    problem = ""
    Set undercutSet = GetUndercutSet(part, instanceIndex)
    Set headSet = GetHeadSet(part, instanceIndex)

    ' Templates do not all group the parameters the same way (some keep
    ' HAS_EXTERNAL_FACE next to UNDERCUT_LENGTH), so every parameter is
    ' resolved in its expected set first and then anywhere in the tree.
    Set a = FindInstanceParam(part, undercutSet, headSet, p1, instanceIndex)
    Set b = FindInstanceParam(part, undercutSet, headSet, p2, instanceIndex)
    Set c = FindInstanceParam(part, undercutSet, headSet, P3, instanceIndex)
    Set d = FindInstanceParam(part, undercutSet, headSet, P4, instanceIndex)
    Set e = FindInstanceParam(part, headSet, undercutSet, P5, instanceIndex)

    problem = MissingList(part, a, b, c, d, e)

    If Len(problem) > 0 Then
        MsgBox "Parameter update failed." & vbCrLf & problem, vbCritical, APP_TITLE
        Err.Clear
        Exit Sub
    End If

    Err.Clear
    CATIA.RefreshDisplay = False
    problem = SetDimension(a, ReadKey(data, p1), "mm")
    If Len(problem) = 0 Then problem = SetDimension(b, ReadKey(data, p2), "deg")
    If Len(problem) = 0 Then problem = SetDimension(c, ReadKey(data, P3), "deg")
    Dim wantedBool
    wantedBool = (LCase(Trim(ReadKey(data, P4))) = "true")
    If Len(problem) = 0 Then problem = SetBooleanValue(part, d, ReadKey(data, P4))
    If Len(problem) = 0 Then problem = SetDimension(e, ReadKey(data, P5), "mm")

    If Len(problem) = 0 Then
        part.Update
        If Err.Number <> 0 Then
            problem = Err.Description
            Err.Clear
        End If
    End If

    ' The update itself can revert a Boolean that is driven by a rule or a
    ' formula: check the value that really survived and say so.
    If Len(problem) = 0 Then
        If BoolValueOf(d) <> wantedBool Then
            problem = "CATIA reset " & P4 & " to " & LCase(CStr(BoolValueOf(d))) & _
                      " during the update. It is driven by a formula, a rule or a design table."
        End If
    End If

    CATIA.RefreshDisplay = True
    Err.Clear

    If Len(problem) > 0 Then
        MsgBox "Parameter update failed." & vbCrLf & problem, vbCritical, APP_TITLE
        ' Re-send the real values so the dashboard never shows a state the
        ' CATPart does not have (a Boolean that CATIA refused, for example).
        SendValues part, rsp, instanceIndex
        Exit Sub
    End If

    SendValues part, rsp, instanceIndex
End Sub
''')

# ------------------------------------------- SetDimension / SetBooleanValue
old = text[text.index('Private Sub SetDimension(ByVal parameter As Object, ByVal rawValue As String, ByVal unitName As String)'):]
old = old[:old.index('Private Sub SendValues')]
patch(old, '''
'------------------------------------------------------------------
' Both setters return "" on success, or the reason of the failure -
' VBScript has no On Error GoTo, so the caller checks the text.
'------------------------------------------------------------------
Function SetDimension(parameter, rawValue, unitName)
    On Error Resume Next
    SetDimension = ""
    If Len(Trim(rawValue)) = 0 Then
        SetDimension = "A dashboard value is empty."
        Exit Function
    End If
    Err.Clear
    parameter.ValuateFromString Replace(Trim(rawValue), ",", ".") & unitName
    If Err.Number <> 0 Then
        SetDimension = Err.Description
        Err.Clear
    End If
End Function

'------------------------------------------------------------------
' Writing a CATIA Boolean parameter is not a single call that always
' works: the property assignment can be silently ignored, and a
' formula driving the parameter always wins. Every attempt is
' therefore verified by reading the value back.
'------------------------------------------------------------------
Function SetBooleanValue(part, parameter, rawValue)
    On Error Resume Next
    Dim text, wanted, message, relation, attempt

    SetBooleanValue = ""
    If parameter Is Nothing Then
        SetBooleanValue = P4 & " was not found on this instance."
        Exit Function
    End If

    text = LCase(Trim(rawValue))
    If text = "true" Then
        wanted = True
    ElseIf text = "false" Then
        wanted = False
    Else
        SetBooleanValue = "Invalid Boolean value."
        Exit Function
    End If

    If BoolValueOf(parameter) = wanted Then Exit Function      ' nothing to do

    ' A formula on the parameter cannot be overridden by a value.
    Set relation = Nothing
    Err.Clear
    Set relation = GetFormulaFor(part.Relations, parameter)
    Err.Clear
    If Not relation Is Nothing Then
        SetBooleanValue = P4 & " is driven by the formula """ & CStr(relation.Name) & _
                          """ - delete or deactivate that formula to steer it from the dashboard."
        Exit Function
    End If

    ' 1) the normal way
    Err.Clear
    parameter.Value = wanted
    message = Err.Description
    Err.Clear
    If BoolValueOf(parameter) = wanted Then Exit Function

    ' 2) string valuation, in the spellings CATIA accepts
    For attempt = 1 To 3
        Err.Clear
        If attempt = 1 Then parameter.ValuateFromString text
        If attempt = 2 Then parameter.ValuateFromString UCase(Left(text, 1)) & Mid(text, 2)
        If attempt = 3 Then
            If wanted Then
                parameter.ValuateFromString "1"
            Else
                parameter.ValuateFromString "0"
            End If
        End If
        If Len(Err.Description) > 0 Then message = Err.Description
        Err.Clear
        If BoolValueOf(parameter) = wanted Then Exit Function
    Next

    SetBooleanValue = P4 & " could not be set to " & text & _
                      " (CATIA keeps " & LCase(CStr(BoolValueOf(parameter))) & ")."
    If Len(message) > 0 Then SetBooleanValue = SetBooleanValue & " " & message
End Function

'------------------------------------------------------------------
' Reads a CATIA Boolean parameter whatever its flavour.
'------------------------------------------------------------------
Function BoolValueOf(parameter)
    On Error Resume Next
    Dim v, s
    BoolValueOf = False
    If parameter Is Nothing Then Exit Function

    Err.Clear
    v = parameter.Value
    If Err.Number = 0 Then
        Err.Clear
        BoolValueOf = CBool(v)
        If Err.Number = 0 Then Exit Function
    End If

    Err.Clear
    s = LCase(Trim(CStr(parameter.ValueAsString)))
    Err.Clear
    BoolValueOf = (s = "true" Or s = "1" Or s = "yes")
End Function

''')

# ----------------------------------------------------------- GetDraftDisplay
patch('''
Private Sub GetDraftDisplay(ByVal part As Object, ByVal headSet As Object, ByRef draftText As String, ByRef draftDiag As String)
    On Error GoTo Failed
''', '''
Sub GetDraftDisplay(part, headSet, ByRef draftText, ByRef draftDiag)
    On Error Resume Next
''')
patch('''
        draftDiag = "draft from parameter"
        Exit Sub
    End If
    Exit Sub

Failed:
    draftText = "-"
    draftDiag = "draft not available"
End Sub
''', '''
        draftDiag = "draft from parameter"
        Exit Sub
    End If

    If Err.Number <> 0 Then
        Err.Clear
        draftText = "-"
        draftDiag = "draft not available"
    End If
End Sub
''')

# ------------------------------------------------------- BooleanText
patch("""
Private Function BooleanText(ByVal parameter As Object) As String
    On Error Resume Next
    If CBool(parameter.value) Then
        BooleanText = "true"
    Else
        BooleanText = "false"
    End If
    If Err.Number <> 0 Then
        Err.Clear
        BooleanText = LCase$(Trim$(CStr(parameter.ValueAsString)))
    End If
    On Error GoTo 0
End Function
""", """
Function BooleanText(parameter)
    On Error Resume Next
    If BoolValueOf(parameter) Then
        BooleanText = "true"
    Else
        BooleanText = "false"
    End If
    Err.Clear
End Function
""")

# ------------------------------------------------- Collections -> Dictionary
patch('''
Private Function GetUndercutSets(ByVal part As Object) As Collection
    Dim result As New Collection
    CollectUndercutSets part.parameters.RootParameterSet, result
    Set GetUndercutSets = result
End Function
''', '''
Function GetUndercutSets(part)
    On Error Resume Next
    Dim result
    Set result = NewStore()
    CollectUndercutSets part.Parameters.RootParameterSet, result
    Set GetUndercutSets = result
    Err.Clear
End Function
''')
patch('''
Private Function GetHeadSets(ByVal part As Object) As Collection
    Dim result As New Collection
    CollectHeadSets part.parameters.RootParameterSet, result
    Set GetHeadSets = result
End Function
''', '''
Function GetHeadSets(part)
    On Error Resume Next
    Dim result
    Set result = NewStore()
    CollectHeadSets part.Parameters.RootParameterSet, result
    Set GetHeadSets = result
    Err.Clear
End Function
''')
patch('''
    Set undercutSet = GetUndercutSet(part, instanceIndex)
    Set headSet = GetHeadSet(part, instanceIndex)
    If undercutSet Is Nothing Or headSet Is Nothing Then
        SendResponse rsp, "NOT_FOUND", "", "", "", "", "", "", "", ""
        Exit Sub
    End If

    Set a = FindDirect(undercutSet, p1)
    Set b = FindDirect(undercutSet, p2)
    Set c = FindDirect(undercutSet, P3)
    Set d = FindDirect(undercutSet, P4)
    Set e = FindDirect(headSet, P5)
    If a Is Nothing Or b Is Nothing Or c Is Nothing Then
        SendResponse rsp, "INCOMPLETE", "", "", "", "", "", "", "", ""
        Exit Sub
    End If
''', '''
    Set undercutSet = GetUndercutSet(part, instanceIndex)
    Set headSet = GetHeadSet(part, instanceIndex)

    Set a = FindInstanceParam(part, undercutSet, headSet, p1, instanceIndex)
    Set b = FindInstanceParam(part, undercutSet, headSet, p2, instanceIndex)
    Set c = FindInstanceParam(part, undercutSet, headSet, P3, instanceIndex)
    Set d = FindInstanceParam(part, undercutSet, headSet, P4, instanceIndex)
    Set e = FindInstanceParam(part, headSet, undercutSet, P5, instanceIndex)
    If a Is Nothing Or b Is Nothing Or c Is Nothing Then
        SendResponse rsp, "INCOMPLETE", "", "", "", "", "", "", "", ""
        Exit Sub
    End If
''')

patch('''        If IsUndercutSet(childSet) Then result.Add childSet''',
      '''        If IsUndercutSet(childSet) Then result.Add result.Count + 1, childSet''')
patch('''        If Not FindDirect(childSet, P5) Is Nothing Then result.Add childSet''',
      '''        If Not FindDirect(childSet, P5) Is Nothing Then result.Add result.Count + 1, childSet''')

patch('''
Private Function InstanceCount(ByVal part As Object) As Long
    Dim undercutSets As Collection, headSets As Collection
    Set undercutSets = GetUndercutSets(part)
    Set headSets = GetHeadSets(part)
    InstanceCount = undercutSets.count
    If headSets.count < InstanceCount Then InstanceCount = headSets.count
End Function
''', '''
Function InstanceCount(part)
    On Error Resume Next
    Dim undercutSets, headSets, value
    Set undercutSets = GetUndercutSets(part)
    Set headSets = GetHeadSets(part)
    value = undercutSets.Count
    If headSets.Count < value Then value = headSets.Count
    InstanceCount = value
    Err.Clear
End Function
''')

# ------------------------------------------------------------- SendResponse
patch('''
    Dim f As Integer
    DeleteFile path
    f = FreeFile
    Open path For Output As #f
    Print #f, "STATUS=" & Status
    Print #f, "V1=" & v1
    Print #f, "V2=" & v2
    Print #f, "V3=" & v3
    Print #f, "V4=" & v4
    Print #f, "V5=" & v5
    Print #f, "V6=" & v6
    Print #f, "V7=" & v7
    Print #f, "DIAG=" & diag
    Close #f
End Sub
''', '''
    On Error Resume Next
    Dim f
    DeleteFile path
    Set f = NewWriter(path)
    If f Is Nothing Then Exit Sub
    f.WriteLine "STATUS=" & Status
    f.WriteLine "V1=" & v1
    f.WriteLine "V2=" & v2
    f.WriteLine "V3=" & v3
    f.WriteLine "V4=" & v4
    f.WriteLine "V5=" & v5
    f.WriteLine "V6=" & v6
    f.WriteLine "V7=" & v7
    f.WriteLine "DIAG=" & diag
    f.Close
    Err.Clear
End Sub
''')

# -------------------------------------------------------------- WaitCommand
old = text[text.index('Private Function WaitCommand(ByVal path As String, ByVal seconds As Long) As String'):]
old = old[:old.index('End Function') + len('End Function')]
patch(old, '''
Function WaitCommand(path, seconds)
    On Error Resume Next
    Dim started
    WaitCommand = ""
    started = Now
    Do
        If FileExists(path) Then
            SleepMs 90
            If FileSize(path) > 0 Then
                WaitCommand = ReadAll(path)
                Err.Clear
                Exit Function
            End If
        End If
        If DateDiff("s", started, Now) >= seconds Then
            Err.Clear
            Exit Function
        End If
        SleepMs 200
    Loop
End Function
''')

# ------------------------------------------- file helpers at the bottom
old = text[text.index('Private Function FileExists(ByVal path As String) As Boolean'):]
old = old[:old.index('Private Function QuoteText')]
patch(old, '''
Function FileExists(path)
    On Error Resume Next
    FileExists = False
    If Len(path) = 0 Then Exit Function
    FileExists = gFso.FileExists(path)
    Err.Clear
End Function

Function FileSize(path)
    On Error Resume Next
    FileSize = 0
    If Not gFso.FileExists(path) Then Exit Function
    FileSize = gFso.GetFile(path).Size
    Err.Clear
End Function

Sub DeleteFile(path)
    On Error Resume Next
    If Len(path) > 0 Then
        If gFso.FileExists(path) Then gFso.DeleteFile path, True
    End If
    Err.Clear
End Sub

Function ReadAll(path)
    On Error Resume Next
    Dim stream
    ReadAll = ""
    If Not gFso.FileExists(path) Then Exit Function
    Set stream = gFso.OpenTextFile(path, 1, False)
    If stream Is Nothing Then Exit Function
    If Not stream.AtEndOfStream Then ReadAll = stream.ReadAll
    stream.Close
    Err.Clear
End Function

Function NewWriter(path)
    On Error Resume Next
    Set NewWriter = Nothing
    Set NewWriter = gFso.CreateTextFile(path, True, False)
    Err.Clear
End Function

Sub W(f, text)
    On Error Resume Next
    f.WriteLine text
End Sub

''')

# ------------------------------------------------------------- PauseMs
old = text[text.index('Private Sub PauseMs(ByVal milliseconds As Long)'):]
old = old[:old.index('End Sub') + len('End Sub')]
patch(old, '''
Sub PauseMs(milliseconds)
    SleepMs milliseconds
End Sub
''')



# ======================================================================
# 1) protect the hand written blocks
# ======================================================================
for i, (old, new) in enumerate(PATCHES):
    old_n = old.replace('\r\n', '\n')
    if old_n not in text:
        raise SystemExit('PATCH %d not found:\n%s' % (i, old_n[:300]))
    text = text.replace(old_n, '\x02%d\x02' % i, 1)


# ======================================================================
# 2) mechanical VBA -> VBScript pass on everything that is left
# ======================================================================
LIT = re.compile(r'"(?:[^"]|"")*"')


def split_code_comment(line):
    in_str = False
    for i, ch in enumerate(line):
        if ch == '"':
            in_str = not in_str
        elif ch == "'" and not in_str:
            return line[:i], line[i:]
    return line, ''


def transform_code(code):
    lits = []

    def keep(m):
        lits.append(m.group(0))
        return '\x00%d\x00' % (len(lits) - 1)

    code = LIT.sub(keep, code)

    code = re.sub(r'\bEnviron\$?\(\s*\x00(\d+)\x00\s*\)', 'TempDir()', code)
    code = re.sub(r'\bFormat\$?\(\s*Now\s*,\s*\x00(\d+)\x00\s*\)',
                  lambda m: 'StampText(%s)' % lits[int(m.group(1))], code)
    code = re.sub(r'\bFormat\$?\(\s*([A-Za-z_]\w*)\s*,\s*\x00\d+\x00\s*\)',
                  lambda m: 'Pad3(%s)' % m.group(1), code)
    code = re.sub(r'\bFileLen\(', 'FileSize(', code)

    code = re.sub(r'^(\s*)(?:Private|Public)\s+(Sub|Function|Const|Dim)\b', r'\1\2', code)
    code = re.sub(r'^(\s*)(?:Private|Public)\s+(g[A-Z]\w*)', r'\1Dim \2', code)
    code = re.sub(r'\bByVal\s+', '', code)
    code = re.sub(r'\s+As\s+New\s+Collection\b', '', code)
    code = re.sub(r'\s+As\s+[A-Za-z_][\w.]*', '', code)
    code = re.sub(r'(\d)#', r'\1.0', code)
    code = re.sub(r'\b(Trim|UCase|LCase|Left|Right|Mid|Chr|Str|CStr|Space|String|Dir)\$', r'\1', code)
    code = re.sub(r'^(\s*)Next\s+[A-Za-z_]\w*\s*$', r'\1Next', code)
    code = re.sub(r'Err\.Raise\s+([^,]+),\s*,', r'Err.Raise \1, "LifterStudio",', code)
    code = code.replace('vbNullString', '""')
    code = re.sub(r'\bDoEvents\b', '', code)
    code = re.sub(r'On Error GoTo 0', 'Err.Clear', code)
    code = re.sub(r'On Error GoTo Failed', 'Err.Clear', code)

    code = code.replace('\x01', '')
    code = re.sub(r'\x00(\d+)\x00', lambda m: lits[int(m.group(1))], code)
    return code


lines = []
for line in text.split('\n'):
    code, comment = split_code_comment(line)
    lines.append(transform_code(code) + comment)
text = '\n'.join(lines)

# every procedure gets "On Error Resume Next": VBScript has no
# procedure level error handler, so the macro must never hard-crash.
out = []
pending = None
for line in text.split('\n'):
    out.append(line)
    if pending is not None:
        if not line.rstrip().endswith('_'):
            out.append(pending + '    On Error Resume Next')
            pending = None
        continue
    m = re.match(r'^(\s*)(Sub|Function)\s+\w+\s*\(', line)
    if m and '\x02' not in line:
        if line.rstrip().endswith('_'):
            pending = m.group(1)      # multi line signature
        else:
            out.append(m.group(1) + '    On Error Resume Next')
text = '\n'.join(out)

# ======================================================================
# 3) put the hand written blocks back + append the runtime library
# ======================================================================
for i, (old, new) in enumerate(PATCHES):
    text = text.replace('\x02%d\x02' % i, new)

RUNTIME = '''

'==================================================================
' VBScript runtime helpers
'------------------------------------------------------------------
' Replacements for the VBA intrinsics that VBScript does not have:
' Environ$, Format$, Shell, Sleep/DoEvents and the Open/Print #
' file statements.
'==================================================================

Function TempDir()
    On Error Resume Next
    Dim d
    d = gShell.ExpandEnvironmentStrings("%TEMP%")
    If Len(d) = 0 Or InStr(d, "%") > 0 Then d = gFso.GetSpecialFolder(2).Path
    If Right(d, 1) = "\\" Then d = Left(d, Len(d) - 1)
    TempDir = d
    Err.Clear
End Function

Function Pad2(n)
    Pad2 = Right("0" & CStr(n), 2)
End Function

Function Pad3(n)
    Pad3 = Right("00" & CStr(n), 3)
End Function

'------------------------------------------------------------------
' Format$(Now, "yyyymmdd_hhnnss") / Format$(Now, "hhnnss")
'------------------------------------------------------------------
Function StampText(pattern)
    On Error Resume Next
    Dim d, timePart
    d = Now
    timePart = Pad2(Hour(d)) & Pad2(Minute(d)) & Pad2(Second(d))
    If InStr(1, pattern, "y", 1) > 0 Then
        StampText = CStr(Year(d)) & Pad2(Month(d)) & Pad2(Day(d)) & "_" & timePart
    Else
        StampText = timePart
    End If
    Err.Clear
End Function

'------------------------------------------------------------------
' Shell x, vbNormalFocus
'------------------------------------------------------------------
Sub ShellRun(commandLine)
    On Error Resume Next
    gShell.Run commandLine, 1, False
    Err.Clear
End Sub

'------------------------------------------------------------------
' Sleep without DoEvents: a blocking ping for the long waits (no CPU
' burn while the HTA dashboard is used), a Timer loop for short ones.
'------------------------------------------------------------------
'==================================================================
' Command server - driven by the PW-User application
'------------------------------------------------------------------
' IMPORTANT: VBScript has no DoEvents, so a waiting loop inside this
' script would freeze CATIA for as long as the dashboard is open.
' The application therefore owns the loop: it launches the HTA, polls
' the command file and calls RunCommand() once per user action. Each
' call returns immediately, so CATIA stays fully interactive.
'==================================================================

Dim gPart, gDoc

'------------------------------------------------------------------
' Human readable list of the parameters that could not be resolved,
' with the number of candidates found in the whole tree - so the
' message says WHAT is missing instead of "one of these two".
'------------------------------------------------------------------
Function MissingList(part, a, b, c, d, e)
    On Error Resume Next
    Dim missing
    missing = ""
    If a Is Nothing Then missing = AppendMissing(missing, part, p1)
    If b Is Nothing Then missing = AppendMissing(missing, part, p2)
    If c Is Nothing Then missing = AppendMissing(missing, part, P3)
    If d Is Nothing Then missing = AppendMissing(missing, part, P4)
    If e Is Nothing Then missing = AppendMissing(missing, part, P5)
    If Len(missing) > 0 Then
        MissingList = "These parameters were not found on instance: " & missing & _
                      vbCrLf & "Right-click the Parameters icon of the PW toolbar to dump the parameter tree."
    Else
        MissingList = ""
    End If
    Err.Clear
End Function

Function AppendMissing(current, part, wantedName)
    On Error Resume Next
    Dim all, text
    Set all = NewStore()
    CollectNamedParams part.Parameters.RootParameterSet, wantedName, all
    text = wantedName & " (" & CStr(all.Count) & " candidate(s) in the tree)"
    If Len(current) = 0 Then
        AppendMissing = text
    Else
        AppendMissing = current & ", " & text
    End If
    Err.Clear
End Function

'------------------------------------------------------------------
' Diagnostic dump: the whole parameter tree + what the dashboard
' detects, written next to the session files and opened by the app.
'------------------------------------------------------------------
Function DumpParameterTree()
    On Error Resume Next
    Dim doc, part, path, f, sets, i

    DumpParameterTree = "ERROR:no CATPart is active"
    If CATIA.Documents.Count = 0 Then Exit Function
    If TypeName(CATIA.ActiveDocument) <> "PartDocument" Then Exit Function

    Set doc = CATIA.ActiveDocument
    Set part = doc.Part
    Set gPart = part
    Set gDoc = doc

    path = SessionDir() & "\\parameter-tree.txt"
    DeleteFile path
    Set f = NewWriter(path)
    If f Is Nothing Then
        DumpParameterTree = "ERROR:the report file could not be written"
        Exit Function
    End If

    f.WriteLine "PW-User - lifter parameter diagnostic"
    f.WriteLine "Document : " & doc.Name
    f.WriteLine "Generated: " & CStr(Now)
    f.WriteLine ""
    f.WriteLine "=========== PARAMETER TREE ==========="
    DumpSet f, part.Parameters.RootParameterSet, ""

    f.WriteLine ""
    f.WriteLine "=========== WHAT THE DASHBOARD DETECTS ==========="
    Set sets = GetUndercutSets(part)
    f.WriteLine "UNDERCUT sets (need " & p1 & " + " & p2 & " + " & P3 & " + " & P4 & "): " & CStr(sets.Count)
    For i = 1 To sets.Count
        f.WriteLine "   " & CStr(i) & ") " & CStr(sets.Item(i).Name)
    Next
    Set sets = GetHeadSets(part)
    f.WriteLine "LIFTER_HEAD sets (need " & P5 & "): " & CStr(sets.Count)
    For i = 1 To sets.Count
        f.WriteLine "   " & CStr(i) & ") " & CStr(sets.Item(i).Name)
    Next
    f.WriteLine "Instances shown by the dashboard: " & CStr(InstanceCount(part))

    f.WriteLine ""
    f.WriteLine "=========== PARAMETER SEARCH ==========="
    DumpSearch f, part, p1
    DumpSearch f, part, p2
    DumpSearch f, part, P3
    DumpSearch f, part, P4
    DumpSearch f, part, P5
    DumpSearch f, part, P_STROKE
    DumpSearch f, part, P_DRAFT

    f.Close
    Err.Clear
    DumpParameterTree = "OK|" & path
End Function

Sub DumpSet(f, parentSet, indent)
    On Error Resume Next
    Dim direct, childSets, i, p, value

    f.WriteLine indent & "[SET] " & CStr(parentSet.Name)

    Set direct = Nothing
    Set direct = parentSet.DirectParameters
    Err.Clear
    If Not direct Is Nothing Then
        For i = 1 To direct.Count
            Set p = direct.Item(i)
            value = ""
            value = CStr(p.ValueAsString)
            Err.Clear
            f.WriteLine indent & "    - " & CStr(p.Name) & "  =  " & value & "   [" & TypeName(p) & "]"
        Next
    End If
    Err.Clear

    Set childSets = Nothing
    Set childSets = parentSet.ParameterSets
    Err.Clear
    If childSets Is Nothing Then Exit Sub
    For i = 1 To childSets.Count
        DumpSet f, childSets.Item(i), indent & "    "
    Next
    Err.Clear
End Sub

Sub DumpSearch(f, part, wantedName)
    On Error Resume Next
    Dim all, i
    Set all = NewStore()
    CollectNamedParams part.Parameters.RootParameterSet, wantedName, all
    f.WriteLine wantedName & " : " & CStr(all.Count) & " match(es)"
    For i = 1 To all.Count
        f.WriteLine "    " & CStr(i) & ") " & CStr(all.Item(i).Name) & "  =  " & CStr(all.Item(i).ValueAsString)
    Next
    Err.Clear
End Sub

'------------------------------------------------------------------
' Finds a parameter of one instance even when the CATPart stores it
' in another set than expected: the preferred set first, then the
' second set, then the whole parameter tree (nth match = nth
' instance). HAS_EXTERNAL_FACE, for example, lives in the UNDERCUT
' set on some templates and next to UNDERCUT_LENGTH on others.
'------------------------------------------------------------------
Function FindInstanceParam(part, primarySet, secondarySet, wantedName, instanceIndex)
    On Error Resume Next
    Dim p, all, pick
    Set FindInstanceParam = Nothing

    Set p = Nothing
    If Not primarySet Is Nothing Then Set p = FindDirect(primarySet, wantedName)
    If p Is Nothing And Not secondarySet Is Nothing Then Set p = FindDirect(secondarySet, wantedName)

    If p Is Nothing Then
        Set all = NewStore()
        CollectNamedParams part.Parameters.RootParameterSet, wantedName, all
        If all.Count > 0 Then
            pick = instanceIndex
            If pick < 1 Then pick = 1
            If pick > all.Count Then pick = all.Count
            Set p = all.Item(pick)
        End If
    End If

    Set FindInstanceParam = p
    Err.Clear
End Function

'------------------------------------------------------------------
' Every parameter of the tree whose leaf name matches, in tree order.
'------------------------------------------------------------------
Sub CollectNamedParams(parentSet, wantedName, result)
    On Error Resume Next
    Dim direct, childSets, child, i

    Set direct = Nothing
    Set direct = parentSet.DirectParameters
    Err.Clear
    If Not direct Is Nothing Then
        For i = 1 To direct.Count
            If StrComp(LeafName(CStr(direct.Item(i).Name)), wantedName, vbTextCompare) = 0 Then
                result.Add result.Count + 1, direct.Item(i)
            End If
        Next
    End If
    Err.Clear

    Set childSets = Nothing
    Set childSets = parentSet.ParameterSets
    Err.Clear
    If childSets Is Nothing Then Exit Sub
    For i = 1 To childSets.Count
        Set child = childSets.Item(i)
        CollectNamedParams child, wantedName, result
    Next
    Err.Clear
End Sub

Function SessionDir()
    On Error Resume Next
    Dim d
    d = TempDir() & "\\PWLifterSession"
    If Not gFso.FolderExists(d) Then gFso.CreateFolder d
    SessionDir = d
    Err.Clear
End Function

Function CmdPath()
    CmdPath = SessionDir() & "\\cmd.txt"
End Function

Function RspPath()
    RspPath = SessionDir() & "\\rsp.txt"
End Function

Function DashboardPath()
    DashboardPath = SessionDir() & "\\dashboard.hta"
End Function

Function StatePath()
    StatePath = SessionDir() & "\\bodies.txt"
End Function

'------------------------------------------------------------------
' Step 1: prepare the session and BUILD the dashboard, then return.
'   NOSTROKE                       STROKE_Distance must be measured
'   OK|<hta>|<linked>|<drafts>     ready, the app launches the HTA
'   ERROR:<reason>
'------------------------------------------------------------------
Function PrepareSession()
    On Error Resume Next
    PrepareSession = "ERROR:The lifter dashboard could not be prepared."

    If CATIA.Documents.Count = 0 Then
        PrepareSession = "ERROR:No document is open in CATIA - open the CATPart first."
        Exit Function
    End If
    If TypeName(CATIA.ActiveDocument) <> "PartDocument" Then
        PrepareSession = "ERROR:The active CATIA document must be a CATPart."
        Exit Function
    End If

    Set gDoc = CATIA.ActiveDocument
    Set gPart = gDoc.Part
    If Err.Number <> 0 Then
        PrepareSession = "ERROR:The CATPart is not accessible."
        Exit Function
    End If

    If Not StrokeParameterExists(gPart) Then
        PrepareSession = "NOSTROKE"
        Exit Function
    End If

    Dim linked, drafts
    linked = LinkStrokeToMainBody(gPart)
    drafts = CreateDraftParameters(gPart)

    DeleteFile CmdPath()
    DeleteFile RspPath()
    DeleteFile DashboardPath()
    Err.Clear

    BuildDashboard DashboardPath(), CmdPath(), RspPath(), gDoc.Name, gPart, APP_TITLE, linked, drafts
    If Not gFso.FileExists(DashboardPath()) Then
        PrepareSession = "ERROR:The dashboard file could not be written."
        Exit Function
    End If

    PrepareSession = "OK|" & DashboardPath() & "|" & CStr(linked) & "|" & CStr(drafts)
    Err.Clear
End Function

'------------------------------------------------------------------
' Step 2: execute exactly ONE dashboard command and return at once.
' Returns the command name (REFRESH / UPDATE / SELECTBODY / REMOVEONE
' / POWERCOPY / CLOSE), "IDLE" when there is nothing to do, or
' "ERROR:<reason>".
'------------------------------------------------------------------
Function RunCommand()
    On Error Resume Next
    RunCommand = "IDLE"

    If Not gFso.FileExists(CmdPath()) Then Exit Function
    Dim data
    data = ReadAll(CmdPath())
    DeleteFile CmdPath()
    If Len(Trim(data)) = 0 Then Exit Function

    If CATIA.Documents.Count = 0 Then
        RunCommand = "ERROR:No document is open in CATIA."
        Exit Function
    End If
    If TypeName(CATIA.ActiveDocument) <> "PartDocument" Then
        RunCommand = "ERROR:The active CATIA document must be a CATPart."
        Exit Function
    End If

    Set gDoc = CATIA.ActiveDocument
    Set gPart = gDoc.Part
    LoadBodyState

    Dim action, instanceIndex
    action = UCase(ReadKey(data, "COMMAND"))
    instanceIndex = ReadInstance(data)
    DeleteFile RspPath()

    If action = "REFRESH" Then SendValues gPart, RspPath(), instanceIndex
    If action = "UPDATE" Then UpdateValues gPart, data, RspPath(), instanceIndex
    If action = "SELECTBODY" Then ChooseInstanceBody gPart, data, RspPath(), APP_TITLE
    If action = "REMOVEONE" Then RunBooleanRemove gPart, RspPath(), instanceIndex
    If action = "POWERCOPY" Then StartPowerCopy

    SaveBodyState
    Err.Clear
    RunCommand = action
End Function

'------------------------------------------------------------------
' Boolean Remove selections survive between two commands in a file.
'------------------------------------------------------------------
Sub LoadBodyState()
    On Error Resume Next
    EnsureBodyCollections
    gCopyBodyNames.RemoveAll
    gTargetBodyNames.RemoveAll
    gCopyBodyObjects.RemoveAll
    gTargetBodyObjects.RemoveAll
    If Not gFso.FileExists(StatePath()) Then Exit Sub

    Dim lines, i, parts, idx
    lines = Split(Replace(ReadAll(StatePath()), vbCrLf, vbLf), vbLf)
    For i = 0 To UBound(lines)
        parts = Split(lines(i), "|")
        If UBound(parts) >= 2 Then
            idx = CLng(parts(1))
            If UCase(parts(0)) = "COPY" Then
                SetStoredText gCopyBodyNames, idx, parts(2)
                SetStoredText gCopyBodyObjects, idx, parts(2)
            End If
            If UCase(parts(0)) = "TARGET" Then
                SetStoredText gTargetBodyNames, idx, parts(2)
                SetStoredText gTargetBodyObjects, idx, parts(2)
            End If
        End If
    Next
    Err.Clear
End Sub

Sub SaveBodyState()
    On Error Resume Next
    Dim f, keys, i
    Set f = NewWriter(StatePath())
    If f Is Nothing Then Exit Sub
    keys = gCopyBodyNames.Keys
    For i = 0 To UBound(keys)
        f.WriteLine "COPY|" & CStr(CLng(keys(i))) & "|" & gCopyBodyNames.Item(keys(i))
    Next
    keys = gTargetBodyNames.Keys
    For i = 0 To UBound(keys)
        f.WriteLine "TARGET|" & CStr(CLng(keys(i))) & "|" & gTargetBodyNames.Item(keys(i))
    Next
    f.Close
    Err.Clear
End Sub

Sub SleepMs(milliseconds)
    On Error Resume Next
    Dim started, elapsed, seconds
    If milliseconds >= 500 Then
        seconds = Int(milliseconds / 1000) + 1
        gShell.Run "%COMSPEC% /c ping -n " & CStr(seconds + 1) & " 127.0.0.1 > nul", 0, True
        Err.Clear
        Exit Sub
    End If
    started = Timer
    Do
        elapsed = Timer - started
        If elapsed < 0 Then elapsed = elapsed + 86400
        If elapsed * 1000 >= milliseconds Then Exit Do
    Loop
    Err.Clear
End Sub
'''

text = text.rstrip() + '\n' + RUNTIME + '\n'

# tidy: collapse 3+ blank lines
text = re.sub(r'\n{4,}', '\n\n\n', text)

os.makedirs(os.path.dirname(OUT_VBS), exist_ok=True)
open(OUT_VBS, 'w', encoding='utf-8', newline='\r\n').write(text)

# ======================================================================
# 4) generate the C# file that embeds the script
# ======================================================================
escaped = text.replace('"', '""')
cs = '''// <auto-generated>
//   Generated from Scripts/LifterStudio.CATScript - do not edit by hand.
//   The VBScript below is the "CATIA Lifter Parameters" dashboard macro
//   (VBScript edition of the original CATVBA script). The WPF app writes
//   it next to the user profile and runs it INSIDE CATIA through
//   SystemService.ExecuteScript when "Use in CATIA" is clicked.
// </auto-generated>
using System;
using System.IO;
using System.Text;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>The Lifter Studio macro that runs inside CATIA.</summary>
    internal static class LifterStudioScript
    {
        /// <summary>Writes the macro to the local app data folder and returns its path.</summary>
        public static string Write()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Estichara", "MoldAutomationCatalog", "Scripts");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "LifterStudio.CATScript");
            File.WriteAllText(path, Source, new UTF8Encoding(false));
            return path;
        }

        public const string Source = @"%s";
    }
}
''' % escaped

open(OUT_CS, 'w', encoding='utf-8', newline='\r\n').write(cs)
print('wrote', OUT_VBS, len(text), 'chars')
