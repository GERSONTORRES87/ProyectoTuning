param(
    [string]$BaseUrl = 'http://localhost:5080',
    # Carpeta de la evidencia; por defecto ..\validacion\workflow junto al repositorio.
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
# Compatible con Windows PowerShell 5.1 y PowerShell 7: en 5.1 HttpClient no esta cargado por defecto.
Add-Type -AssemblyName System.Net.Http
$taskChecks = [Collections.Generic.List[string]]::new()
function Assert-Workflow([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $taskChecks.Add($Message)
}
function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', [int]$Expected = 200) {
    $taskRequest = @{ Method=$Method; Uri=($BaseUrl+$Path); TimeoutSec=30; UseBasicParsing=$true }
    # SkipHttpErrorCheck solo existe en PowerShell 7; en 5.1 el error HTTP llega como WebException (abajo).
    if ($PSVersionTable.PSVersion.Major -ge 7) { $taskRequest.SkipHttpErrorCheck = $true }
    if ($Token) { $taskRequest.Headers = @{ Authorization="Bearer $Token" } }
    if ($null -ne $Body) {
        $taskRequest.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10))
        $taskRequest.ContentType = 'application/json; charset=utf-8'
    }
    try {
        $taskResponse = Invoke-WebRequest @taskRequest
        $taskStatus = [int]$taskResponse.StatusCode
        $taskContent = $taskResponse.Content
    } catch [System.Net.WebException] {
        $taskFailure = $_.Exception.Response
        if ($null -eq $taskFailure) { throw }
        $taskStatus = [int]$taskFailure.StatusCode
        $taskReader = [IO.StreamReader]::new($taskFailure.GetResponseStream(), [Text.Encoding]::UTF8)
        try { $taskContent = $taskReader.ReadToEnd() } finally { $taskReader.Dispose() }
    }
    if ($taskStatus -ne $Expected) {
        throw "$Method $Path esperaba $Expected, recibió ${taskStatus}: $taskContent"
    }
    # En 5.1 ConvertFrom-Json entrega un arreglo como un solo objeto; pasar por una variable lo enumera igual que en 7.
    if ($taskContent) { $taskParsed = $taskContent | ConvertFrom-Json; return $taskParsed }
}
# Claves con las que el participante ve cada evento de turno: no pueden delatar quien respondio.
function Get-TurnEventShapes($Events) {
    @($Events | Where-Object { $_.type -eq 'ConversationTurnAdded' } |
        ForEach-Object { (($_.payloadJson | ConvertFrom-Json).PSObject.Properties.Name | Sort-Object) -join ',' } |
        Sort-Object -Unique)
}
function Complete-Survey($Session, [string]$Token, [string]$Guess, [string]$ResearcherToken, [string]$AdminToken) {
    $null=Invoke-Api POST "/api/sessions/$($Session.id)/complete" -Token $Token
    $taskEnded=@(Invoke-Api GET "/api/sessions/$($Session.id)/events" -Token $Token)
    Assert-Workflow ('SessionEnded' -in $taskEnded.type -and 'SurveyAvailable' -in $taskEnded.type) "El cierre ($Guess) publica SessionEnded y SurveyAvailable"
    $taskDefinition=Invoke-Api GET "/api/sessions/$($Session.id)/survey" -Token $Token
    Assert-Workflow ($taskDefinition.questions.Count -eq 4) 'Encuesta disponible con cuatro preguntas'
    $taskAnswers=@{}
    foreach ($taskQuestion in $taskDefinition.questions) {
        $taskAnswers[$taskQuestion.code] = switch ($taskQuestion.type) { 'choice' {$Guess} 'scale' {'4'} default {'Verificación funcional del experimento.'} }
    }
    $taskSubmission=@{ surveyDefinitionId=$taskDefinition.id; answers=$taskAnswers }
    # Un investigador observa la encuesta, pero no puede enviarla en nombre del dueño.
    $null=Invoke-Api GET "/api/sessions/$($Session.id)/survey" -Token $ResearcherToken
    $null=Invoke-Api POST "/api/sessions/$($Session.id)/survey/responses" $taskSubmission $ResearcherToken 404
    $null=Invoke-Api POST "/api/sessions/$($Session.id)/survey/responses" $taskSubmission $AdminToken 404
    $taskUntouched=Invoke-Api GET "/api/sessions/$($Session.id)/results" -Token $ResearcherToken
    Assert-Workflow (@($taskUntouched.survey).Count -eq 0) "Researcher y Administrator no pueden enviar la encuesta del dueño ($Guess)"
    $null=Invoke-Api POST "/api/sessions/$($Session.id)/survey/responses" $taskSubmission $Token 201
    $null=Invoke-Api POST "/api/sessions/$($Session.id)/survey/responses" $taskSubmission $Token 409
    $taskResult=Invoke-Api GET "/api/sessions/$($Session.id)/results" -Token $Token
    Assert-Workflow ($taskResult.survey[0].answers.Count -eq 4 -and $null -ne $taskResult.survey[0].submittedAtUtc) 'Respuestas persistidas mediante un único POST'
    Assert-Workflow ($taskResult.session.condition -eq 'Hidden') 'Resultado del participante conserva el cegamiento'
    Assert-Workflow (@($taskResult.emotionReadings | Where-Object { $_.provider -ne 'analysis' }).Count -eq 0 -and -not ($taskResult.degradedEvents.payloadJson -match 'Conversation|TextGeneration')) `
        "Resultados del participante ($Guess) sin proveedor ni degradación de conversación"
}

$taskSuffix=[Guid]::NewGuid().ToString('N').Substring(0,8)
$taskPassword='TurningDemo2026!'
$null=Invoke-Api POST '/api/auth/register' @{fullName='Participante Verificación';email="e2e-owner-$taskSuffix@turning.local";password=$taskPassword;role='Administrator'} -Expected 403
$taskOwner=Invoke-Api POST '/api/auth/register' @{fullName='Participante Verificación';email="e2e-owner-$taskSuffix@turning.local";password=$taskPassword}
$taskPartner=Invoke-Api POST '/api/auth/register' @{fullName='Interlocutor Verificación';email="e2e-partner-$taskSuffix@turning.local";password=$taskPassword}
$taskOtherEmail="e2e-other-$taskSuffix@turning.local"
$taskOther=Invoke-Api POST '/api/auth/register' @{fullName='Cuenta Roles';email=$taskOtherEmail;password=$taskPassword}
$taskAdmin=Invoke-Api POST '/api/auth/login' @{email='admin@turning.local';password=$taskPassword}
$taskResearcher=Invoke-Api POST '/api/auth/login' @{email='researcher@turning.local';password=$taskPassword}
Assert-Workflow ($taskOwner.user.role -eq 'Participant') 'El registro público rechaza privilegios y crea Participant'
$null=Invoke-Api POST '/api/auth/register' @{fullName='Duplicado';email="e2e-owner-$taskSuffix@turning.local";password=$taskPassword} -Expected 409
Assert-Workflow $true 'Un correo duplicado devuelve conflicto de registro'

$taskHuman=$null; $taskAi=$null
for ($taskIndex=0; $taskIndex -lt 6 -and ($null -eq $taskHuman -or $null -eq $taskAi); $taskIndex++) {
    $taskCreated=Invoke-Api POST '/api/sessions' @{} $taskOwner.accessToken 201
    Assert-Workflow ($taskCreated.condition -eq 'Hidden' -and $taskCreated.participationRole -eq 'Owner') 'Sesión creada sin imponer ni revelar condición'
    $taskInternal=Invoke-Api GET "/api/sessions/$($taskCreated.id)" -Token $taskResearcher.accessToken
    if ($taskInternal.condition -eq 'Human' -and $null -eq $taskHuman) { $taskHuman=$taskCreated }
    if ($taskInternal.condition -eq 'AI' -and $null -eq $taskAi) { $taskAi=$taskCreated }
}
Assert-Workflow ($null -ne $taskHuman -and $null -ne $taskAi) 'El balanceo permite verificar Human y AI'
$null=Invoke-Api GET "/api/sessions/$($taskHuman.id)" -Token $taskOther.accessToken -Expected 404
$null=Invoke-Api GET "/api/sessions/$($taskHuman.id)/survey" -Token $taskOwner.accessToken -Expected 400
$taskJoined=Invoke-Api POST '/api/sessions/join' @{sessionCode=$taskHuman.sessionCode} $taskPartner.accessToken
Assert-Workflow ($taskJoined.participationRole -eq 'Interlocutor') 'Una segunda cuenta se une como interlocutor por código'
# Solo el dueño inicia la sesión: ni el interlocutor ni un observador pueden abrirla con un turno.
$taskEarly=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Intento abrir la sesión'} $taskPartner.accessToken 409
Assert-Workflow ($taskEarly.error -eq 'SESSION_NOT_STARTED') 'El interlocutor no puede iniciar la sesión con su primer turno'
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Observador'} $taskResearcher.accessToken 404
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Observador'} $taskAdmin.accessToken 404
$taskStillCreated=Invoke-Api GET "/api/sessions/$($taskHuman.id)" -Token $taskOwner.accessToken
Assert-Workflow ($taskStillCreated.status -eq 'Created' -and $taskStillCreated.conversationTurnCount -eq 0) 'Los intentos rechazados no activan la sesión ni dejan turnos'
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/activate" -Token $taskPartner.accessToken -Expected 404
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/activate" -Token $taskResearcher.accessToken -Expected 404
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/activate" -Token $taskOwner.accessToken
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Hola, quisiera conversar.'} $taskOwner.accessToken
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Aquí estoy, te escucho.'} $taskPartner.accessToken
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Suplantación';sender='Interlocutor'} $taskOwner.accessToken 400
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Observador'} $taskResearcher.accessToken 404
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Observador'} $taskAdmin.accessToken 404
$taskHumanTurns=@(Invoke-Api GET "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" -Token $taskOwner.accessToken)
Assert-Workflow ($taskHumanTurns.Count -eq 2 -and $taskHumanTurns[1].sender -eq 'Interlocutor') 'Conversación Human persistida con dos identidades reales'
# El chat se actualiza sondeando con la última secuencia conocida (hallazgo 3 de Azure).
$taskPolled=@(Invoke-Api GET "/api/experiment-sessions/$($taskHuman.id)/conversation-turns?afterSequence=1" -Token $taskOwner.accessToken)
Assert-Workflow ($taskPolled.Count -eq 1 -and $taskPolled[0].sender -eq 'Interlocutor' -and $null -eq $taskPolled[0].originatingTurnId) 'El sondeo con cursor entrega el turno del interlocutor al dueño'

# Emociones desde la Web: el POST se persiste, el avatar lo refleja y el feed lo registra (hallazgos 2 y 7 de Azure).
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/emotions" @{emotion='joy';score=0.7;source='self-report'} $taskResearcher.accessToken 404
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/emotions" @{emotion='joy';score=0.7;source='self-report'} $taskAdmin.accessToken 404
$taskBefore=@(Invoke-Api GET "/api/sessions/$($taskHuman.id)/emotions" -Token $taskResearcher.accessToken)
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/emotions" @{emotion='joy';score=0.7;source='self-report'} $taskOwner.accessToken 201
$taskAfter=@(Invoke-Api GET "/api/sessions/$($taskHuman.id)/emotions" -Token $taskOwner.accessToken)
Assert-Workflow ($taskAfter.Count -eq $taskBefore.Count + 1 -and $taskAfter[-1].emotion -eq 'joy' -and $taskAfter[-1].source -eq 'self-report') 'La emoción autoinformada se persiste y los privilegiados no pueden escribirla'
$taskAvatar=Invoke-Api GET "/api/sessions/$($taskHuman.id)/avatar/current" -Token $taskOwner.accessToken
Assert-Workflow ($taskAvatar.expressionName -eq 'Joy' -and [math]::Abs($taskAvatar.intensity - 0.7) -lt 0.001 -and -not $taskAvatar.isFallback) 'El avatar refleja la última lectura medida'
$taskHumanFeed=@(Invoke-Api GET "/api/sessions/$($taskHuman.id)/events" -Token $taskOwner.accessToken)
Assert-Workflow ('EmotionReadingAdded' -in $taskHumanFeed.type) 'El feed registra la lectura emocional'

# Gestión del emparejamiento: Researcher libera y Administrator asigna sobre una sesión ajena.
$taskReleased=Invoke-Api DELETE "/api/sessions/$($taskHuman.id)/interlocutor" -Token $taskResearcher.accessToken
Assert-Workflow (-not $taskReleased.hasInterlocutor) 'Researcher puede liberar el interlocutor de una sesión ajena'
$null=Invoke-Api GET "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" -Token $taskPartner.accessToken -Expected 404
$taskReassigned=Invoke-Api POST "/api/sessions/$($taskHuman.id)/interlocutor" @{interlocutorUserId=$taskPartner.user.id} $taskAdmin.accessToken
Assert-Workflow ($taskReassigned.hasInterlocutor) 'Administrator puede asignar un interlocutor a una sesión ajena'
$null=Invoke-Api GET "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" -Token $taskPartner.accessToken
$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/interlocutor" @{interlocutorUserId=$taskPartner.user.id} $taskOwner.accessToken 403

$null=Invoke-Api POST "/api/sessions/$($taskHuman.id)/complete" -Token $taskResearcher.accessToken -Expected 404
Complete-Survey $taskHuman $taskOwner.accessToken 'Human' $taskResearcher.accessToken $taskAdmin.accessToken
$null=Invoke-Api POST "/api/experiment-sessions/$($taskHuman.id)/conversation-turns" @{message='Tardío'} $taskOwner.accessToken 409
$taskHumanShapes=@(Get-TurnEventShapes @(Invoke-Api GET "/api/sessions/$($taskHuman.id)/events" -Token $taskOwner.accessToken))

$null=Invoke-Api POST "/api/sessions/$($taskAi.id)/activate" -Token $taskOwner.accessToken
$taskHttp=[Net.Http.HttpClient]::new()
try {
    $taskHttp.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$taskOwner.accessToken)
    $taskFirstContent=[Net.Http.StringContent]::new('{"message":"Me alegra poder conversar."}',[Text.Encoding]::UTF8,'application/json')
    $taskSecondContent=[Net.Http.StringContent]::new('{"message":"Hoy estoy un poco preocupado."}',[Text.Encoding]::UTF8,'application/json')
    $taskFirst=$taskHttp.PostAsync("$BaseUrl/api/experiment-sessions/$($taskAi.id)/conversation-turns",$taskFirstContent)
    $taskSecond=$taskHttp.PostAsync("$BaseUrl/api/experiment-sessions/$($taskAi.id)/conversation-turns",$taskSecondContent)
    $taskResponses=@($taskFirst.GetAwaiter().GetResult(),$taskSecond.GetAwaiter().GetResult())
    Assert-Workflow (@($taskResponses | Where-Object { [int]$_.StatusCode -ne 200 }).Count -eq 0) 'Dos mensajes concurrentes de AI se aceptan sin errores'
} finally { $taskHttp.Dispose() }
$taskAiTurns=@(Invoke-Api GET "/api/experiment-sessions/$($taskAi.id)/conversation-turns" -Token $taskOwner.accessToken)
Assert-Workflow ($taskAiTurns.Count -eq 4 -and (($taskAiTurns.sequenceNumber -join ',') -eq '1,2,3,4')) 'AI conserva dos mensajes, dos respuestas y secuencia consecutiva'
Assert-Workflow (@($taskAiTurns | Where-Object { $null -ne $_.originatingTurnId }).Count -eq 0) 'El historial del participante no revela referencias exclusivas de AI'
$taskAiAck=Invoke-Api POST "/api/experiment-sessions/$($taskAi.id)/conversation-turns" @{message='Gracias por escucharme.'} $taskOwner.accessToken
Assert-Workflow ($taskAiAck.source -eq 'pending' -and $null -eq $taskAiAck.ai -and $null -eq $taskAiAck.interlocutorTurn -and -not $taskAiAck.degraded) 'La respuesta al participante no revela proveedor ni origen'
# Cegamiento en el feed: las respuestas de IA y los turnos humanos comparten esquema y no hay degradación de conversación.
$taskAiFeed=@(Invoke-Api GET "/api/sessions/$($taskAi.id)/events" -Token $taskOwner.accessToken)
$taskAiShapes=@(Get-TurnEventShapes $taskAiFeed)
Assert-Workflow ($taskAiShapes.Count -eq 1 -and $taskHumanShapes.Count -eq 1 -and $taskAiShapes[0] -eq $taskHumanShapes[0] -and $taskAiShapes[0] -eq 'sender,sequenceNumber,turnId') 'Los eventos de turno del participante tienen el mismo esquema en Human y AI'
Assert-Workflow (-not ($taskAiFeed.type -contains 'AiReplyGenerated') -and -not ($taskAiFeed.type -contains 'AiProviderFailover') -and -not ($taskAiFeed.payloadJson -match 'provider|model|originatingTurnId|TextGeneration|Conversation"')) 'El feed del participante no contiene eventos ni campos exclusivos de AI'
$taskAiResearcherFeed=@(Invoke-Api GET "/api/sessions/$($taskAi.id)/events" -Token $taskResearcher.accessToken)
Assert-Workflow ('AiReplyGenerated' -in $taskAiResearcherFeed.type) 'El investigador conserva el diagnóstico completo de AI'
Complete-Survey $taskAi $taskOwner.accessToken 'AI' $taskResearcher.accessToken $taskAdmin.accessToken
$taskEvents=@(Invoke-Api GET "/api/sessions/$($taskAi.id)/events" -Token $taskOwner.accessToken)
Assert-Workflow ('SessionEnded' -in $taskEvents.type -and 'SurveyAvailable' -in $taskEvents.type) 'El cierre publica eventos y disponibilidad de encuesta'
$null=Invoke-Api PUT "/api/auth/users/$($taskOther.user.id)/role" @{role='Researcher'} $taskOwner.accessToken 403
$null=Invoke-Api PUT "/api/auth/users/$($taskOther.user.id)/role" @{role='Researcher'} $taskAdmin.accessToken
$null=Invoke-Api GET '/api/auth/me' -Token $taskOther.accessToken -Expected 401
$taskChanged=Invoke-Api POST '/api/auth/login' @{email=$taskOtherEmail;password=$taskPassword}
Assert-Workflow ($taskChanged.user.role -eq 'Researcher') 'Administrador concede un rol utilizable en el siguiente login'
$null=Invoke-Api GET '/api/results?page=1&pageSize=20' -Token $taskChanged.accessToken
$null=Invoke-Api GET '/api/results?page=0&pageSize=20' -Token $taskChanged.accessToken -Expected 400
$null=Invoke-Api GET '/api/results' -Token $taskOwner.accessToken -Expected 403

$taskCancelSession=Invoke-Api POST '/api/sessions' @{} $taskOwner.accessToken 201
$null=Invoke-Api POST "/api/sessions/$($taskCancelSession.id)/cancel" @{reason='Verificación administrativa'} $taskOwner.accessToken 403
$null=Invoke-Api POST "/api/sessions/$($taskCancelSession.id)/cancel" @{reason='Verificación administrativa'} $taskResearcher.accessToken 403
$taskCancelled=Invoke-Api POST "/api/sessions/$($taskCancelSession.id)/cancel" @{reason='Verificación administrativa'} $taskAdmin.accessToken
Assert-Workflow ($taskCancelled.status -eq 'Cancelled') 'Sólo Administrator puede cancelar la sesión de otro usuario'
$taskCancelledSurvey=Invoke-Api GET "/api/sessions/$($taskCancelSession.id)/survey" -Token $taskOwner.accessToken
$taskCancelledEvents=@(Invoke-Api GET "/api/sessions/$($taskCancelSession.id)/events" -Token $taskOwner.accessToken)
Assert-Workflow ($taskCancelledSurvey.questions.Count -eq 4 -and 'SurveyAvailable' -in $taskCancelledEvents.type) 'Cancelación deja encuesta disponible y su evento persistido'

if (-not $OutputDir) { $OutputDir = Join-Path (Split-Path $PSScriptRoot -Parent) '..\validacion\workflow' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$taskChecks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDir 'http-workflow.json') -Encoding utf8
Write-Output ("Verificación HTTP correcta: {0} comprobaciones; Human y AI, roles, concurrencia, encuesta y resultados." -f $taskChecks.Count)
