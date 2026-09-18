namespace BarberSaas.Api.Services;

// A single persisted conversation turn (see WhatsAppConversationState.HistoryJson) -- Role is
// "user" or "assistant". Never carries tool-call plumbing; that's resolved entirely within one
// GetReplyAsync call and never persisted across messages.
public record OpenAiTurn(string Role, string Content);

public record OpenAiToolDefinition(string Name, string Description, string ParametersJsonSchema);

// Purely a transport wrapper around the OpenAI Chat Completions API -- no business logic or DB
// access here (mirrors IWhatsAppBridgeClient). Tool execution (which needs DB access) is supplied
// by the caller as executeToolAsync; this client just drives the tool-call round trip until the
// model produces final text.
public interface IOpenAiChatClient
{
    Task<string> GetReplyAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiTurn> history,
        string userMessage,
        IReadOnlyList<OpenAiToolDefinition> tools,
        Func<string, string, Task<string>> executeToolAsync);
}
