using System;
using System.Collections.Generic;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using BIBIM_MVP;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Property-based tests for session data models.
    /// Feature: chat-session-management
    /// </summary>
    public class SessionModelsPropertyTests
    {
        /// <summary>
        /// Property 3: Session JSON Structure Completeness
        /// For any saved session, the JSON representation should contain all required fields:
        /// sessionId (non-empty), title (string), messages (array), createdAt (valid timestamp), updatedAt (valid timestamp).
        /// Validates: Requirements 2.4
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionJsonStructureCompleteness()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                Arb.From<string>(),
                Arb.From<string>(),
                (NonEmptyString sessionId, string title, string revitVersion) =>
                {
                    // Arrange: Create a session with generated values
                    var session = new ChatSession
                    {
                        SessionId = sessionId.Get,
                        Title = title ?? "",
                        RevitVersion = revitVersion ?? "",
                        CreatedAt = DateTime.UtcNow.AddDays(-1),
                        UpdatedAt = DateTime.UtcNow,
                        Messages = new List<MessagePair>
                        {
                            new MessagePair
                            {
                                UserPrompt = "Test prompt",
                                AiResponse = "Test response",
                                PythonCode = "print('hello')",
                                SequenceOrder = 1,
                                CreatedAt = DateTime.UtcNow
                            }
                        }
                    };

                    // Act: Serialize to JSON
                    var json = JsonSerializer.Serialize(session);
                    var jsonDoc = JsonDocument.Parse(json);
                    var root = jsonDoc.RootElement;

                    // Assert: All required fields exist and have correct types
                    var hasSessionId = root.TryGetProperty("sessionId", out var sessionIdProp) 
                        && sessionIdProp.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(sessionIdProp.GetString());

                    var hasTitle = root.TryGetProperty("title", out var titleProp)
                        && titleProp.ValueKind == JsonValueKind.String;

                    var hasMessages = root.TryGetProperty("messages", out var messagesProp)
                        && messagesProp.ValueKind == JsonValueKind.Array;

                    var hasCreatedAt = root.TryGetProperty("createdAt", out var createdAtProp)
                        && createdAtProp.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(createdAtProp.GetString(), out _);

                    var hasUpdatedAt = root.TryGetProperty("updatedAt", out var updatedAtProp)
                        && updatedAtProp.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(updatedAtProp.GetString(), out _);

                    return hasSessionId && hasTitle && hasMessages && hasCreatedAt && hasUpdatedAt;
                });
        }
    }
}
