namespace BarberSaas.Api.Filters;

// Keeps a frequent, uninteresting write (e.g. marking a chat read) out of ActivityLogFilter's log.
[AttributeUsage(AttributeTargets.Method)]
public class SkipActivityLogAttribute : Attribute;
