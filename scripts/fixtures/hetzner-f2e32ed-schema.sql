CREATE TABLE IF NOT EXISTS "hosts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_hosts" PRIMARY KEY AUTOINCREMENT,
    "TwitchUserId" TEXT NULL,
    "Login" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "ProfileImageUrl" TEXT NULL,
    "ChannelBotAuthorizedAtUtc" TEXT NULL,
    "ChannelBotAuthorizedScopes" TEXT NULL,
    "BotRuntimeState" INTEGER NOT NULL,
    "BotRuntimeStateChangedAtUtc" TEXT NULL,
    "EnabledFeatures" INTEGER NOT NULL DEFAULT 7,
    "TimeZoneId" TEXT NOT NULL DEFAULT 'UTC',
    "CreatedAtUtc" TEXT NOT NULL
);
CREATE TABLE sqlite_sequence(name,seq);
CREATE TABLE IF NOT EXISTS "public_chat_outbox" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_public_chat_outbox" PRIMARY KEY AUTOINCREMENT,
    "Channel" TEXT NOT NULL,
    "Message" TEXT NULL,
    "DeduplicationKey" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "ExpiresAtUtc" TEXT NOT NULL,
    "NextAttemptAtUtc" TEXT NULL,
    "Status" TEXT NOT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "SafePreSendFailureCount" INTEGER NOT NULL,
    "ClaimToken" TEXT NULL,
    "ClaimSlot" INTEGER NULL,
    "ClaimExpiresAtUtc" TEXT NULL,
    "SendStartedAtUtc" TEXT NULL,
    "CompletedAtUtc" TEXT NULL,
    "FailurePhase" TEXT NULL,
    "FailureType" TEXT NULL,
    "HttpStatusCode" INTEGER NULL,
    "RejectionCode" TEXT NULL,
    CONSTRAINT "CK_public_chat_outbox_AttemptCount" CHECK (AttemptCount >= 0),
    CONSTRAINT "CK_public_chat_outbox_Channel" CHECK (length(trim(Channel)) > 0),
    CONSTRAINT "CK_public_chat_outbox_DeduplicationKey" CHECK (DeduplicationKey IS NULL OR length(DeduplicationKey) = 64),
    CONSTRAINT "CK_public_chat_outbox_FailurePhase" CHECK (FailurePhase IS NULL OR FailurePhase IN ('Preparation', 'Send')),
    CONSTRAINT "CK_public_chat_outbox_SafePreSendFailureCount" CHECK (SafePreSendFailureCount >= 0),
    CONSTRAINT "CK_public_chat_outbox_State" CHECK ((Status = 'Pending' AND length(Message) > 0 AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND SafePreSendFailureCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'Claimed' AND length(Message) > 0 AND ClaimToken IS NOT NULL AND ClaimSlot = 1 AND ClaimExpiresAtUtc IS NOT NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND ((SafePreSendFailureCount = 0 AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (SafePreSendFailureCount > 0 AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL))) OR (Status = 'Sending' AND length(Message) > 0 AND ClaimToken IS NOT NULL AND ClaimSlot = 1 AND ClaimExpiresAtUtc IS NOT NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL AND AttemptCount > 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'SafePreSendTransient' AND length(Message) > 0 AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND SafePreSendFailureCount > 0 AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'SafePreSendExhausted' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND SafePreSendFailureCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status IN ('MissingChannel', 'MissingBot') AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND SafePreSendFailureCount = 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'Rejected' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' AND AttemptCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND (RejectionCode IS NULL OR length(RejectionCode) > 0)) OR (Status = 'Ambiguous' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' AND AttemptCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'Unexpected' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'Expired' AND Message IS NULL AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL)),
    CONSTRAINT "CK_public_chat_outbox_Status" CHECK (Status IN ('Ambiguous', 'Claimed', 'Expired', 'MissingBot', 'MissingChannel', 'Pending', 'Rejected', 'SafePreSendExhausted', 'SafePreSendTransient', 'Sending', 'Unexpected'))
);
CREATE TABLE IF NOT EXISTS "public_chat_send_receipts" (
    "OutboxMessageId" INTEGER NOT NULL CONSTRAINT "PK_public_chat_send_receipts" PRIMARY KEY,
    "AttemptedAtUtc" TEXT NOT NULL,
    "CompletedAtUtc" TEXT NULL,
    "DeliveredDeduplicationKey" TEXT NULL,
    "DeliveredAtUtc" TEXT NULL,
    CONSTRAINT "CK_public_chat_send_receipts_Delivery" CHECK ((DeliveredDeduplicationKey IS NULL AND DeliveredAtUtc IS NULL) OR (length(DeliveredDeduplicationKey) = 64 AND DeliveredAtUtc IS NOT NULL))
);
CREATE TABLE IF NOT EXISTS "site_access_entries" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_site_access_entries" PRIMARY KEY AUTOINCREMENT,
    "Login" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "CK_site_access_entries_Kind" CHECK (Kind IN ('blacklist', 'whitelist'))
);
CREATE TABLE IF NOT EXISTS "site_access_settings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_site_access_settings" PRIMARY KEY AUTOINCREMENT,
    "WhitelistEnabled" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "custom_announcement_delivery_policies" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_announcement_delivery_policies" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "PolicyType" TEXT NOT NULL,
    "RetryDelayTicks" INTEGER NULL,
    "OccurrenceLifetimeTicks" INTEGER NULL,
    CONSTRAINT "AK_custom_announcement_delivery_policies_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "CK_custom_announcement_delivery_policies_Payload" CHECK (PolicyType = 'RetryUntilExpiredThenSkip' AND RetryDelayTicks IS NOT NULL AND RetryDelayTicks > 0 AND OccurrenceLifetimeTicks IS NOT NULL AND OccurrenceLifetimeTicks <= 600000000 AND RetryDelayTicks < OccurrenceLifetimeTicks),
    CONSTRAINT "CK_custom_announcement_delivery_policies_PolicyType" CHECK (PolicyType IN ('RetryUntilExpiredThenSkip')),
    CONSTRAINT "FK_custom_announcement_delivery_policies_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_commands" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_commands" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "ModeratorOnly" INTEGER NOT NULL,
    "CooldownSeconds" INTEGER NOT NULL,
    "CooldownScope" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "AK_custom_commands_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "CK_custom_commands_CooldownScope" CHECK (CooldownScope IN ('Global', 'User')),
    CONSTRAINT "FK_custom_commands_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_counters" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_counters" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "AK_custom_counters_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "FK_custom_counters_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_message_library_entries" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_message_library_entries" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "SelectionMode" TEXT NOT NULL,
    "CurrentVariantIndex" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "AK_custom_message_library_entries_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "CK_custom_message_library_entries_SelectionMode" CHECK (SelectionMode IN ('First', 'Random', 'Sequential')),
    CONSTRAINT "FK_custom_message_library_entries_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "durable_alerts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_durable_alerts" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Severity" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "SourceKey" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Message" TEXT NOT NULL,
    "LinkPath" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "AcknowledgedAtUtc" TEXT NULL,
    "AcknowledgedByLogin" TEXT NULL,
    CONSTRAINT "CK_durable_alerts_Severity" CHECK (Severity IN ('Critical', 'Info', 'Warning')),
    CONSTRAINT "FK_durable_alerts_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "guess_round_profiles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_guess_round_profiles" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "Revision" INTEGER NOT NULL DEFAULT 0,
    "WinningGuessPointReward" TEXT NOT NULL DEFAULT '0',
    CONSTRAINT "AK_guess_round_profiles_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "FK_guess_round_profiles_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "host_mod_access_entries" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_host_mod_access_entries" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Login" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "CK_host_mod_access_entries_Kind" CHECK (Kind IN ('blacklist', 'whitelist')),
    CONSTRAINT "FK_host_mod_access_entries_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "host_mod_access_settings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_host_mod_access_settings" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "ModsEnabled" INTEGER NOT NULL,
    "AllowModsByDefault" INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT "FK_host_mod_access_settings_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "point_balances" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_point_balances" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Login" TEXT NOT NULL,
    "Amount" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_point_balances_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "point_ledger_entries" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_point_ledger_entries" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "Login" TEXT NOT NULL,
    "Delta" TEXT NOT NULL,
    "BalanceAfter" TEXT NOT NULL,
    "ActorLogin" TEXT NULL,
    "CounterpartyLogin" TEXT NULL,
    "GiveawayId" INTEGER NULL,
    "Note" TEXT NOT NULL,
    CONSTRAINT "CK_point_ledger_entries_Kind" CHECK (Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin')),
    CONSTRAINT "FK_point_ledger_entries_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "points_giveaways" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_points_giveaways" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "StartedAtUtc" TEXT NOT NULL,
    "EndsAtUtc" TEXT NOT NULL,
    "CompletedAtUtc" TEXT NULL,
    "MinimumPayout" TEXT NOT NULL,
    "MaximumPayout" TEXT NOT NULL,
    "WinnerCount" INTEGER NOT NULL,
    "Eligibility" TEXT NOT NULL,
    CONSTRAINT "CK_points_giveaways_Eligibility" CHECK (Eligibility IN ('everyone', 'followers', 'subscribers')),
    CONSTRAINT "CK_points_giveaways_Status" CHECK (Status IN ('Active', 'Cancelled', 'Completed', 'Expired')),
    CONSTRAINT "FK_points_giveaways_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "points_settings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_points_settings" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "PointLabel" TEXT NOT NULL,
    "GamblingWinRatePercent" INTEGER NOT NULL,
    "GamblingCooldownSeconds" INTEGER NOT NULL,
    "GiveawayDurationSeconds" INTEGER NOT NULL,
    "GiveawayMinimumPayout" TEXT NOT NULL,
    "GiveawayMaximumPayout" TEXT NOT NULL,
    "GiveawayWinnerCount" INTEGER NOT NULL,
    "GiveawayEligibility" TEXT NOT NULL,
    "GiveawayCooldownSeconds" INTEGER NOT NULL,
    "BalanceReply" TEXT NOT NULL,
    "OtherBalanceReply" TEXT NOT NULL,
    "TransferReply" TEXT NOT NULL,
    "AddReply" TEXT NOT NULL,
    "RemoveReply" TEXT NOT NULL,
    "InvalidAmountReply" TEXT NOT NULL,
    "InsufficientBalanceReply" TEXT NOT NULL,
    "ModeratorOnlyReply" TEXT NOT NULL,
    "GamblingWinReply" TEXT NOT NULL,
    "GamblingLoseReply" TEXT NOT NULL,
    "GiveawayStartedReply" TEXT NOT NULL,
    "GiveawayUpdateReply" TEXT NOT NULL,
    "GiveawayJoinedReply" TEXT NOT NULL,
    "GiveawayAlreadyJoinedReply" TEXT NOT NULL,
    "GiveawayEndedReply" TEXT NOT NULL,
    "GiveawayNoEntrantsReply" TEXT NOT NULL,
    "GiveawayCancelledReply" TEXT NOT NULL,
    "GiveawayAlreadyActiveReply" TEXT NOT NULL,
    "GiveawayNotActiveReply" TEXT NOT NULL,
    "GiveawayCooldownReply" TEXT NOT NULL,
    "StreamOfflineReply" TEXT NOT NULL,
    "NotEligibleReply" TEXT NOT NULL,
    "FollowerChecksUnavailableReply" TEXT NOT NULL,
    CONSTRAINT "CK_points_settings_GiveawayEligibility" CHECK (GiveawayEligibility IN ('everyone', 'followers', 'subscribers')),
    CONSTRAINT "FK_points_settings_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "reply_delivery_settings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_reply_delivery_settings" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Feature" TEXT NOT NULL,
    "ScopeId" INTEGER NOT NULL,
    "ReplyKey" TEXT NOT NULL,
    "Target" TEXT NOT NULL,
    CONSTRAINT "CK_reply_delivery_settings_Feature" CHECK (Feature IN ('guessing', 'points')),
    CONSTRAINT "CK_reply_delivery_settings_Target" CHECK (Target IN ('chat', 'whisper')),
    CONSTRAINT "FK_reply_delivery_settings_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "whisper_quota_buckets" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_whisper_quota_buckets" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "BotTwitchUserId" TEXT NOT NULL,
    "DayUtc" TEXT NOT NULL,
    "Exhausted" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_whisper_quota_buckets_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_command_aliases" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_command_aliases" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "CustomCommandId" INTEGER NOT NULL,
    "Alias" TEXT NOT NULL,
    CONSTRAINT "FK_custom_command_aliases_custom_commands_HostId_CustomCommandId" FOREIGN KEY ("HostId", "CustomCommandId") REFERENCES "custom_commands" ("HostId", "Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_announcements" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_announcements" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "MessageLibraryEntryId" INTEGER NOT NULL,
    "DeliveryPolicyId" INTEGER NOT NULL,
    "LastSentAtUtc" TEXT NULL,
    "LastOccurrenceAtUtc" TEXT NULL,
    "OccurrenceStatus" TEXT NOT NULL,
    "OccurrenceDueAtUtc" TEXT NULL,
    "OccurrenceExpiresAtUtc" TEXT NULL,
    "OccurrenceNextAttemptAtUtc" TEXT NULL,
    "OccurrenceCompletedAtUtc" TEXT NULL,
    "OccurrenceAttemptCount" INTEGER NOT NULL,
    "OccurrenceMessage" TEXT NULL,
    "ChatMessagesSinceLastSent" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NOT NULL, DeliveryType TEXT NOT NULL DEFAULT 'ChatMessage', AnnouncementColor TEXT NOT NULL DEFAULT 'Primary', LatestDeliveryResult TEXT NOT NULL DEFAULT 'None',
    CONSTRAINT "AK_custom_announcements_HostId_Id" UNIQUE ("HostId", "Id"),
    CONSTRAINT "CK_custom_announcements_OccurrenceState" CHECK ((OccurrenceStatus = 'None' AND OccurrenceDueAtUtc IS NULL AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'Pending' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NOT NULL AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'Attempting' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR (OccurrenceStatus = 'RetryScheduled' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc >= OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR (OccurrenceStatus IN ('Accepted', 'TerminalRejected', 'TerminalAmbiguous', 'TerminalUnexpected') AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount > 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'SkippedExpired' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount >= 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'TerminalMissingMessage' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'TerminalInvalidTimeZone' AND OccurrenceDueAtUtc IS NULL AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL)),
    CONSTRAINT "CK_custom_announcements_OccurrenceStatus" CHECK (OccurrenceStatus IN ('Accepted', 'Attempting', 'None', 'Pending', 'RetryScheduled', 'SkippedExpired', 'TerminalAmbiguous', 'TerminalInvalidTimeZone', 'TerminalMissingMessage', 'TerminalRejected', 'TerminalUnexpected')),
    CONSTRAINT "FK_custom_announcements_custom_announcement_delivery_policies_HostId_DeliveryPolicyId" FOREIGN KEY ("HostId", "DeliveryPolicyId") REFERENCES "custom_announcement_delivery_policies" ("HostId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_custom_announcements_custom_message_library_entries_HostId_MessageLibraryEntryId" FOREIGN KEY ("HostId", "MessageLibraryEntryId") REFERENCES "custom_message_library_entries" ("HostId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_custom_announcements_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_command_actions" (
    "CustomCommandId" INTEGER NOT NULL CONSTRAINT "PK_custom_command_actions" PRIMARY KEY,
    "HostId" INTEGER NOT NULL,
    "MessageLibraryEntryId" INTEGER NOT NULL,
    "ActionType" TEXT NOT NULL,
    "CounterId" INTEGER NULL,
    CONSTRAINT "CK_custom_command_actions_ActionType" CHECK (ActionType IN ('Counter', 'Message')),
    CONSTRAINT "CK_custom_command_actions_Payload" CHECK ((ActionType = 'Message' AND CounterId IS NULL) OR (ActionType = 'Counter' AND CounterId IS NOT NULL)),
    CONSTRAINT "FK_custom_command_actions_custom_commands_HostId_CustomCommandId" FOREIGN KEY ("HostId", "CustomCommandId") REFERENCES "custom_commands" ("HostId", "Id") ON DELETE CASCADE,
    CONSTRAINT "FK_custom_command_actions_custom_counters_HostId_CounterId" FOREIGN KEY ("HostId", "CounterId") REFERENCES "custom_counters" ("HostId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_custom_command_actions_custom_message_library_entries_HostId_MessageLibraryEntryId" FOREIGN KEY ("HostId", "MessageLibraryEntryId") REFERENCES "custom_message_library_entries" ("HostId", "Id") ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS "custom_message_variants" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_custom_message_variants" PRIMARY KEY AUTOINCREMENT,
    "CustomMessageLibraryEntryId" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    CONSTRAINT "FK_custom_message_variants_custom_message_library_entries_CustomMessageLibraryEntryId" FOREIGN KEY ("CustomMessageLibraryEntryId") REFERENCES "custom_message_library_entries" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "command_aliases" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_command_aliases" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "GuessRoundProfileId" INTEGER NULL,
    "Kind" TEXT NOT NULL,
    "Alias" TEXT NOT NULL,
    CONSTRAINT "CK_command_aliases_Kind" CHECK (Kind IN ('AddPoints', 'CancelGiveaway', 'EndGiveaway', 'Gamble', 'Giveaway', 'GivePoints', 'Guess', 'Guesses', 'Join', 'Points', 'RemovePoints', 'Start', 'Stop', 'Win')),
    CONSTRAINT "FK_command_aliases_guess_round_profiles_HostId_GuessRoundProfileId" FOREIGN KEY ("HostId", "GuessRoundProfileId") REFERENCES "guess_round_profiles" ("HostId", "Id") ON DELETE CASCADE,
    CONSTRAINT "FK_command_aliases_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "guess_options" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_guess_options" PRIMARY KEY AUTOINCREMENT,
    "GuessRoundProfileId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "ReplyText" TEXT NOT NULL,
    "SortOrder" INTEGER NOT NULL DEFAULT 0,
    "ReplyTarget" TEXT NOT NULL DEFAULT 'chat',
    CONSTRAINT "CK_guess_options_ReplyTarget" CHECK (ReplyTarget IN ('chat', 'whisper')),
    CONSTRAINT "FK_guess_options_guess_round_profiles_GuessRoundProfileId" FOREIGN KEY ("GuessRoundProfileId") REFERENCES "guess_round_profiles" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "guess_rounds" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_guess_rounds" PRIMARY KEY AUTOINCREMENT,
    "HostId" INTEGER NOT NULL,
    "GuessRoundProfileId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "StartedAtUtc" TEXT NOT NULL,
    "ClosedAtUtc" TEXT NULL,
    "WinningName" TEXT NULL,
    CONSTRAINT "CK_guess_rounds_Status" CHECK (Status IN ('Closed', 'Completed', 'Open')),
    CONSTRAINT "FK_guess_rounds_guess_round_profiles_GuessRoundProfileId" FOREIGN KEY ("GuessRoundProfileId") REFERENCES "guess_round_profiles" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_guess_rounds_hosts_HostId" FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "reply_settings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_reply_settings" PRIMARY KEY AUTOINCREMENT,
    "GuessRoundProfileId" INTEGER NOT NULL,
    "RoundStartedReply" TEXT NOT NULL,
    "RoundAlreadyOpenReply" TEXT NOT NULL,
    "NoOpenRoundReply" TEXT NOT NULL,
    "GuessingStoppedReply" TEXT NOT NULL,
    "GuessingAlreadyStoppedReply" TEXT NOT NULL,
    "GuessingClosedReply" TEXT NOT NULL,
    "InvalidGuessReply" TEXT NOT NULL,
    "GuessUsageReply" TEXT NOT NULL,
    "AvailableGuessesReply" TEXT NOT NULL,
    "WinUsageReply" TEXT NOT NULL,
    "ModeratorOnlyReply" TEXT NOT NULL,
    "WinnerReply" TEXT NOT NULL,
    "NoWinnersReply" TEXT NOT NULL,
    CONSTRAINT "FK_reply_settings_guess_round_profiles_GuessRoundProfileId" FOREIGN KEY ("GuessRoundProfileId") REFERENCES "guess_round_profiles" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "points_giveaway_entrants" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_points_giveaway_entrants" PRIMARY KEY AUTOINCREMENT,
    "GiveawayId" INTEGER NOT NULL,
    "Login" TEXT NOT NULL,
    "JoinedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_points_giveaway_entrants_points_giveaways_GiveawayId" FOREIGN KEY ("GiveawayId") REFERENCES "points_giveaways" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "points_giveaway_winners" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_points_giveaway_winners" PRIMARY KEY AUTOINCREMENT,
    "GiveawayId" INTEGER NOT NULL,
    "Login" TEXT NOT NULL,
    "Payout" TEXT NOT NULL,
    CONSTRAINT "FK_points_giveaway_winners_points_giveaways_GiveawayId" FOREIGN KEY ("GiveawayId") REFERENCES "points_giveaways" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "whisper_quota_recipients" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_whisper_quota_recipients" PRIMARY KEY AUTOINCREMENT,
    "WhisperQuotaBucketId" INTEGER NOT NULL,
    "RecipientTwitchUserId" TEXT NOT NULL,
    "RecipientLogin" TEXT NOT NULL,
    "FirstSentAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_whisper_quota_recipients_whisper_quota_buckets_WhisperQuotaBucketId" FOREIGN KEY ("WhisperQuotaBucketId") REFERENCES "whisper_quota_buckets" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "custom_announcement_schedules" (
    "CustomAnnouncementId" INTEGER NOT NULL CONSTRAINT "PK_custom_announcement_schedules" PRIMARY KEY,
    "HostId" INTEGER NOT NULL,
    "ScheduleType" TEXT NOT NULL,
    "IntervalMinutes" INTEGER NULL,
    "RequiredChatMessages" INTEGER NULL,
    "WeeklyDay" INTEGER NULL,
    "WeeklyTime" TEXT NULL,
    CONSTRAINT "CK_custom_announcement_schedules_Payload" CHECK ((ScheduleType = 'Interval' AND IntervalMinutes >= 1 AND RequiredChatMessages IS NULL AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR (ScheduleType = 'IntervalAfterChat' AND IntervalMinutes >= 1 AND RequiredChatMessages >= 1 AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR (ScheduleType = 'Weekly' AND IntervalMinutes IS NULL AND RequiredChatMessages IS NULL AND WeeklyDay BETWEEN 0 AND 6 AND WeeklyTime IS NOT NULL)),
    CONSTRAINT "CK_custom_announcement_schedules_ScheduleType" CHECK (ScheduleType IN ('Interval', 'IntervalAfterChat', 'Weekly')),
    CONSTRAINT "FK_custom_announcement_schedules_custom_announcements_HostId_CustomAnnouncementId" FOREIGN KEY ("HostId", "CustomAnnouncementId") REFERENCES "custom_announcements" ("HostId", "Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "guess_votes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_guess_votes" PRIMARY KEY AUTOINCREMENT,
    "GuessRoundId" INTEGER NOT NULL,
    "Login" TEXT NOT NULL,
    "GuessName" TEXT NOT NULL,
    "GuessedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_guess_votes_guess_rounds_GuessRoundId" FOREIGN KEY ("GuessRoundId") REFERENCES "guess_rounds" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX "IX_command_aliases_HostId_Alias" ON "command_aliases" ("HostId", "Alias");
CREATE INDEX "IX_command_aliases_HostId_GuessRoundProfileId" ON "command_aliases" ("HostId", "GuessRoundProfileId");
CREATE UNIQUE INDEX "IX_custom_announcement_schedules_HostId_CustomAnnouncementId" ON "custom_announcement_schedules" ("HostId", "CustomAnnouncementId");
CREATE UNIQUE INDEX "IX_custom_announcements_HostId_DeliveryPolicyId" ON "custom_announcements" ("HostId", "DeliveryPolicyId");
CREATE INDEX "IX_custom_announcements_HostId_MessageLibraryEntryId" ON "custom_announcements" ("HostId", "MessageLibraryEntryId");
CREATE UNIQUE INDEX "IX_custom_announcements_HostId_Name" ON "custom_announcements" ("HostId", "Name");
CREATE INDEX "IX_custom_command_actions_HostId_CounterId" ON "custom_command_actions" ("HostId", "CounterId");
CREATE UNIQUE INDEX "IX_custom_command_actions_HostId_CustomCommandId" ON "custom_command_actions" ("HostId", "CustomCommandId");
CREATE INDEX "IX_custom_command_actions_HostId_MessageLibraryEntryId" ON "custom_command_actions" ("HostId", "MessageLibraryEntryId");
CREATE UNIQUE INDEX "IX_custom_command_aliases_HostId_Alias" ON "custom_command_aliases" ("HostId", "Alias");
CREATE INDEX "IX_custom_command_aliases_HostId_CustomCommandId" ON "custom_command_aliases" ("HostId", "CustomCommandId");
CREATE UNIQUE INDEX "IX_custom_commands_HostId_Name" ON "custom_commands" ("HostId", "Name");
CREATE UNIQUE INDEX "IX_custom_counters_HostId_Name" ON "custom_counters" ("HostId", "Name");
CREATE UNIQUE INDEX "IX_custom_message_library_entries_HostId_Name" ON "custom_message_library_entries" ("HostId", "Name");
CREATE UNIQUE INDEX "IX_custom_message_variants_CustomMessageLibraryEntryId_SortOrder" ON "custom_message_variants" ("CustomMessageLibraryEntryId", "SortOrder");
CREATE INDEX "IX_durable_alerts_HostId_AcknowledgedAtUtc" ON "durable_alerts" ("HostId", "AcknowledgedAtUtc");
CREATE UNIQUE INDEX "IX_durable_alerts_HostId_Source_SourceKey" ON "durable_alerts" ("HostId", "Source", "SourceKey") WHERE "AcknowledgedAtUtc" IS NULL;
CREATE UNIQUE INDEX "IX_guess_options_GuessRoundProfileId_Name" ON "guess_options" ("GuessRoundProfileId", "Name");
CREATE UNIQUE INDEX "IX_guess_round_profiles_HostId" ON "guess_round_profiles" ("HostId") WHERE "IsDefault" = 1;
CREATE UNIQUE INDEX "IX_guess_round_profiles_HostId_Slug" ON "guess_round_profiles" ("HostId", "Slug");
CREATE INDEX "IX_guess_rounds_GuessRoundProfileId" ON "guess_rounds" ("GuessRoundProfileId");
CREATE UNIQUE INDEX "IX_guess_rounds_HostId" ON "guess_rounds" ("HostId") WHERE "Status" IN ('Open', 'Closed');
CREATE UNIQUE INDEX "IX_guess_votes_GuessRoundId_Login" ON "guess_votes" ("GuessRoundId", "Login");
CREATE UNIQUE INDEX "IX_host_mod_access_entries_HostId_Kind_Login" ON "host_mod_access_entries" ("HostId", "Kind", "Login");
CREATE UNIQUE INDEX "IX_host_mod_access_settings_HostId" ON "host_mod_access_settings" ("HostId");
CREATE UNIQUE INDEX "IX_hosts_Login" ON "hosts" ("Login");
CREATE UNIQUE INDEX "IX_point_balances_HostId_Login" ON "point_balances" ("HostId", "Login");
CREATE INDEX "IX_point_ledger_entries_HostId_CreatedAtUtc" ON "point_ledger_entries" ("HostId", "CreatedAtUtc");
CREATE UNIQUE INDEX "IX_points_giveaway_entrants_GiveawayId_Login" ON "points_giveaway_entrants" ("GiveawayId", "Login");
CREATE INDEX "IX_points_giveaway_winners_GiveawayId" ON "points_giveaway_winners" ("GiveawayId");
CREATE UNIQUE INDEX "IX_points_giveaways_HostId" ON "points_giveaways" ("HostId") WHERE "Status" = 'Active';
CREATE UNIQUE INDEX "IX_points_settings_HostId" ON "points_settings" ("HostId");
CREATE UNIQUE INDEX "IX_public_chat_outbox_ClaimSlot" ON "public_chat_outbox" ("ClaimSlot") WHERE "ClaimSlot" IS NOT NULL;
CREATE UNIQUE INDEX "IX_public_chat_outbox_ClaimToken" ON "public_chat_outbox" ("ClaimToken") WHERE "ClaimToken" IS NOT NULL;
CREATE INDEX "IX_public_chat_outbox_Status_ClaimExpiresAtUtc" ON "public_chat_outbox" ("Status", "ClaimExpiresAtUtc");
CREATE INDEX "IX_public_chat_outbox_Status_ExpiresAtUtc" ON "public_chat_outbox" ("Status", "ExpiresAtUtc");
CREATE INDEX "IX_public_chat_outbox_Status_NextAttemptAtUtc_CreatedAtUtc_Id" ON "public_chat_outbox" ("Status", "NextAttemptAtUtc", "CreatedAtUtc", "Id");
CREATE INDEX "IX_public_chat_send_receipts_AttemptedAtUtc" ON "public_chat_send_receipts" ("AttemptedAtUtc");
CREATE INDEX "IX_public_chat_send_receipts_DeliveredAtUtc" ON "public_chat_send_receipts" ("DeliveredAtUtc");
CREATE UNIQUE INDEX "IX_reply_delivery_settings_HostId_Feature_ScopeId_ReplyKey" ON "reply_delivery_settings" ("HostId", "Feature", "ScopeId", "ReplyKey");
CREATE UNIQUE INDEX "IX_reply_settings_GuessRoundProfileId" ON "reply_settings" ("GuessRoundProfileId");
CREATE UNIQUE INDEX "IX_site_access_entries_Kind_Login" ON "site_access_entries" ("Kind", "Login");
CREATE UNIQUE INDEX "IX_whisper_quota_buckets_HostId_BotTwitchUserId_DayUtc" ON "whisper_quota_buckets" ("HostId", "BotTwitchUserId", "DayUtc");
CREATE UNIQUE INDEX "IX_whisper_quota_recipients_WhisperQuotaBucketId_RecipientTwitchUserId" ON "whisper_quota_recipients" ("WhisperQuotaBucketId", "RecipientTwitchUserId");
CREATE TABLE IF NOT EXISTS "host_bot_account_settings" (
    Id INTEGER NOT NULL
        CONSTRAINT PK_host_bot_account_settings PRIMARY KEY AUTOINCREMENT,
    HostId INTEGER NOT NULL,
    OverrideEnabled INTEGER NOT NULL,
    WhisperResponsesEnabled INTEGER NOT NULL,
    TwitchUserId TEXT NULL,
    Login TEXT NULL,
    DisplayName TEXT NULL,
    ProfileImageUrl TEXT NULL,
    ProtectedTokenPayload BLOB NULL,
    AuthorizedAtUtc TEXT NULL,
    AuthorizedScopes TEXT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    CONSTRAINT FK_host_bot_account_settings_hosts_HostId
        FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IX_host_bot_account_settings_HostId
    ON host_bot_account_settings (HostId);
