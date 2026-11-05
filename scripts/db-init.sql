-- ==============================================================
-- db-init.sql — Script idempotente de inicialización SQL Server
-- Proyecto Turning — Entregable GE-02 (Gerson Torres, DBA)
-- ==============================================================
-- Qué hace: crea todas las tablas, FK, índices y constraints del
-- modelo (ver docs/2-arquitectura/modelo-datos/diagrama-er.md y docs/2-arquitectura/modelo-datos/diccionario-datos.md) si aún
-- no existen. Es seguro ejecutarlo varias veces: cada bloque valida
-- contra __EFMigrationsHistory antes de crear algo, por lo que no
-- duplica tablas ni falla si ya fue aplicado antes.
--
-- Uso:
--   sqlcmd -S (localdb)\MSSQLLocalDB -d Turning -i scripts\db-init.sql
-- o ejecutarlo desde SSMS conectado a la base de datos deseada.
--
-- Alternativa recomendada para desarrollo local (SQLite, sin este
-- script): dotnet ef database update --project src	urning.Infrastructure
-- --startup-project src	urning.API  (ver scripts/db-reset.ps1).
-- Generado desde la migración EF Core 'InitialSqlServer'.
-- ==============================================================

﻿IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [SurveyDefinitions] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Version] nvarchar(20) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_SurveyDefinitions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [UserAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(200) NOT NULL,
        [NormalizedEmail] nvarchar(200) NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [PasswordHash] nvarchar(1000) NOT NULL,
        [Role] nvarchar(50) NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_UserAccounts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [SurveyQuestions] (
        [Id] uniqueidentifier NOT NULL,
        [SurveyDefinitionId] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Text] nvarchar(1000) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [Required] bit NOT NULL,
        [Order] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_SurveyQuestions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SurveyQuestions_SurveyDefinitions_SurveyDefinitionId] FOREIGN KEY ([SurveyDefinitionId]) REFERENCES [SurveyDefinitions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [ExperimentSessions] (
        [Id] uniqueidentifier NOT NULL,
        [OwnerUserId] uniqueidentifier NOT NULL,
        [SessionCode] nvarchar(20) NOT NULL,
        [Condition] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AvatarState] nvarchar(50) NOT NULL,
        [LastDetectedEmotion] nvarchar(50) NULL,
        [ConversationTurnCount] int NOT NULL,
        [EmotionSampleCount] int NOT NULL,
        [ActivatedAtUtc] datetime2 NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [LastActivityAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [CancellationReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_ExperimentSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExperimentSessions_UserAccounts_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [ConditionAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [Condition] nvarchar(20) NOT NULL,
        [Strategy] nvarchar(50) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_ConditionAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConditionAssignments_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [ConversationTurns] (
        [Id] uniqueidentifier NOT NULL,
        [ExperimentSessionId] uniqueidentifier NOT NULL,
        [SequenceNumber] int NOT NULL,
        [Sender] nvarchar(20) NOT NULL,
        [Message] nvarchar(4000) NOT NULL,
        [OriginatingTurnId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_ConversationTurns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConversationTurns_ExperimentSessions_ExperimentSessionId] FOREIGN KEY ([ExperimentSessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [ExperimentEvents] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [Type] nvarchar(50) NOT NULL,
        [PayloadJson] nvarchar(4000) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_ExperimentEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExperimentEvents_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [SessionAuditEntries] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [PreviousStatus] nvarchar(30) NOT NULL,
        [NewStatus] nvarchar(30) NOT NULL,
        [ActorType] nvarchar(50) NOT NULL,
        [ActorId] uniqueidentifier NULL,
        [Reason] nvarchar(500) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [MetadataJson] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_SessionAuditEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SessionAuditEntries_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [SurveyResponses] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [SurveyDefinitionId] uniqueidentifier NOT NULL,
        [OwnerUserId] uniqueidentifier NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_SurveyResponses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SurveyResponses_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SurveyResponses_SurveyDefinitions_SurveyDefinitionId] FOREIGN KEY ([SurveyDefinitionId]) REFERENCES [SurveyDefinitions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SurveyResponses_UserAccounts_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [EmotionReadings] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [ConversationTurnId] uniqueidentifier NULL,
        [Source] nvarchar(30) NOT NULL,
        [Emotion] nvarchar(50) NOT NULL,
        [Score] float NOT NULL,
        [CapturedAtUtc] datetime2 NOT NULL,
        [Provider] nvarchar(100) NOT NULL,
        [IsDegraded] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_EmotionReadings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmotionReadings_ConversationTurns_ConversationTurnId] FOREIGN KEY ([ConversationTurnId]) REFERENCES [ConversationTurns] ([Id]),
        CONSTRAINT [FK_EmotionReadings_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [SurveyAnswers] (
        [Id] uniqueidentifier NOT NULL,
        [SurveyResponseId] uniqueidentifier NOT NULL,
        [SurveyQuestionId] uniqueidentifier NOT NULL,
        [Value] nvarchar(4000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_SurveyAnswers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SurveyAnswers_SurveyQuestions_SurveyQuestionId] FOREIGN KEY ([SurveyQuestionId]) REFERENCES [SurveyQuestions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SurveyAnswers_SurveyResponses_SurveyResponseId] FOREIGN KEY ([SurveyResponseId]) REFERENCES [SurveyResponses] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE TABLE [AvatarExpressions] (
        [Id] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [EmotionReadingId] uniqueidentifier NOT NULL,
        [ExpressionName] nvarchar(50) NOT NULL,
        [Intensity] float NOT NULL,
        [ParametersJson] nvarchar(2000) NOT NULL,
        [IsFallback] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_AvatarExpressions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AvatarExpressions_EmotionReadings_EmotionReadingId] FOREIGN KEY ([EmotionReadingId]) REFERENCES [EmotionReadings] ([Id]),
        CONSTRAINT [FK_AvatarExpressions_ExperimentSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ExperimentSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AvatarExpressions_EmotionReadingId] ON [AvatarExpressions] ([EmotionReadingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AvatarExpressions_SessionId_CreatedAt] ON [AvatarExpressions] ([SessionId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConditionAssignments_SessionId] ON [ConditionAssignments] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConversationTurns_ExperimentSessionId_SequenceNumber] ON [ConversationTurns] ([ExperimentSessionId], [SequenceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_EmotionReadings_ConversationTurnId] ON [EmotionReadings] ([ConversationTurnId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_EmotionReadings_SessionId_CapturedAtUtc] ON [EmotionReadings] ([SessionId], [CapturedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_ExperimentEvents_SessionId_OccurredAtUtc] ON [ExperimentEvents] ([SessionId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_ExperimentSessions_OwnerUserId_CreatedAt] ON [ExperimentSessions] ([OwnerUserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExperimentSessions_SessionCode] ON [ExperimentSessions] ([SessionCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_ExperimentSessions_Status_ActivatedAtUtc] ON [ExperimentSessions] ([Status], [ActivatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_SessionAuditEntries_SessionId_OccurredAtUtc] ON [SessionAuditEntries] ([SessionId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_SurveyAnswers_SurveyQuestionId] ON [SurveyAnswers] ([SurveyQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SurveyAnswers_SurveyResponseId_SurveyQuestionId] ON [SurveyAnswers] ([SurveyResponseId], [SurveyQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SurveyDefinitions_Code] ON [SurveyDefinitions] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SurveyQuestions_SurveyDefinitionId_Order] ON [SurveyQuestions] ([SurveyDefinitionId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_SurveyResponses_OwnerUserId] ON [SurveyResponses] ([OwnerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SurveyResponses_SessionId_SurveyDefinitionId] ON [SurveyResponses] ([SessionId], [SurveyDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_SurveyResponses_SurveyDefinitionId] ON [SurveyResponses] ([SurveyDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserAccounts_NormalizedEmail] ON [UserAccounts] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824194759_InitialSqlServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824194759_InitialSqlServer', N'10.0.0');
END;

COMMIT;
GO

