namespace BarberSaas.Api.Filters;

// Marker attribute -- an action carrying this is exempt from RequirePasswordChangeFilter's
// lockout, e.g. AuthController.ChangePassword itself (it has to be reachable while the lockout
// is in effect, or the business could never clear it).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AllowWithPendingPasswordChangeAttribute : Attribute;
