using OpenAI.Chat;

namespace BarberSaas.Api.Services;

// OpenAI:ApiKey/OpenAI:Model, same no-default-in-appsettings.json secret pattern as
// Twilio:AuthToken/CronSecret -- WhatsAppController only ever constructs/calls this when
// OpenAI:ApiKey is set (see ProcessMessageAsync's dispatcher), so a missing key never reaches here.
public class OpenAiChatClient(IConfiguration config) : IOpenAiChatClient
{
    // Safety net against a runaway tool-call loop -- a well-behaved model resolves in 1-2 round
    // trips (decide to call a tool, then compose final text from the result).
    private const int MaxToolRoundTrips = 4;

    public async Task<string> GetReplyAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiTurn> history,
        string userMessage,
        IReadOnlyList<OpenAiToolDefinition> tools,
        Func<string, string, Task<string>> executeToolAsync)
    {
        var client = new ChatClient(config["OpenAI:Model"] ?? "gpt-4o-mini", config["OpenAI:ApiKey"]);

        List<ChatMessage> messages = [new SystemChatMessage(systemPrompt)];
        foreach (var turn in history)
            messages.Add(turn.Role == "assistant" ? new AssistantChatMessage(turn.Content) : new UserChatMessage(turn.Content));
        messages.Add(new UserChatMessage(userMessage));

        var options = new ChatCompletionOptions();
        foreach (var tool in tools)
            options.Tools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.ParametersJsonSchema)));

        for (var i = 0; i < MaxToolRoundTrips; i++)
        {
            ChatCompletion completion = await client.CompleteChatAsync(messages, options);

            if (completion.FinishReason != ChatFinishReason.ToolCalls)
                return completion.Content.Count > 0 ? completion.Content[0].Text : "";

            messages.Add(new AssistantChatMessage(completion));
            foreach (var toolCall in completion.ToolCalls)
            {
                var result = await executeToolAsync(toolCall.FunctionName, toolCall.FunctionArguments.ToString());
                messages.Add(new ToolChatMessage(toolCall.Id, result));
            }
        }

        throw new InvalidOperationException("OpenAI tool-call loop did not resolve within the retry cap");
    }
}
