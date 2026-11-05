# Diagrama entidad-relación — Turning MVP

El siguiente diagrama representa el modelo persistente de la migración `InitialSqlServer`.
Los identificadores son `uniqueidentifier`; las fechas se almacenan como `datetime2` y
`ExperimentSessions.RowVersion` como `rowversion` de SQL Server.

```mermaid
erDiagram
    UserAccounts ||--o{ ExperimentSessions : owns
    UserAccounts ||--o{ SurveyResponses : submits
    ExperimentSessions ||--o| ConditionAssignments : receives
    ExperimentSessions ||--o{ ConversationTurns : contains
    ExperimentSessions ||--o{ SessionAuditEntries : audits
    ExperimentSessions ||--o{ EmotionReadings : records
    ExperimentSessions ||--o{ AvatarExpressions : produces
    ExperimentSessions ||--o{ ExperimentEvents : emits
    ExperimentSessions ||--o{ SurveyResponses : has
    ConversationTurns |o--o{ EmotionReadings : relates
    EmotionReadings ||--o{ AvatarExpressions : drives
    SurveyDefinitions ||--o{ SurveyQuestions : defines
    SurveyDefinitions ||--o{ SurveyResponses : used_by
    SurveyResponses ||--o{ SurveyAnswers : contains
    SurveyQuestions ||--o{ SurveyAnswers : answered_by

    UserAccounts {
        uniqueidentifier Id PK
        nvarchar Email UK
        nvarchar NormalizedEmail UK
        nvarchar FullName
        nvarchar PasswordHash
        nvarchar Role
        datetime2 LastLoginAt
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    ExperimentSessions {
        uniqueidentifier Id PK
        uniqueidentifier OwnerUserId FK
        nvarchar SessionCode UK
        nvarchar Condition
        nvarchar Status
        nvarchar AvatarState
        nvarchar LastDetectedEmotion
        int ConversationTurnCount
        int EmotionSampleCount
        datetime2 ActivatedAtUtc
        datetime2 ExpiresAtUtc
        datetime2 LastActivityAtUtc
        datetime2 CompletedAtUtc
        datetime2 CancelledAtUtc
        nvarchar CancellationReason
        rowversion RowVersion
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    ConditionAssignments {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK UK
        nvarchar Condition
        nvarchar Strategy
        nvarchar Reason
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    ConversationTurns {
        uniqueidentifier Id PK
        uniqueidentifier ExperimentSessionId FK
        int SequenceNumber
        nvarchar Sender
        nvarchar Message
        uniqueidentifier OriginatingTurnId
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    SessionAuditEntries {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK
        nvarchar PreviousStatus
        nvarchar NewStatus
        nvarchar ActorType
        uniqueidentifier ActorId
        nvarchar Reason
        datetime2 OccurredAtUtc
        nvarchar MetadataJson
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    EmotionReadings {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK
        uniqueidentifier ConversationTurnId FK
        nvarchar Source
        nvarchar Emotion
        float Score
        datetime2 CapturedAtUtc
        nvarchar Provider
        bit IsDegraded
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    AvatarExpressions {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK
        uniqueidentifier EmotionReadingId FK
        nvarchar ExpressionName
        float Intensity
        nvarchar ParametersJson
        bit IsFallback
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    SurveyDefinitions {
        uniqueidentifier Id PK
        nvarchar Code UK
        nvarchar Version
        nvarchar Name
        bit IsActive
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    SurveyQuestions {
        uniqueidentifier Id PK
        uniqueidentifier SurveyDefinitionId FK
        nvarchar Code
        nvarchar Text
        nvarchar Type
        bit Required
        int Order
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    SurveyResponses {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK
        uniqueidentifier SurveyDefinitionId FK
        uniqueidentifier OwnerUserId FK
        datetime2 StartedAtUtc
        datetime2 SubmittedAtUtc
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    SurveyAnswers {
        uniqueidentifier Id PK
        uniqueidentifier SurveyResponseId FK
        uniqueidentifier SurveyQuestionId FK
        nvarchar Value
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }

    ExperimentEvents {
        uniqueidentifier Id PK
        uniqueidentifier SessionId FK
        nvarchar Type
        nvarchar PayloadJson
        datetime2 OccurredAtUtc
        datetime2 ExpiresAtUtc
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }
```

Restricciones relevantes:

- `SessionCode`, `NormalizedEmail` y `SurveyDefinition.Code` son únicos.
- Una sesión tiene como máximo una asignación (`ConditionAssignments.SessionId` único).
- Una sesión no puede repetir el mismo número de turno.
- Una respuesta no puede repetir la combinación sesión–encuesta.
- Una respuesta no puede repetir la combinación respuesta–pregunta.
- La eliminación de una sesión elimina sus datos dependientes; el usuario y la definición de encuesta no se eliminan en cascada.
