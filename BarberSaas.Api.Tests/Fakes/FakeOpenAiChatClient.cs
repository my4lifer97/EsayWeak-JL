using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

// Real OpenAI calls would dial out to a service tests shouldn't depend on -- configurable per test
// to return a canned tool call, canned plain text, or throw (to exercise WhatsAppController's
// fallback-to-rule-based path). Only ever constructed/used by tests that explicitly set
// OpenAI:ApiKey (see WhatsAppAiChatbotTests) -- everything else falls through to the rule-based
// flow, same as when OpenAI isn't configured at all.
public class FakeOpenAiChatClient : IOpenAiChatClient
{
    public string? ToolCallName { get; set; }
    public string ToolCallArgsJson { get; set; } = "{}";
    public string? PlainTextReply { get; set; }
    public Exception? ThrowOnCall { get; set; }

    public List<(string SystemPrompt, IReadOnlyList<OpenAiTurn> History, string UserMessage)> Calls { get; } = [];

    public async Task<string> GetReplyAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiTurn> history,
        string userMessage,
        IReadOnlyList<OpenAiToolDefinition> tools,
        Func<string, string, Task<string>> executeToolAsync)
    {
        Calls.Add((systemPrompt, history, userMessage));
        if (ThrowOnCall is not null) throw ThrowOnCall;

        if (ToolCallName is not null)
        {
            var toolResult = await executeToolAsync(ToolCallName, ToolCallArgsJson);
            return $"AI: {toolResult}";
        }

        return PlainTextReply ?? "AI: default reply";
    }
}
