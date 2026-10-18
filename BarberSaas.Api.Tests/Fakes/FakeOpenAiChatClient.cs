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
    // How many times the canned tool is called in one turn (the real model can repeat a call).
    public int ToolCallRepeat { get; set; } = 1;
    // Thrown after the tool calls ran, like an OpenAI failure on the follow-up request.
    public Exception? ThrowAfterTool { get; set; }

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
            var toolResult = "";
            for (var i = 0; i < ToolCallRepeat; i++)
                toolResult = await executeToolAsync(ToolCallName, ToolCallArgsJson);
            if (ThrowAfterTool is not null) throw ThrowAfterTool;
            return $"AI: {toolResult}";
        }

        return PlainTextReply ?? "AI: default reply";
    }
}
